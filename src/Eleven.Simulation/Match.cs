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
        private bool MatchPaused => _state.Match.Phase == MatchPhase.HalfTime || _state.Match.Phase == MatchPhase.FullTime;

        private void StepMatch(long tick, List<SimEvent> events)
        {
            ref MatchState m = ref _state.Match;
            switch (m.Phase)
            {
                case MatchPhase.FirstHalf:
                case MatchPhase.SecondHalf:
                    if (_state.Restart.Phase != RestartPhase.Live) return;
                    if (++m.ClockTicks < Config.HalfTicks) return;
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

                        // Half-time's rest (#100): a share of every footballer's fatigue goes.
                        for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].Fatigue *= 1 - Config.HalfTimeRecovery;
                        events.Add(new SimEvent(tick, SimEventKind.HalfTime, 0));
                    }
                    else
                    {
                        m.Phase = MatchPhase.FullTime;
                        events.Add(new SimEvent(tick, SimEventKind.FullTime, 0));
                    }

                    return;

                case MatchPhase.HalfTime:
                    if (--m.PhaseTicks > 0) return;
                    for (int i = 0; i < _state.Teams.Length; i++) _state.Teams[i].AttackSign = -_state.Teams[i].AttackSign;
                    m.Phase = MatchPhase.SecondHalf;
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
            if (m.Phase > MatchPhase.FullTime) throw new ArgumentException("Unknown match phase.", nameof(state));
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
            int most = m.Phase == MatchPhase.HalfTime ? config.HalfTimeTicks : 0;
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
        public const int SchemaVersion = 4;

        public MatchResult(int homeTeam, int awayTeam, int homeGoals, int awayGoals, double halfSeconds, double playedSeconds, IReadOnlyList<ResultGoal> goals, int restarts,
            int homeFouls = 0, int awayFouls = 0, int homePenalties = 0, int awayPenalties = 0,
            int homeCautions = 0, int awayCautions = 0, int homeSendingsOff = 0, int awaySendingsOff = 0,
            int homeInjuries = 0, int awayInjuries = 0)
        {
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

        /// <summary>The text form: one <c>name=value</c> line each, then one <c>goal=</c> line per goal. Invariant culture, stable.</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("eleven-result/").Append(SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append(string.Format(CultureInfo.InvariantCulture, "home={0}\naway={1}\nscore={2}-{3}\nhalf={4:F0}\nplayed={5:F2}\nrestarts={6}\nfouls={7}-{8}\npenalties={9}-{10}\ncautions={11}-{12}\nsendingsoff={13}-{14}\ninjuries={15}-{16}\n",
                HomeTeam, AwayTeam, HomeGoals, AwayGoals, HalfSeconds, PlayedSeconds, Restarts, HomeFouls, AwayFouls, HomePenalties, AwayPenalties,
                HomeCautions, AwayCautions, HomeSendingsOff, AwaySendingsOff, HomeInjuries, AwayInjuries));
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

        public MatchResult? Result { get; private set; }

        public void Observe(SimConfig config, SimState after, IReadOnlyList<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.Goal)
                {
                    int half = after.Match.Phase == MatchPhase.SecondHalf ? 2 : 1;
                    _goals.Add(new ResultGoal(e.Other, after.Ball.LastPlayedBy, half, after.Match.ClockTicks * config.TickSeconds));
                }
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
                    Result = new MatchResult(home.Id, away.Id, home.Goals, away.Goals, config.HalfLength, 2 * config.HalfTicks * config.TickSeconds, _goals.ToArray(), _restarts,
                        _homeFouls, _awayFouls, _homePenalties, _awayPenalties, _homeCautions, _awayCautions, _homeSendingsOff, _awaySendingsOff, _homeInjuries, _awayInjuries);
                }
            }
        }
    }
}
