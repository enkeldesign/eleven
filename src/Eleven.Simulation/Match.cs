// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eleven.Simulation
{
    /// <summary>
    /// The match's clock and halves (#86), as rules over the state, after the restart sequencer on
    /// each step. The clock runs only while the ball is in play: a dead ball, the half-time break
    /// and full time never advance it, so there is no stoppage time. At half-time the sides change
    /// ends, by their attack signs, and the side that didn't kick off the first half kicks off the
    /// second. docs/simulation/match.md.
    /// </summary>
    public sealed partial class Simulator
    {
        // Whether the match is between its halves or over: then the ball is dead.
        private bool MatchPaused => MatchBreak || _state.Match.Phase == MatchPhase.FullTime;

        // Whether the match is in a break between halves (#86, #111): half-time, or one of extra time's.
        private bool MatchBreak => _state.Match.Phase == MatchPhase.HalfTime || _state.Match.Phase == MatchPhase.ExtraBreak || _state.Match.Phase == MatchPhase.ExtraHalfTime;

        private void StepMatch(long tick, List<SimEvent> events)
        {
            ref MatchState m = ref _state.Match;
            switch (m.Phase)
            {
                case MatchPhase.FirstHalf:
                case MatchPhase.SecondHalf:
                case MatchPhase.ExtraFirstHalf:
                case MatchPhase.ExtraSecondHalf:
                    if (_state.Restart.Phase != RestartPhase.Live) return;
                    bool extra = m.Phase == MatchPhase.ExtraFirstHalf || m.Phase == MatchPhase.ExtraSecondHalf;
                    if (++m.ClockTicks < (extra ? Config.ExtraTicks : Config.HalfTicks)) return;
                    _state.Ball.AttachedTo = 0;

                    // The whistle ends any kick under way: nothing is played until the next kick-off.
                    for (int i = 0; i < _state.Players.Length; i++)
                    {
                        ref PlayerState p = ref _state.Players[i];
                        p.WindUpTicks = p.AftertouchTicks = 0;
                        p.WindUpDirection = p.WindUpStrength = p.WindUpLoft = p.AftertouchDirection = 0;
                        p.WindUpTarget = 0;
                    }
                    if (m.Phase == MatchPhase.FirstHalf)
                    {
                        m.Phase = MatchPhase.HalfTime;
                        m.PhaseTicks = Config.HalfTimeTicks;
                        CloseSubstitutionWindows();

                        // Half-time's rest (#100): a share of every footballer's fatigue goes.
                        for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].Fatigue *= 1 - Config.HalfTimeRecovery;
                        events.Add(new SimEvent(tick, SimEventKind.HalfTime, 0));
                    }
                    else if (m.Phase == MatchPhase.SecondHalf && Config.Knockout >= 0.5 && Level())
                    {
                        // A knockout level after two halves (#111): a break, then extra time.
                        m.Phase = MatchPhase.ExtraBreak;
                        m.PhaseTicks = Config.HalfTimeTicks;
                        CloseSubstitutionWindows();
                        events.Add(new SimEvent(tick, SimEventKind.ExtraTime, 0));
                    }
                    else if (m.Phase == MatchPhase.ExtraFirstHalf)
                    {
                        m.Phase = MatchPhase.ExtraHalfTime;
                        m.PhaseTicks = Config.HalfTimeTicks;
                        CloseSubstitutionWindows();
                    }
                    else if (m.Phase == MatchPhase.ExtraSecondHalf && Level())
                    {
                        StartShootout(tick, events);
                    }
                    else
                    {
                        m.Phase = MatchPhase.FullTime;
                        events.Add(new SimEvent(tick, SimEventKind.FullTime, 0));
                    }

                    return;

                case MatchPhase.ExtraBreak:
                case MatchPhase.ExtraHalfTime:
                {
                    // Extra time's halves (#111): the ends change before each, and the side that
                    // kicked off the match kicks off the first, the other the second.
                    if (--m.PhaseTicks > 0) return;
                    bool first = m.Phase == MatchPhase.ExtraBreak;
                    for (int i = 0; i < _state.Teams.Length; i++) _state.Teams[i].AttackSign = -_state.Teams[i].AttackSign;
                    m.Phase = first ? MatchPhase.ExtraFirstHalf : MatchPhase.ExtraSecondHalf;
                    CloseSubstitutionWindows();
                    m.ClockTicks = 0;
                    events.Add(new SimEvent(tick, SimEventKind.ExtraTime, 0, other: first ? 1 : 2));
                    Stop(RestartKind.KickOff, first ? m.FirstKickOff : Opponents(m.FirstKickOff), Vec2.Zero, tick, events);
                    return;
                }

                case MatchPhase.Shootout:
                    StepShootout(tick, events);
                    return;

                case MatchPhase.HalfTime:
                    if (--m.PhaseTicks > 0) return;
                    for (int i = 0; i < _state.Teams.Length; i++) _state.Teams[i].AttackSign = -_state.Teams[i].AttackSign;
                    m.Phase = MatchPhase.SecondHalf;
                    CloseSubstitutionWindows();
                    m.ClockTicks = 0;
                    events.Add(new SimEvent(tick, SimEventKind.SecondHalf, 0));
                    Stop(RestartKind.KickOff, Opponents(m.FirstKickOff), Vec2.Zero, tick, events);
                    return;
            }
        }

        // A goal in a match is credited to the side that scored it (#86).
        private void Credit(int team)
        {
            if (_state.Match.Phase == MatchPhase.None) return;
            for (int i = 0; i < _state.Teams.Length; i++)
            {
                if (_state.Teams[i].Id == team) _state.Teams[i].Goals++;
            }
        }

        private static void ValidateMatch(SimState state, SimConfig config)
        {
            MatchState m = state.Match;
            if (m.Phase > MatchPhase.Shootout) throw new ArgumentException("Unknown match phase.", nameof(state));
            foreach (TeamState t in state.Teams)
            {
                if (t.Goals < 0) throw new ArgumentOutOfRangeException(nameof(state), $"Side {t.Id} has {t.Goals} goals.");
                if (t.Goals > 0 && m.Phase == MatchPhase.None) throw new ArgumentException("Goals are kept only in a match.", nameof(state));
            }

            if (m.Phase == MatchPhase.None)
            {
                if (m.ClockTicks != 0 || m.PhaseTicks != 0 || m.FirstKickOff != 0) throw new ArgumentException("Without a match there is no clock.", nameof(state));
                return;
            }

            if (!state.Restart.On) throw new ArgumentException("A match needs its referee: the restart sequencer on.", nameof(state));
            if (Array.FindIndex(state.Teams, t => t.Id == m.FirstKickOff) < 0) throw new ArgumentException($"The first kick-off's side {m.FirstKickOff} isn't one of the two.", nameof(state));
            if (m.ClockTicks < 0 || m.ClockTicks > config.HalfTicks) throw new ArgumentOutOfRangeException(nameof(state), $"The clock at {m.ClockTicks} steps is outside the half's [0, {config.HalfTicks}].");
            bool rest = m.Phase == MatchPhase.HalfTime || m.Phase == MatchPhase.ExtraBreak || m.Phase == MatchPhase.ExtraHalfTime;
            int most = rest ? config.HalfTimeTicks : m.Phase == MatchPhase.Shootout ? config.ShootoutKickTicks : 0;
            if (m.PhaseTicks < 0 || m.PhaseTicks > most) throw new ArgumentOutOfRangeException(nameof(state), $"The break's {m.PhaseTicks} steps left are outside [0, {most}].");
        }
    }

    /// <summary>A goal in a match's result (#86).</summary>
    public readonly struct ResultGoal
    {
        public ResultGoal(int team, int scorer, int half, double clockSeconds)
        {
            Team = team;
            Scorer = scorer;
            Half = half;
            ClockSeconds = clockSeconds;
        }

        /// <summary>The side credited with it.</summary>
        public int Team { get; }

        /// <summary>The footballer who last played the ball: for an own goal, a defender.</summary>
        public int Scorer { get; }

        /// <summary>1 or 2.</summary>
        public int Half { get; }

        /// <summary>s on the half's clock.</summary>
        public double ClockSeconds { get; }
    }

    /// <summary>
    /// A match's result (#86, INV-14): the stable contract a consumer reads, a career or a results
    /// screen, instead of the simulation's state or events. Plain values, built once at full time,
    /// with its own schema version and text form. Its fields change only with that version.
    /// </summary>
    public sealed class MatchResult
    {
        /// <summary>Bumped whenever a field is added or changes meaning.</summary>
        public const int SchemaVersion = 7;

        public MatchResult(int homeTeam, int awayTeam, int homeGoals, int awayGoals, double halfSeconds, double playedSeconds, IReadOnlyList<ResultGoal> goals, int restarts,
            int homeFouls = 0, int awayFouls = 0, int homePenalties = 0, int awayPenalties = 0,
            int homeCautions = 0, int awayCautions = 0, int homeSendingsOff = 0, int awaySendingsOff = 0,
            int homeInjuries = 0, int awayInjuries = 0, int homeSubstitutions = 0, int awaySubstitutions = 0,
            int extraTime = 0, int homeShootoutKicks = 0, int awayShootoutKicks = 0, int homeShootoutGoals = 0, int awayShootoutGoals = 0)
        {
            ExtraTime = extraTime;
            HomeShootoutKicks = homeShootoutKicks;
            AwayShootoutKicks = awayShootoutKicks;
            HomeShootoutGoals = homeShootoutGoals;
            AwayShootoutGoals = awayShootoutGoals;
            HomeSubstitutions = homeSubstitutions;
            AwaySubstitutions = awaySubstitutions;
            HomeInjuries = homeInjuries;
            AwayInjuries = awayInjuries;
            HomeCautions = homeCautions;
            AwayCautions = awayCautions;
            HomeSendingsOff = homeSendingsOff;
            AwaySendingsOff = awaySendingsOff;
            HomeFouls = homeFouls;
            AwayFouls = awayFouls;
            HomePenalties = homePenalties;
            AwayPenalties = awayPenalties;
            HomeTeam = homeTeam;
            AwayTeam = awayTeam;
            HomeGoals = homeGoals;
            AwayGoals = awayGoals;
            HalfSeconds = halfSeconds;
            PlayedSeconds = playedSeconds;
            Goals = goals;
            Restarts = restarts;
        }

        public int HomeTeam { get; }
        public int AwayTeam { get; }
        public int HomeGoals { get; }
        public int AwayGoals { get; }

        /// <summary>s, a half's length on the match clock.</summary>
        public double HalfSeconds { get; }

        /// <summary>s of match clock played: both halves.</summary>
        public double PlayedSeconds { get; }

        /// <summary>Every goal, in the order scored.</summary>
        public IReadOnlyList<ResultGoal> Goals { get; }

        /// <summary>Restarts taken, kick-offs included.</summary>
        public int Restarts { get; }

        /// <summary>Fouls each side committed (schema 2, #99).</summary>
        public int HomeFouls { get; }

        public int AwayFouls { get; }

        /// <summary>Penalties each side was awarded (schema 2, #99).</summary>
        public int HomePenalties { get; }

        public int AwayPenalties { get; }

        /// <summary>Cautions each side was shown, and footballers each had sent off (schema 3, #108).</summary>
        public int HomeCautions { get; }

        public int AwayCautions { get; }

        public int HomeSendingsOff { get; }

        public int AwaySendingsOff { get; }

        /// <summary>Injuries each side's footballers suffered, of either level (schema 4, #109).</summary>
        public int HomeInjuries { get; }

        public int AwayInjuries { get; }

        /// <summary>Substitutes each side brought on (schema 5, #110).</summary>
        public int HomeSubstitutions { get; }

        public int AwaySubstitutions { get; }

        /// <summary>1 when extra time was played, else 0 (schema 6, #111): then <see cref="PlayedSeconds"/> includes it, and a goal's half may be 3 or 4.</summary>
        public int ExtraTime { get; }

        /// <summary>Kicks each side took in a shootout, and scored (schema 6, #111): all 0 when there was none.</summary>
        public int HomeShootoutKicks { get; }

        public int AwayShootoutKicks { get; }

        public int HomeShootoutGoals { get; }

        public int AwayShootoutGoals { get; }

        /// <summary>Each side's match metrics (schema 7, #128).</summary>
        public SideMetrics HomeMetrics { get; internal set; }

        public SideMetrics AwayMetrics { get; internal set; }

        /// <summary>The winner's side id, or 0 for a draw: by goals, then by the shootout (schema 6, #111).</summary>
        public int Winner => HomeGoals != AwayGoals ? (HomeGoals > AwayGoals ? HomeTeam : AwayTeam)
            : HomeShootoutGoals != AwayShootoutGoals ? (HomeShootoutGoals > AwayShootoutGoals ? HomeTeam : AwayTeam) : 0;

        /// <summary>
        /// The same match seen from the other ground (#167): every home field the away one's and
        /// back, the metrics too. The goals keep their sides' ids, so the winner is the same side.
        /// </summary>
        public MatchResult Mirrored() => this;

        /// <summary>The text form: one <c>name=value</c> line each, then one <c>goal=</c> line per goal. Invariant culture, stable.</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("eleven-result/").Append(SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append(string.Format(CultureInfo.InvariantCulture, "home={0}\naway={1}\nscore={2}-{3}\nhalf={4:F0}\nplayed={5:F2}\nrestarts={6}\nfouls={7}-{8}\npenalties={9}-{10}\ncautions={11}-{12}\nsendingsoff={13}-{14}\ninjuries={15}-{16}\nsubstitutions={17}-{18}\nextratime={19}\nshootout={20}-{21}\nshootoutkicks={22}-{23}\nwinner={24}\n",
                HomeTeam, AwayTeam, HomeGoals, AwayGoals, HalfSeconds, PlayedSeconds, Restarts, HomeFouls, AwayFouls, HomePenalties, AwayPenalties,
                HomeCautions, AwayCautions, HomeSendingsOff, AwaySendingsOff, HomeInjuries, AwayInjuries, HomeSubstitutions, AwaySubstitutions,
                ExtraTime, HomeShootoutGoals, AwayShootoutGoals, HomeShootoutKicks, AwayShootoutKicks, Winner));
            text.Append(string.Format(CultureInfo.InvariantCulture, "possession={0:F3}-{1:F3}\nshots={2}-{3}\nontarget={4}-{5}\nxg={6:F2}-{7:F2}\npasses={8}-{9}\npassescompleted={10}-{11}\ncorners={12}-{13}\nppda={14:F2}-{15:F2}\n",
                HomeMetrics.Possession, AwayMetrics.Possession, HomeMetrics.Shots, AwayMetrics.Shots, HomeMetrics.OnTarget, AwayMetrics.OnTarget, HomeMetrics.Xg, AwayMetrics.Xg,
                HomeMetrics.Passes, AwayMetrics.Passes, HomeMetrics.PassesCompleted, AwayMetrics.PassesCompleted, HomeMetrics.Corners, AwayMetrics.Corners, HomeMetrics.Ppda, AwayMetrics.Ppda));
            foreach (ResultGoal g in Goals) text.Append(string.Format(CultureInfo.InvariantCulture, "goal={0},{1},{2},{3:F2}\n", g.Team, g.Scorer, g.Half, g.ClockSeconds));
            return text.ToString();
        }
    }

    /// <summary>
    /// Builds a <see cref="MatchResult"/> from a match as it is played (#86): fed each step's
    /// events and state, it keeps the goals; at full time it gives the result. A reader of the
    /// match, never a part of it: replaying the same commands builds the same result.
    /// </summary>
    public sealed class MatchRecorder
    {
        private readonly List<ResultGoal> _goals = new List<ResultGoal>();
        private int _restarts;
        private int _homeFouls, _awayFouls, _homePenalties, _awayPenalties;
        private int _homeCautions, _awayCautions, _homeSendingsOff, _awaySendingsOff, _homeInjuries, _awayInjuries;
        private bool _extraTime;

        // The metrics (#128), by side: [0] home, [1] away.
        private readonly MetricsModel _model;
        private readonly long[] _possessed = new long[2];
        private readonly int[] _shots = new int[2], _onTarget = new int[2], _passes = new int[2], _completed = new int[2], _corners = new int[2];
        private readonly int[] _ownSixtyPasses = new int[2], _actions = new int[2];
        private readonly double[] _xg = new double[2];
        private int _inPossession = -1;
        private int _shooter = -1, _passer = -1;
        private double _shotChance;

        // The side whose shot from the last touch is already counted, and whether on target: a ball
        // that crosses the line or comes off the frame and then scores is that one shot, on target.
        private int _counted = -1;
        private bool _countedOnTarget;

        // The state before this step: whether play was live in it, and where the ball was. A kick
        // connects before the ball moves on its step, so a shot is read from where it was struck;
        // and the step that ends a half still has live play's events.
        private bool _wasInPlay, _wasLive = true;
        private Vec2? _ballBefore;

        // The pass waiting for its next player (#128): its side (_passer), its passer, and whether his
        // own kick has sent it. That kick doesn't play it; a teammate's next play completes it; an
        // opponent's, or the passer's own recovery, ends it uncompleted.
        private int _passerId;
        private bool _passSent;

        // A first-time pass met the pending one: it waits for its passer's kick to connect, which
        // plays the pending pass, before it is the pending pass itself. A miss drops it.
        private int _queuedSide = -1, _queuedId;

        // The side that last touched the ball, and a shot's chance from there: a goal scored off a
        // touch, not a kick, is a shot too.
        private int _toucher = -1;
        private double _touchChance;

        public MatchRecorder(MetricsModel? model = null)
        {
            _model = model ?? MetricsModel.Reference;
        }

        public MatchResult? Result { get; private set; }

        /// <summary>
        /// The metrics so far (#128): <paramref name="side"/> 0 for home, 1 for away. A match's
        /// dashboard reads them while it is played; at full time they are the result's.
        /// </summary>
        public SideMetrics Now(int side) => Metrics(side);

        // 0 for home, 1 for away, -1 for neither: the side footballer `id` plays for.
        private static int SideOf(SimState s, int id)
        {
            if (id == 0 || s.Teams.Length < 2) return -1;
            foreach (PlayerState p in s.Players)
            {
                if (p.Id != id) continue;
                return p.TeamId == s.Teams[0].Id ? 0 : p.TeamId == s.Teams[1].Id ? 1 : -1;
            }

            return -1;
        }

        private static bool InPlay(SimState s) =>
            s.Match.Phase == MatchPhase.FirstHalf || s.Match.Phase == MatchPhase.SecondHalf || s.Match.Phase == MatchPhase.ExtraFirstHalf || s.Match.Phase == MatchPhase.ExtraSecondHalf;

        // The metrics of a step (#128): read from its events and the state after it, never from inside the football.
        private void Measure(SimConfig config, SimState after, IReadOnlyList<SimEvent> events)
        {
            bool inPlay = InPlay(after), wasInPlay = _wasInPlay;
            bool live = after.Restart.Phase == RestartPhase.Live, wasLive = _wasLive;
            Vec2 struck = _ballBefore ?? after.Ball.Position.Horizontal;
            _wasInPlay = inPlay;
            _wasLive = live;
            _ballBefore = after.Ball.Position.Horizontal;

            // Live play: a step that began or ended in a half and with the ball live. A step wholly in a
            // break, or wholly with the ball dead before a restart, has nothing to measure.
            if (after.Teams.Length < 2 || !(inPlay || wasInPlay) || !(live || wasLive)) return;
            bool stopped = false;

            // A penalty's kick is raised before the restart that says it was a penalty, in its step.
            // And offside is given after the ball has moved in its step: a save or a crossing before
            // it in the step is of the disallowed play, no shot.
            bool penalty = false, offside = false;
            foreach (SimEvent e in events)
            {
                penalty |= e.Kind == SimEventKind.RestartTaken && e.Other == (int)RestartKind.Penalty;
                offside |= e.Kind == SimEventKind.Offside;
            }

            if (offside) _shooter = -1;
            foreach (SimEvent e in events)
            {
                int side = SideOf(after, e.Subject);
                switch (e.Kind)
                {
                    case SimEventKind.Kicked:
                    case SimEventKind.Headed:
                    case SimEventKind.Volleyed:
                        if (side < 0) break;
                        _shooter = side;
                        double goalX = after.Teams[side].AttackSign * config.GoalLineX;
                        _shotChance = penalty && e.Kind == SimEventKind.Kicked ? _model.Penalty : _model.Chance(struck, goalX, config.GoalWidth);
                        break;
                    case SimEventKind.Goal:
                    case SimEventKind.HitFrame:
                    case SimEventKind.GoalLineCrossed:
                        if (_shooter >= 0 && after.Teams[_shooter].AttackSign * after.Ball.Position.X > 0) Shot(e.Kind == SimEventKind.Goal);
                        else if (e.Kind == SimEventKind.Goal && _counted >= 0 && after.Teams[_counted].Id == e.Other)
                        {
                            // The shot already counted from this touch went in: on target, not a second shot.
                            if (!_countedOnTarget) _onTarget[_counted]++;
                            _countedOnTarget = true;
                        }
                        else if (e.Kind == SimEventKind.Goal && _counted < 0 && _toucher >= 0 && after.Teams[_toucher].Id == e.Other)
                        {
                            // Scored off a touch: a shot on target from where it was touched. An own goal is none.
                            _shooter = _toucher;
                            _shotChance = _touchChance;
                            Shot(true);
                        }

                        _shooter = -1;
                        break;
                    case SimEventKind.KeeperHeld:
                    case SimEventKind.KeeperParried:
                        if (_shooter >= 0 && side >= 0 && side != _shooter) Shot(true);
                        else if (_counted >= 0 && side >= 0 && side != _counted && !_countedOnTarget)
                        {
                            // A shot already counted off the frame, then saved: on target, not a second shot.
                            _onTarget[_counted]++;
                            _countedOnTarget = true;
                        }

                        _shooter = -1;
                        break;
                    case SimEventKind.Touched:
                    case SimEventKind.Attached:
                    case SimEventKind.ChallengeWon:
                    case SimEventKind.SlideWon:
                        // Anyone's touch ends the kick, a teammate's too: a goal after it is his touch's shot.
                        if (side >= 0) _shooter = -1;
                        break;
                }

                if (side >= 0 && (e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Attached || e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.SlideWon
                    || e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.Headed || e.Kind == SimEventKind.Volleyed))
                {
                    _toucher = side;
                    _touchChance = _model.Chance(struck, after.Teams[side].AttackSign * config.GoalLineX, config.GoalWidth);
                    _counted = -1;
                }

                switch (e.Kind)
                {
                    case SimEventKind.RestartAwarded:
                        stopped = true;
                        if (e.Other == (int)RestartKind.Corner)
                        {
                            if (e.Subject == after.Teams[0].Id) _corners[0]++;
                            else if (e.Subject == after.Teams[1].Id) _corners[1]++;
                        }

                        _passer = -1;
                        _queuedSide = -1;
                        break;
                }

                // Passes, completed when a teammate plays the ball next; PPDA's passes in the passer's own 60 %
                // and the other side's defensive actions there (#126's definitions).
                if (e.Kind == SimEventKind.QuickPassResolved && side >= 0)
                {
                    _passes[side]++;
                    if (InOwnSixty(config, after, side)) _ownSixtyPasses[side]++;

                    // A first-time pass resolves as its passer winds up, ahead of his kick: it waits
                    // for the kick to connect before it plays the pass it meets.
                    if (_passer >= 0 && (e.Subject != _passerId || _passSent))
                    {
                        _queuedSide = side;
                        _queuedId = e.Subject;
                    }
                    else
                    {
                        _passer = side;
                        _passerId = e.Subject;
                        _passSent = false;
                    }

                    continue;
                }

                // A miss drops the pass it was to send: a queued first-time pass, or the pending pass not yet sent.
                if (e.Kind == SimEventKind.KickMissed && _queuedSide >= 0 && e.Subject == _queuedId) _queuedSide = -1;
                else if (e.Kind == SimEventKind.KickMissed && _passer >= 0 && e.Subject == _passerId && !_passSent) _passer = -1;

                if (side < 0) continue;
                bool defends = e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.SlideWon || e.Kind == SimEventKind.Foul || (e.Kind == SimEventKind.Touched && _passer == 1 - side);
                if (defends && InOwnSixty(config, after, 1 - side)) _actions[side]++;
                bool plays = e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Attached || e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.SlideWon
                    || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.KeeperParried || e.Kind == SimEventKind.Kicked;
                if (!plays) continue;

                // A queued first-time pass connects: it plays the pass it met, then is sent itself.
                if (e.Kind == SimEventKind.Kicked && _queuedSide >= 0 && e.Subject == _queuedId)
                {
                    if (_passer >= 0 && _passSent) PlayPass(e.Subject, side);
                    _passer = _queuedSide;
                    _passerId = _queuedId;
                    _passSent = true;
                    _queuedSide = -1;
                    continue;
                }

                if (_passer < 0) continue;

                // The passer's own kick sends his pass; it doesn't play it. Until it is sent, nothing does.
                if (e.Kind == SimEventKind.Kicked && e.Subject == _passerId && !_passSent) _passSent = true;
                else if (_passSent) PlayPass(e.Subject, side);
            }

            // Play stopped: no kick before it is a shot now, whatever the dead ball does next.
            if (stopped) _shooter = -1;

            // Possession: live play, by the side that last played the ball.
            int had = SideOf(after, after.Ball.LastPlayedBy);
            if (had >= 0) _inPossession = had;
            if (inPlay && after.Restart.Phase == RestartPhase.Live && _inPossession >= 0) _possessed[_inPossession]++;
        }

        // The pending pass is played by `player` of `side`: completed by a teammate, not by an
        // opponent nor by the passer himself.
        private void PlayPass(int player, int side)
        {
            if (side == _passer && player != _passerId) _completed[side]++;
            _passer = -1;
        }

        private void Shot(bool onTarget)
        {
            _shots[_shooter]++;
            _xg[_shooter] += _shotChance;
            if (onTarget) _onTarget[_shooter]++;
            _counted = _shooter;
            _countedOnTarget = onTarget;
        }

        // Whether the ball is in `side`'s own 60 % of the pitch, from its own goal line.
        private static bool InOwnSixty(SimConfig config, SimState s, int side) =>
            s.Teams[side].AttackSign * s.Ball.Position.X < -config.GoalLineX + 0.6 * 2 * config.GoalLineX;

        private SideMetrics Metrics(int side)
        {
            long live = _possessed[0] + _possessed[1];
            int other = 1 - side;
            return new SideMetrics(live > 0 ? _possessed[side] / (double)live : 0, _shots[side], _onTarget[side], _xg[side], _passes[side], _completed[side], _corners[side],
                _actions[side] > 0 ? _ownSixtyPasses[other] / (double)_actions[side] : 0);
        }

        public void Observe(SimConfig config, SimState after, IReadOnlyList<SimEvent> events)
        {
            Measure(config, after, events);
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.Goal)
                {
                    // The half it was scored in: 1 and 2, and 3 and 4 for extra time's (#111).
                    int half = after.Match.Phase switch
                    {
                        MatchPhase.SecondHalf => 2,
                        MatchPhase.ExtraFirstHalf => 3,
                        MatchPhase.ExtraSecondHalf => 4,
                        _ => 1,
                    };
                    _goals.Add(new ResultGoal(e.Other, after.Ball.LastPlayedBy, half, after.Match.ClockTicks * config.TickSeconds));
                }
                else if (e.Kind == SimEventKind.ExtraTime) _extraTime = true;
                else if (e.Kind == SimEventKind.RestartTaken) _restarts++;
                else if (e.Kind == SimEventKind.Foul)
                {
                    // The offender's side, by who he is (#99).
                    int side = Array.Find(after.Players, p => p.Id == e.Subject).TeamId;
                    if (side == after.Teams[0].Id) _homeFouls++;
                    else if (side == after.Teams[1].Id) _awayFouls++;
                }
                else if (e.Kind == SimEventKind.Injured)
                {
                    // The footballer's side (#109).
                    if (Array.Find(after.Players, p => p.Id == e.Subject).TeamId == after.Teams[0].Id) _homeInjuries++;
                    else _awayInjuries++;
                }
                else if (e.Kind == SimEventKind.Cautioned || e.Kind == SimEventKind.SentOff)
                {
                    // The footballer's side (#108).
                    int side = Array.Find(after.Players, p => p.Id == e.Subject).TeamId;
                    bool home = side == after.Teams[0].Id;
                    if (e.Kind == SimEventKind.Cautioned)
                    {
                        if (home) _homeCautions++;
                        else _awayCautions++;
                    }
                    else if (home) _homeSendingsOff++;
                    else _awaySendingsOff++;
                }
                else if (e.Kind == SimEventKind.RestartAwarded && e.Other == (int)RestartKind.Penalty)
                {
                    if (e.Subject == after.Teams[0].Id) _homePenalties++;
                    else if (e.Subject == after.Teams[1].Id) _awayPenalties++;
                }
                else if (e.Kind == SimEventKind.FullTime && Result == null)
                {
                    TeamState home = after.Teams[0], away = after.Teams[1];
                    Result = new MatchResult(home.Id, away.Id, home.Goals, away.Goals, config.HalfLength, 2 * (config.HalfTicks + (_extraTime ? config.ExtraTicks : 0)) * config.TickSeconds, _goals.ToArray(), _restarts,
                        _homeFouls, _awayFouls, _homePenalties, _awayPenalties, _homeCautions, _awayCautions, _homeSendingsOff, _awaySendingsOff, _homeInjuries, _awayInjuries,
                        home.Substitutions, away.Substitutions,
                        _extraTime ? 1 : 0, home.ShootoutKicks, away.ShootoutKicks, home.ShootoutGoals, away.ShootoutGoals);
                    Result.HomeMetrics = Metrics(0);
                    Result.AwayMetrics = Metrics(1);
                }
            }
        }
    }
}
