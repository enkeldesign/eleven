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

        // ?tier=elite: the CPU opponent's difficulty (#83); pro unless given.
        private CpuSkill _opponentSkill = CpuSkill.Pro;

        private bool _match;
        private bool _matchPaused;
        private MatchRecorder _recorder;
        private Vector2 _previousOpponent;
        private Vector2 _previousOpponentKeeper;
        private int _lastClockSecond = -1;

        /// <summary>Whether a match is being played rather than practice.</summary>
        public bool InMatch => _match;

        private void ReadMatchOptions(string url)
        {
            int halfAt = url.IndexOf("half=", StringComparison.Ordinal);
            if (halfAt >= 0 && double.TryParse(url.Substring(halfAt + 5).Split('&', '#')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double half))
            {
                _halfLength = Math.Max(60, Math.Min(600, half));
            }

            int seedAt = url.IndexOf("seed=", StringComparison.Ordinal);
            if (seedAt >= 0 && ulong.TryParse(url.Substring(seedAt + 5).Split('&', '#')[0], out ulong seed)) _matchSeed = seed;
            int tierAt = url.IndexOf("tier=", StringComparison.Ordinal);
            CpuSkill? tier = tierAt >= 0 ? CpuSkill.Named(url.Substring(tierAt + 5).Split('&', '#')[0]) : null;
            if (tier.HasValue) _opponentSkill = tier.Value;
        }

        // The bench's values, with the half's length asked for in the address.
        private SimConfig MatchConfig() => _halfLength > 0 ? _bench.Config.With("halfLength", _halfLength) : _bench.Config;

        /// <summary>Starts a match at its kick-off, or a new one: the bench's values, the human's footballer.</summary>
        private void StartMatch(string via)
        {
            _match = true;
            _matchPaused = false;
            _config = MatchConfig();
            _view.Use(_config);
            _view.ShowMatch(true);
            _simulator = new Simulator(_config, OneVOne.Match(_config, _matchSeed, home: _bench.Capabilities, awayCpu: _opponentSkill));
            _recording = CommandRecording.StartFrom(_simulator);
            _feel.Start(_simulator);
            _recorder = new MatchRecorder();
            _input.Clear();
            _lastClockSecond = -1;
            OrientForSide();
            RememberPrevious();
            _resultLines = HudLayout.MaxResultLines;
            UpdateMatchPanel();
            UpdateLayout(force: false);
            _hud?.SetMatchActions(true, false);
            _hud?.ShowStatus("1v1 match: kick-off. Attacking up.");
            Log(FormattableString.Invariant($"ELEVEN match started via={via} seed={_matchSeed} half={_config.HalfLength:F0}s tier={TierName(_opponentSkill)} config={_config.Hash:x16} start={_simulator.StateHash():x16}"));
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
            _resultLines = 0;
            _hud?.HideMatch();
            _hud?.SetMatchActions(false, false);
            if (_orientation != PitchOrientation.Practice) SwitchView(false);
            ResetPractice();
            UpdateLayout(force: false);
            Log("ELEVEN match ended to=practice");
        }

        /// <summary>The bench's play switch (#87): 0 practice, 1 a 1v1 match.</summary>
        private void SwitchPlay(int index)
        {
            if (index == 1 && !_match) StartMatch("bench");
            else if (index == 0 && _match) EndMatch();
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
                    case SimEventKind.FullTime:
                        _hud?.ShowStatus($"Full time {Score(s)}");
                        break;
                }
            }

            if (_recorder.Result != null && _recorder.Result != _shownResult)
            {
                _shownResult = _recorder.Result;
                _hud?.ShowMatchResult(_shownResult.HomeGoals, _shownResult.AwayGoals);
                Log($"ELEVEN match result {_shownResult.ToText().Replace('\n', ' ').Trim()}");
                Log($"ELEVEN match recording steps={_recording.StepCount} replay={(_recording.Replay(_config).StateHash() == _simulator.StateHash() ? "match" : "MISMATCH")}");
            }

            int second = (int)(s.Match.ClockTicks * _config.TickSeconds);
            if (second != _lastClockSecond)
            {
                _lastClockSecond = second;
                UpdateMatchPanel();
                if (_trace && second % 10 == 0) Log(FormattableString.Invariant($"ELEVEN match clock phase={s.Match.Phase} clock={s.Match.ClockTicks} score={Score(s)} tick={s.Tick}"));
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
                MatchPhase.FullTime => "Full time",
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
