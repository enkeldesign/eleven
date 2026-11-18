using System;
using Eleven.Simulation;
using Eleven.Simulation.Harness;
using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// The playable 1v1 match (#87): the human's outfielder and his keeper against a CPU
    /// outfielder and his, from the kick-off to full time. The match is the simulation's: its
    /// referee, clock and halves (#85, #86). The host starts it, sends the human's commands with
    /// the AI controllers', shows the score and the clock, turns the view when the human's side
    /// changes ends, and pauses it. Practice stays as it was.
    /// </summary>
    public sealed partial class HostLoop
    {
        // ?seed=: the match's seed, for a reproducible match.
        private ulong _matchSeed = 0x87;

        // ?half=60: a half's length in seconds of match clock, 60 to 600 (#86's range).
        private double _halfLength;

        // ?knockout=1 (#111): a level match goes to extra time and then a shootout.
        private bool _knockout;

        // ?tier=elite: the CPU opponent's difficulty (#83); pro unless given.
        private CpuSkill _opponentSkill = CpuSkill.Pro;

        private bool _match;
        private bool _matchPaused;
        private MatchRecorder _recorder;
        private int _lastClockSecond = -1;

        // Outfielders a side (#95, #103): 1 for the 1v1 match, 2 for 2v2, 5 for 5v5.
        private int _perSide = 1;

        // The bench's play switch, by segment (#103): practice, then the match sizes.
        private static readonly int[] PlaySizes = { 0, 1, 2, 5, 10 };

        // Who the human controls in a 2v2 match (#93): automatic at the ball's changes of hands, and
        // SWITCH's own request, which the next step's command carries.
        private SwitchController _switcher;
        private bool _switchRequested;

        // How automatic switching is (#136): the bench's switch or ?switching=auto|ball|manual. The
        // teammate SWITCH would take, marked; and why the switcher last moved control on its own,
        // cued once the switch is made.
        private SwitchMode _switchMode = SwitchMode.Auto;
        private int _nextId;
        private SwitchCause _cueCause;

        private static readonly string[] SwitchModeNames = { "auto", "ball", "manual" };

        private static string NameOf(SwitchMode mode) => SwitchModeNames[(int)mode];

        // The bench's switching switch (#136): it acts at once, and the match goes on.
        private void SwitchSwitching(int index)
        {
            _switchMode = (SwitchMode)Math.Max(0, Math.Min(2, index));
            if (_switcher != null) _switcher.Mode = _switchMode;
            _hud?.ShowStatus(_switchMode == SwitchMode.Auto ? "Switching: auto." : _switchMode == SwitchMode.OnTheBall ? "Switching: on the ball only." : "Switching: manual, on SWITCH only.");
            Log($"ELEVEN match switching mode={NameOf(_switchMode)}");
        }

        // The cue for a switch the human didn't ask for (#136), in words.
        private static string CueText(SwitchCause cause) => cause switch
        {
            SwitchCause.Pass => "Switched: your pass's receiver.",
            SwitchCause.MateHasBall => "Switched: your teammate on the ball.",
            SwitchCause.Turnover => "Switched: first to the lost ball.",
            _ => "",
        };

        // SUBS's request (#110): kept until the simulation makes the substitution, at the next
        // stoppage. The policy that picks who: the manager's own, with no wait and no threshold.
        private bool _subRequested;
        private SimConfig _subPolicy;

        // A match's name: outfielders a side, or eleven a side counting the keeper, as football says.
        private static string SideName(int perSide) => perSide >= 10 ? "11v11" : $"{perSide}v{perSide}";

        // The step's cost in the browser (#106): each simulation step timed, reported every five
        // seconds of match clock in trace.
        private readonly System.Diagnostics.Stopwatch _stepWatch = new System.Diagnostics.Stopwatch();
        private double _stepMsTotal, _stepMsMax;
        private int _stepsTimed;

        private void TimeStep(double ms)
        {
            _stepMsTotal += ms;
            _stepMsMax = Math.Max(_stepMsMax, ms);
            _stepsTimed++;
        }

        private void ReportBudget(int second)
        {
            if (!_trace || _stepsTimed == 0) return;
            Log(FormattableString.Invariant($"ELEVEN match budget second={second} side={_perSide} steps={_stepsTimed} meanMs={_stepMsTotal / _stepsTimed:F3} maxMs={_stepMsMax:F3}"));
            _stepMsTotal = _stepMsMax = 0;
            _stepsTimed = 0;
        }

        /// <summary>The play switch's segment for what is being played.</summary>
        private int PlayIndex => _match ? Math.Max(1, Array.IndexOf(PlaySizes, _perSide)) : 0;
        private int _switches;

        /// <summary>Whether a match is being played rather than practice.</summary>
        public bool InMatch => _match;

        private void ReadMatchOptions(string url)
        {
            int halfAt = url.IndexOf("half=", StringComparison.Ordinal);
            if (halfAt >= 0 && double.TryParse(url.Substring(halfAt + 5).Split('&', '#')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double half))
            {
                _halfLength = Math.Max(60, Math.Min(600, half));
            }

            _knockout = url.Contains("knockout=1");
            int seedAt = url.IndexOf("seed=", StringComparison.Ordinal);
            if (seedAt >= 0 && ulong.TryParse(url.Substring(seedAt + 5).Split('&', '#')[0], out ulong seed)) _matchSeed = seed;
            int tierAt = url.IndexOf("tier=", StringComparison.Ordinal);
            CpuSkill? tier = tierAt >= 0 ? CpuSkill.Named(url.Substring(tierAt + 5).Split('&', '#')[0]) : null;
            if (tier.HasValue) _opponentSkill = tier.Value;
        }

        // The bench's values, with the half's length asked for in the address.
        private SimConfig MatchConfig()
        {
            SimConfig config = _halfLength > 0 ? _bench.Config.With("halfLength", _halfLength) : _bench.Config;
            return _knockout ? config.With("knockout", 1) : config;
        }

        /// <summary>
        /// Starts a match at its kick-off, or a new one: the bench's values, the human's footballer,
        /// <paramref name="perSide"/> outfielders a side (#95). At one a side it is M1's 1v1 match.
        /// </summary>
        private void StartMatch(string via, int perSide)
        {
            _match = true;
            _matchPaused = false;
            _perSide = perSide >= 10 ? 10 : perSide >= 5 ? 5 : Math.Max(1, Math.Min(2, perSide));
            _config = MatchConfig();
            _view.Use(_config);
            _view.ShowMatch(true, _perSide);
            _simulator = new Simulator(_config, _perSide == 1
                ? OneVOne.Match(_config, _matchSeed, home: _bench.Capabilities, awayCpu: _opponentSkill)
                : Squads.Match(_config, _matchSeed, _perSide, awayCpu: _opponentSkill, home: _bench.Capabilities));
            _switcher = new SwitchController(OneVOne.HomeController, _switchMode);
            _switchRequested = false;
            _nextId = 0;
            _cueCause = SwitchCause.None;
            _switches = 0;
            _hud?.ShowSwitch(_perSide > 1);
            _subRequested = false;
            _subPolicy = _config.With("cpuSubAfter", 0).With("cpuSubFatigue", 0);
            _hud?.ShowSubs(SubsLeft());
            _recording = CommandRecording.StartFrom(_simulator);
            _feel.Start(_simulator);
            _poses.Clear();
            _recorder = new MatchRecorder();
            _input.Clear();
            _lastClockSecond = -1;
            OrientForSide();
            RememberPrevious();
            _resultLines = HudLayout.MaxResultLines;
            UpdateMatchPanel();
            UpdateLayout(force: false);
            _hud?.SetMatchActions(true, false);
            _hud?.ShowStatus($"{SideName(_perSide)} match: kick-off. Attacking up.");
            Log(FormattableString.Invariant($"ELEVEN match started via={via} seed={_matchSeed} half={_config.HalfLength:F0}s tier={TierName(_opponentSkill)} config={_config.Hash:x16} start={_simulator.StateHash():x16} side={_perSide}"));
        }

        /// <summary>Back to practice: the bench's values from the standard practice start.</summary>
        private void EndMatch()
        {
            if (!_match) return;
            _match = false;
            _matchPaused = false;
            _config = _bench.Config;
            _view.Use(_config);
            _view.ShowMatch(false);
            _perSide = 1;
            _hud?.ShowSwitch(false);
            _subRequested = false;
            _hud?.ShowSubs(false);
            _resultLines = 0;
            _hud?.HideMatch();
            _hud?.SetMatchActions(false, false);
            if (_orientation != PitchOrientation.Practice) SwitchView(false);
            ResetPractice();
            UpdateLayout(force: false);
            Log("ELEVEN match ended to=practice");
        }

        /// <summary>The bench's play switch (#87, #95, #103, #112): 0 practice, 1 a 1v1 match, 2 a 2v2 match, 3 a 5v5 match, 4 eleven a side.</summary>
        private void SwitchPlay(int index)
        {
            if (index == 0)
            {
                if (_match) EndMatch();
                return;
            }

            int size = PlaySizes[Math.Max(0, Math.Min(PlaySizes.Length - 1, index))];
            if (!_match || _perSide != size) StartMatch("bench", size);
        }

        // The footballer the human's controller owns: in practice the only one; in a match
        // whoever he has switched to (#93).
        private int Controlled(SimState s)
        {
            if (!_match) return 0;
            int i = Array.FindIndex(s.Players, p => p.OwnerControllerId == OneVOne.HomeController);
            return i >= 0 ? i : 0;
        }

        private Vector2 Previous(int index, SimState s) =>
            index < _previousAll.Length ? _previousAll[index] : ToVector(s.Players[index].Position);

        // Everyone but the controlled footballer and his keeper, between their last two steps.
        private readonly System.Collections.Generic.List<Vector2> _mates = new System.Collections.Generic.List<Vector2>();
        private readonly System.Collections.Generic.List<Vector2> _opponents = new System.Collections.Generic.List<Vector2>();

        private void RenderSides(SimState s, int me, float alpha)
        {
            _mates.Clear();
            _opponents.Clear();
            Vector2 theirKeeper = Vector2.zero;
            Vector2? next = null;
            for (int i = 0; i < s.Players.Length; i++)
            {
                if (i == me) continue;
                PlayerState p = s.Players[i];
                bool keeper = Array.FindIndex(s.Keepers, k => k.PlayerId == p.Id) >= 0;
                // Off the pitch (#108, #109): drawn far beyond it, out of the view and the cues' reach.
                Vector2 at = p.Off ? new Vector2(0, 1000) : Vector2.Lerp(Previous(i, s), ToVector(p.Position), alpha);
                if (p.Id == _nextId && !p.Off) next = at;
                if (p.TeamId == OneVOne.HomeTeam)
                {
                    if (!keeper) _mates.Add(at);
                }
                else if (keeper) theirKeeper = at;
                else _opponents.Add(at);
            }

            _view.RenderSides(_mates, _opponents, theirKeeper);
            _view.ShowNext(_perSide > 1 ? next : null);
        }

        // Each footballer's action this frame (#101), chosen from the state and never written back.
        private readonly ActionPoses _poses = new ActionPoses();
        private readonly System.Collections.Generic.List<PitchView.Drawn> _matePoses = new System.Collections.Generic.List<PitchView.Drawn>();
        private readonly System.Collections.Generic.List<PitchView.Drawn> _opponentPoses = new System.Collections.Generic.List<PitchView.Drawn>();

        private void DrawPoses(SimState s, int me)
        {
            _matePoses.Clear();
            _opponentPoses.Clear();
            bool still = UiRoot.ReducedMotion;
            PitchView.Drawn Of(int i)
            {
                Eleven.Presentation.Pose pose = _poses.Of(s, i, _config, still);
                return new PitchView.Drawn(pose, ActionPoses.Facing(s, i, pose));
            }

            PitchView.Drawn mine = Of(me), ownKeeper = default, theirKeeper = default;
            int home = s.Players[me].TeamId;
            for (int i = 0; i < s.Players.Length; i++)
            {
                if (i == me) continue;
                bool keeper = Array.FindIndex(s.Keepers, k => k.PlayerId == s.Players[i].Id) >= 0;
                if (s.Players[i].TeamId == home)
                {
                    if (keeper) ownKeeper = Of(i);
                    else _matePoses.Add(Of(i));
                }
                else if (keeper) theirKeeper = Of(i);
                else _opponentPoses.Add(Of(i));
            }

            _view.SetPoses(mine, ownKeeper, _matePoses, _opponentPoses, theirKeeper);
        }

        // The human's command for this step, with the switch the switcher decides on the last
        // step's events and SWITCH's request (#93): recorded with it, so a replay needs no switcher.
        private Command WithSwitch(Command c)
        {
            if (!_match || _perSide < 2 || _switcher == null) return c;
            bool manual = _switchRequested;
            _switchRequested = false;
            int target = _switcher.Decide(_simulator, _events, manual);
            if (manual) Log(FormattableString.Invariant($"ELEVEN match switch asked tick={_simulator.Tick} to={target}"));
            if (target == 0) return c;
            _cueCause = manual ? SwitchCause.None : _switcher.LastCause;
            if (!manual) Log(FormattableString.Invariant($"ELEVEN match switch auto cause={_switcher.LastCause} mode={NameOf(_switchMode)} tick={_simulator.Tick} to={target}"));
            return new Command(c.ControllerId, c.Tick, c.Move, c.Kick, c.Aftertouch, c.QuickPass, c.Dive, c.Plan, target);
        }

        // Whether the human's side has a substitute and a substitution left (#110).
        private bool SubsLeft()
        {
            if (!_match) return false;
            SimState s = _simulator.PeekState();
            if (s.Bench.Length == 0 || s.Match.Phase == MatchPhase.FullTime) return false;
            TeamState home = Array.Find(s.Teams, t => t.Id == OneVOne.HomeTeam);
            return home.Substitutions < _config.Substitutions && Array.FindIndex(s.Bench, b => b.TeamId == OneVOne.HomeTeam && !b.Used) >= 0;
        }

        // SUBS (#110): ask for a substitution at the next stoppage; a second press takes it back.
        private void AskSubstitution()
        {
            if (!SubsLeft()) return;
            _subRequested = !_subRequested;
            _hud?.ShowStatus(_subRequested ? "Substitution at the next stoppage." : "Substitution called off.");
            Log(FormattableString.Invariant($"ELEVEN match substitution {(_subRequested ? "asked" : "called-off")} tick={_simulator.Tick}"));
        }

        // The human's command with the substitution asked for (#110), once the simulation would
        // make it: the manager's pick, an injured footballer first, then the most tired, for the
        // first substitute. Recorded with the command, so a replay needs no policy.
        private Command WithSubstitution(Command c)
        {
            if (!_match || !_subRequested) return c;
            SimState s = _simulator.PeekState();
            (int off, int on) = Eleven.Simulation.AI.ManagerBrain.Decide(ManagerControllers.Observe(s, _subPolicy, OneVOne.HomeTeam), _subPolicy);
            return off == 0 ? c : c.WithSubstitution(off, on);
        }

        private void TogglePause()
        {
            if (!_match) return;
            _matchPaused = !_matchPaused;
            _accumulator = 0;
            _input.Clear();
            _hud?.SetMatchActions(true, _matchPaused);
            _hud?.ShowStatus(_matchPaused ? "Paused. The clock has stopped." : "Play on.");
            Log(FormattableString.Invariant($"ELEVEN match {(_matchPaused ? "paused" : "resumed")} tick={_simulator.Tick} clock={_simulator.PeekState().Match.ClockTicks}"));
        }

        // The match's AI controllers, after the human's command (#83, #85): recorded with it.
        private void AppendMatchControllers()
        {
            if (!_match) return;
            CpuControllers.Append(_simulator, _commands);
            RestartControllers.Append(_simulator, _commands);

            // The CPU side's manager (#110); the human's side is his own to manage, with SUBS.
            ManagerControllers.Append(_simulator, _commands);
        }

        // After each step: the result, the announcements, the side's end and the panel.
        private void ObserveMatch()
        {
            if (!_match) return;
            SimState s = _simulator.PeekState();
            _recorder.Observe(_config, s, _events);
            foreach (SimEvent e in _events)
            {
                switch (e.Kind)
                {
                    case SimEventKind.Goal:
                        _hud?.ShowStatus(FormattableString.Invariant($"GOAL! {(e.Other == OneVOne.HomeTeam ? "You" : "CPU")} {Score(s)}"));
                        Log(FormattableString.Invariant($"ELEVEN match goal team={e.Other} score={Score(s)} tick={s.Tick}"));
                        break;
                    case SimEventKind.HalfTime:
                        _hud?.ShowStatus($"Half-time {Score(s)}");
                        Log(FormattableString.Invariant($"ELEVEN match halftime score={Score(s)} tick={s.Tick}"));
                        break;
                    case SimEventKind.SecondHalf:
                        OrientForSide();
                        _hud?.ShowStatus("Second half: ends changed. Attacking up.");
                        Log(FormattableString.Invariant($"ELEVEN match secondhalf tick={s.Tick} attack={HumanAttack(s)} orientation={_orientation}"));
                        break;
                    case SimEventKind.ExtraTime:
                        // Extra time (#111): level after two halves, then each of its halves, the ends changed.
                        if (e.Other > 0) OrientForSide();
                        _hud?.ShowStatus(e.Other == 0 ? $"Level at {Score(s)}: extra time." : e.Other == 1 ? "Extra time: ends changed. Attacking up." : "Extra time, second half. Attacking up.");
                        Log(FormattableString.Invariant($"ELEVEN match extratime part={e.Other} score={Score(s)} tick={s.Tick} attack={HumanAttack(s)} orientation={_orientation}"));
                        UpdateMatchPanel();
                        break;
                    case SimEventKind.ShootoutStarted:
                        _hud?.ShowStatus($"Level at {Score(s)}: penalties.");
                        Log(FormattableString.Invariant($"ELEVEN match shootout started score={Score(s)} tick={s.Tick}"));
                        UpdateMatchPanel();
                        break;
                    case SimEventKind.ShootoutKick:
                    {
                        // A shootout's kick (#111): whose, whether it scored, and the kicks' score.
                        int kicker = Array.Find(s.Players, p => p.Id == e.Subject).TeamId;
                        TeamState you = Array.Find(s.Teams, t => t.Id == OneVOne.HomeTeam), cpu = Array.Find(s.Teams, t => t.Id != OneVOne.HomeTeam);
                        _hud?.ShowStatus(FormattableString.Invariant($"{(kicker == OneVOne.HomeTeam ? "You" : "CPU")}: {(e.Other == 1 ? "scored" : "missed")}. Penalties {you.ShootoutGoals}-{cpu.ShootoutGoals}"));
                        Log(FormattableString.Invariant($"ELEVEN match shootout kick player={e.Subject} team={kicker} scored={e.Other} kicks={you.ShootoutKicks}-{cpu.ShootoutKicks} goals={you.ShootoutGoals}-{cpu.ShootoutGoals} tick={s.Tick}"));

                        // The clock stands still in a shootout: the panel's penalties change by the kick.
                        UpdateMatchPanel();
                        break;
                    }

                    case SimEventKind.Injured:
                    {
                        // Injuries (#109), announced with the side.
                        int hurt = Array.Find(s.Players, p => p.Id == e.Subject).TeamId;
                        string whose = hurt == OneVOne.HomeTeam ? "Your" : "CPU";
                        _hud?.ShowStatus(e.Other == (int)InjuryLevel.Serious ? $"{whose} footballer injured and off." : $"{whose} footballer hurt, playing on.");
                        Log(FormattableString.Invariant($"ELEVEN match injury level={e.Other} player={e.Subject} team={hurt} tick={s.Tick}"));
                        break;
                    }

                    case SimEventKind.Cautioned:
                    case SimEventKind.SentOff:
                    {
                        // Cards (#108), announced with the side.
                        int side = Array.Find(s.Players, p => p.Id == e.Subject).TeamId;
                        string who = side == OneVOne.HomeTeam ? "Your" : "CPU";
                        _hud?.ShowStatus(e.Kind == SimEventKind.SentOff ? $"Red card: {who} footballer sent off." : $"Yellow card: {who} footballer cautioned.");
                        Log(FormattableString.Invariant($"ELEVEN match card kind={(e.Kind == SimEventKind.SentOff ? "red" : "yellow")} player={e.Subject} team={side} tick={s.Tick}"));
                        break;
                    }

                    case SimEventKind.Substituted:
                    {
                        // Substitutions (#110), announced with the side. The players' places in the
                        // state have moved, so what was remembered of them by place starts again.
                        int side = Array.Find(s.Players, p => p.Id == e.Subject).TeamId;
                        bool mine = side == OneVOne.HomeTeam;
                        if (mine) _subRequested = false;
                        _poses.Clear();
                        RememberPrevious();
                        _hud?.ShowSubs(SubsLeft());
                        _hud?.ShowStatus(mine ? "Substitution: your side." : "Substitution: CPU.");
                        Log(FormattableString.Invariant($"ELEVEN match substituted on={e.Subject} off={e.Other} team={side} made={Array.Find(s.Teams, t => t.Id == side).Substitutions} tick={s.Tick}"));
                        break;
                    }

                    case SimEventKind.Switched:
                        // A gesture under way was for the footballer he left (#93).
                        _input.Clear();
                        _switches++;
                        Log(FormattableString.Invariant($"ELEVEN match switched to={e.Subject} from={e.Other} count={_switches} tick={s.Tick}"));

                        // Control moved on its own (#136): the marker swells, and the HUD says why.
                        if (_cueCause != SwitchCause.None)
                        {
                            _view.Pulse();
                            _hud?.ShowStatus(CueText(_cueCause));
                        }

                        _cueCause = SwitchCause.None;
                        break;
                    case SimEventKind.FullTime:
                        _subRequested = false;
                        _hud?.ShowSubs(false);
                        _hud?.ShowStatus($"Full time {Score(s)}");
                        UpdateMatchPanel();
                        Log(FormattableString.Invariant($"ELEVEN match fulltime score={Score(s)} tick={s.Tick}"));
                        break;
                }
            }

            if (_recorder.Result != null && _recorder.Result != _shownResult)
            {
                _shownResult = _recorder.Result;
                if (_shownResult.HomeGoals != _shownResult.AwayGoals) _hud?.ShowMatchResult(_shownResult.HomeGoals, _shownResult.AwayGoals);
                else _hud?.ShowMatchResult(_shownResult.HomeShootoutGoals, _shownResult.AwayShootoutGoals);
                Log($"ELEVEN match result {_shownResult.ToText().Replace('\n', ' ').Trim()}");
                Log($"ELEVEN match recording steps={_recording.StepCount} replay={(_recording.Replay(_config).StateHash() == _simulator.StateHash() ? "match" : "MISMATCH")}");
            }

            int second = (int)(s.Match.ClockTicks * _config.TickSeconds);
            if (second != _lastClockSecond)
            {
                _lastClockSecond = second;
                UpdateMatchPanel();
                if (second > 0 && second % 5 == 0) ReportBudget(second);
                if (_trace && second % 10 == 0) Log(FormattableString.Invariant($"ELEVEN match clock phase={s.Match.Phase} clock={s.Match.ClockTicks} score={Score(s)} tick={s.Tick}"));
                // The off-screen cues (#102), each second: how many, and where, inside the play area.
                if (_trace)
                {
                    Log(FormattableString.Invariant($"ELEVEN cues second={second} shown={_view.CuesShown} {_view.CueReport()}"));
                    (float shown, float fitted) = _view.TakeZoomTravel();
                    Log(FormattableString.Invariant($"ELEVEN zoom second={second} travel={shown:F3} fitted={fitted:F3}"));
                }
            }
        }

        private MatchResult _shownResult;

        // The human's side attacks up the screen in both halves (#72): the view turns with the ends.
        private void OrientForSide()
        {
            bool turned = HumanAttack(_simulator.PeekState()) < 0;
            if (turned != (_orientation != PitchOrientation.Practice)) SwitchView(turned);
        }

        private static int HumanAttack(SimState s) => Array.Find(s.Teams, t => t.Id == OneVOne.HomeTeam).AttackSign;

        private static string Score(SimState s) => FormattableString.Invariant($"{s.Teams[0].Goals}-{s.Teams[1].Goals}");

        private void UpdateMatchPanel()
        {
            if (!_match || _hud == null) return;
            SimState s = _simulator.PeekState();
            int clock = (int)(s.Match.ClockTicks * _config.TickSeconds);
            string half = s.Match.Phase switch
            {
                MatchPhase.FirstHalf => "1st half",
                MatchPhase.HalfTime => "Half-time",
                MatchPhase.SecondHalf => "2nd half",
                MatchPhase.ExtraBreak => "Extra time",
                MatchPhase.ExtraFirstHalf => "Extra 1st",
                MatchPhase.ExtraHalfTime => "Extra time",
                MatchPhase.ExtraSecondHalf => "Extra 2nd",
                MatchPhase.Shootout => FormattableString.Invariant($"Pens {s.Teams[0].ShootoutGoals}-{s.Teams[1].ShootoutGoals}"),
                // The verdict by goals, or by the shootout's kicks when the goals are level (#111).
                MatchPhase.FullTime => s.Teams[0].Goals != s.Teams[1].Goals
                    ? Hud.FullTime(s.Teams[0].Goals, s.Teams[1].Goals)
                    : Hud.FullTime(s.Teams[0].ShootoutGoals, s.Teams[1].ShootoutGoals),
                _ => "",
            };
            _hud.ShowMatch($"You {s.Teams[0].Goals} – {s.Teams[1].Goals} CPU", FormattableString.Invariant($"{clock / 60}:{clock % 60:00}"), half);
        }

        private static string TierName(CpuSkill skill) =>
            skill.DecisionInterval == CpuSkill.Elite.DecisionInterval ? "elite"
            : skill.DecisionInterval == CpuSkill.Club.DecisionInterval ? "club"
            : skill.DecisionInterval == CpuSkill.Apprentice.DecisionInterval ? "apprentice" : "pro";
    }
}
