// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// An N-a-side world (#92): outfielders and a keeper a side, two sides, two goals, a referee
    /// and a clock. Player ids, ascending: home's outfielders, home's keeper, away's outfielders,
    /// away's keeper. Each footballer has his own controller, his id. Home's first outfielder is
    /// the human's unless <c>bothCpu</c>; every other outfielder is a CPU footballer.
    /// </summary>
    public static class Squads
    {
        public const int HomeTeam = OneVOne.HomeTeam, AwayTeam = OneVOne.AwayTeam;

        public static int HomeOutfielder(int i) => 1 + i;

        public static int HomeKeeper(int perSide) => perSide + 1;

        public static int AwayOutfielder(int perSide, int i) => perSide + 2 + i;

        public static int AwayKeeper(int perSide) => 2 * perSide + 2;

        /// <summary>From this side size a side has a bench (#110): eleven a side.</summary>
        public const int BenchFrom = 10;

        /// <summary>Substitutes on a side's bench (#110, Law 3: up to nine named).</summary>
        public const int BenchSize = 9;

        /// <summary>A side's bench at this size: <see cref="BenchSize"/> from <see cref="BenchFrom"/>, none below.</summary>
        public static int BenchOf(int perSide) => perSide >= BenchFrom ? BenchSize : 0;

        /// <summary>Home's substitute i's id: after every footballer who starts.</summary>
        public static int HomeBench(int perSide, int i) => 2 * perSide + 3 + i;

        /// <summary>Away's substitute i's id: after home's bench.</summary>
        public static int AwayBench(int perSide, int i) => 2 * perSide + 3 + BenchOf(perSide) + i;

        /// <summary>The smallest side that plays a shape (#96): smaller sides play M2's roles alone.</summary>
        public const int ShapeFrom = 3;

        /// <summary>
        /// An outfielder's place in his side's shape (#96), numbered from the back: the kick-off's
        /// taker, the first, is the furthest forward. 0 for a side smaller than <see cref="ShapeFrom"/>.
        /// </summary>
        public static int SlotOf(int perSide, int i) => perSide < ShapeFrom ? 0 : i == 0 ? perSide : i;

        /// <summary>From this side size the kick-off's places are the shape's homes (#106); smaller sides keep M2's rows.</summary>
        public const int ShapeStartFrom = 6;

        // Home's outfielder i's place at the kick-off, in home's frame: 12 m back in rows either side,
        // or, from ShapeStartFrom, his home in the shape inside his own half.
        private static Vec2 StartAt(int perSide, int i, Formation formation)
        {
            if (perSide < ShapeStartFrom) return new Vec2(-12, (i % 2 == 1 ? 1 : -1) * 12 * ((i + 1) / 2));
            Vec2 home = AI.OutfieldBrain.ShapeHome(formation, perSide, SlotOf(perSide, i));
            double x = Math.Min(home.X, -2);

            // Clear of the kick-off's taker and, turned about, of the opponent facing him (#125): a
            // home in the middle near the halfway line stands a little to one side.
            double y = home.Y;
            if (Math.Abs(y) < KickOffClear && x > -(OneVOne.OpponentDistance + KickOffClear)) y = KickOffClear;
            return new Vec2(x, y);
        }

        private const double KickOffClear = 5;

        // A side's three stored plans (#126), each its own.
        private static TeamTactic[] Plans(int team, Formation formation, TeamTactic[]? given)
        {
            var plans = new TeamTactic[TeamTactics.PerSide];
            for (int i = 0; i < plans.Length; i++)
            {
                plans[i] = given != null ? given[i] : TeamTactic.Reference(team, formation);
                plans[i].TeamId = team;
            }

            return plans;
        }

        // Away's place: home's turned about the centre spot for a shape; M2's rows otherwise.
        private static Vec2 Away(Vec2 home, int perSide) =>
            perSide < ShapeStartFrom ? new Vec2(14, -home.Y) : new Vec2(-home.X, -home.Y);

        /// <summary>
        /// A match's start: home kicks off, the clock at nought. <paramref name="homeSide"/> and
        /// <paramref name="awaySide"/> give a side's outfielders their capabilities (#103's league);
        /// <paramref name="home"/> is the human's own footballer's. Each side lines up in its
        /// formation (#125), the 4-4-2 unless given, and, from <see cref="ShapeFrom"/> a side, stores three tactical plans (#126): those
        /// given, its side's id set on each, or else the reference plan three times in its formation.
        /// It starts on its first; given plans, the formation is the first plan's.
        /// </summary>
        public static SimState Match(SimConfig config, ulong seed, int perSide, bool bothCpu = false, CpuSkill? awayCpu = null, CpuSkill? homeCpu = null,
            PlayerCapabilities? home = null, PlayerCapabilities? homeSide = null, PlayerCapabilities? awaySide = null,
            Formation homeFormation = Formation.FourFourTwo, Formation awayFormation = Formation.FourFourTwo,
            TeamTactic[]? homeTactics = null, TeamTactic[]? awayTactics = null)
        {
            if (perSide < 1) throw new ArgumentOutOfRangeException(nameof(perSide), perSide, "A side has at least one outfielder.");
            if (homeTactics != null && homeTactics.Length != TeamTactics.PerSide) throw new ArgumentException($"A side stores {TeamTactics.PerSide} plans.", nameof(homeTactics));
            if (awayTactics != null && awayTactics.Length != TeamTactics.PerSide) throw new ArgumentException($"A side stores {TeamTactics.PerSide} plans.", nameof(awayTactics));
            if (homeTactics != null) homeFormation = homeTactics[0].Formation;
            if (awayTactics != null) awayFormation = awayTactics[0].Formation;
            double keeperX = config.GoalLineX - OneVOne.KeeperStandOff;
            var players = new List<PlayerState>();
            var cpus = new List<CpuState>();
            for (int i = 0; i < perSide; i++)
            {
                int id = HomeOutfielder(i);
                Vec2 at = i == 0 ? new Vec2(-Simulator.TakerBehind, 0) : StartAt(perSide, i, homeFormation);
                players.Add(new PlayerState { Id = id, OwnerControllerId = id, TeamId = HomeTeam, Capabilities = i == 0 && home.HasValue ? home.Value : homeSide ?? PlayerCapabilities.Reference, Position = at, Heading = 0, Slot = SlotOf(perSide, i) });
                if (i > 0 || bothCpu) cpus.Add(new CpuState { PlayerId = id, Skill = homeCpu ?? CpuSkill.Pro });
            }

            players.Add(new PlayerState { Id = HomeKeeper(perSide), OwnerControllerId = HomeKeeper(perSide), TeamId = HomeTeam, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(-keeperX, 0) });
            for (int i = 0; i < perSide; i++)
            {
                int id = AwayOutfielder(perSide, i);
                Vec2 at = i == 0 ? new Vec2(OneVOne.OpponentDistance, 0) : Away(StartAt(perSide, i, awayFormation), perSide);
                players.Add(new PlayerState { Id = id, OwnerControllerId = id, TeamId = AwayTeam, Capabilities = awaySide ?? PlayerCapabilities.Reference, Position = at, Heading = Math.PI, Slot = SlotOf(perSide, i) });
                cpus.Add(new CpuState { PlayerId = id, Skill = awayCpu ?? CpuSkill.Pro });
            }

            players.Add(new PlayerState { Id = AwayKeeper(perSide), OwnerControllerId = AwayKeeper(perSide), TeamId = AwayTeam, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(keeperX, 0), Heading = Math.PI });

            // The benches (#110): each side's substitutes, with its outfielders' capabilities and its CPU's skill.
            var bench = new List<BenchPlayer>();
            for (int i = 0; i < BenchOf(perSide); i++) bench.Add(new BenchPlayer { Id = HomeBench(perSide, i), TeamId = HomeTeam, Capabilities = homeSide ?? PlayerCapabilities.Reference, Skill = homeCpu ?? CpuSkill.Pro });
            for (int i = 0; i < BenchOf(perSide); i++) bench.Add(new BenchPlayer { Id = AwayBench(perSide, i), TeamId = AwayTeam, Capabilities = awaySide ?? PlayerCapabilities.Reference, Skill = awayCpu ?? CpuSkill.Pro });

            return new SimState
            {
                Seed = seed,
                Ball = new BallState { Id = OneVOne.BallId, Position = new Vec3(0, 0, config.BallRadius) },
                Teams = new[] { new TeamState { Id = HomeTeam, AttackSign = 1, Formation = homeFormation }, new TeamState { Id = AwayTeam, AttackSign = -1, Formation = awayFormation } },
                Tactics = perSide < ShapeFrom ? Array.Empty<TeamTactic>() : Plans(HomeTeam, homeFormation, homeTactics).Concat(Plans(AwayTeam, awayFormation, awayTactics)).ToArray(),
                Players = players.ToArray(),
                Keepers = new[]
                {
                    new KeeperState { PlayerId = HomeKeeper(perSide), Capabilities = KeeperCapabilities.Reference },
                    new KeeperState { PlayerId = AwayKeeper(perSide), Capabilities = KeeperCapabilities.Reference },
                },
                Cpus = cpus.ToArray(),
                Bench = bench.ToArray(),
                Restart = new RestartState
                {
                    On = true, Phase = RestartPhase.Ready, Kind = RestartKind.KickOff, AwardedTeam = HomeTeam,
                    PhaseTicks = config.RestartReadyTicks, TakerId = HomeOutfielder(0),
                },
                Match = new MatchState { Phase = MatchPhase.FirstHalf, FirstKickOff = HomeTeam },
            };
        }
    }

    /// <summary>One CPU-against-CPU N-a-side match (#92).</summary>
    public sealed class SquadTrial
    {
        public ulong Seed;
        public bool Finished;
        public int HomeGoals, AwayGoals;
        public int Passes, PassesCompleted;

        /// <summary>How often the ball went from one side's feet to the other's.</summary>
        public int Possessions;

        /// <summary>m. The mean distance between teammates who aren't keepers, sampled each second in play.</summary>
        public double MeanSpacing;

        /// <summary>s. The longest stretch in play in which a side's outfielders were all within 2 m of each other.</summary>
        public double LongestBunchedSeconds;

        public double LengthSeconds;
        public ulong FinalHash;

        /// <summary>Whether home's first outfielder was a human seat (#95): <see cref="SquadTrials.Seat"/>, switching as a human's controller does.</summary>
        public bool Seat;

        /// <summary>Switches the seat's controller made, and how many were refused.</summary>
        public int Switches, SwitchesRefused;

        /// <summary>
        /// The seat's ball-winning (#135): pressing episodes within challengeRange of a ball an
        /// opponent controls, challenges started and won, and its steps pressing without a start by
        /// the simulation's reason.
        /// </summary>
        public int SeatAttempts, SeatChallenges, SeatChallengesWon;

        public int SeatTooFar, SeatNotClosing, SeatWontReach, SeatNotReady;

        /// <summary>The seat's switches by cause (#136): to a pass's receiver, to a teammate with the ball, at a turnover, asked.</summary>
        public int SwitchesPass, SwitchesMate, SwitchesTurnover, SwitchesAsked;

        /// <summary>
        /// Counts a step of the seat's ball-winning (#135) from the simulation's reports: an attempt
        /// is a run of steps pressing within challengeRange. Returns whether one is under way.
        /// </summary>
        /// <summary>
        /// As <see cref="CountPress"/>, for <paramref name="seatPlayer"/>: the footballer the seat
        /// owned during the step, before any switch at its end (Codex on #145).
        /// </summary>
        public bool CountPressOf(int seatPlayer, IReadOnlyList<ChallengeReport> reports, IReadOnlyList<SimEvent> events, bool pressing, double challengeRange) => pressing;

        public bool CountPress(SimState s, IReadOnlyList<ChallengeReport> reports, IReadOnlyList<SimEvent> events, int seatController, bool pressing, double challengeRange = double.PositiveInfinity)
        {
            int me = Array.FindIndex(s.Players, p => p.OwnerControllerId == seatController);
            int id = me >= 0 ? s.Players[me].Id : 0;
            bool inRange = false;
            foreach (ChallengeReport r in reports)
            {
                if (r.PlayerId != id) continue;
                if (r.Hint == ChallengeHint.TooFar) SeatTooFar++;
                else if (r.Hint == ChallengeHint.NotClosing) SeatNotClosing++;
                else if (r.Hint == ChallengeHint.WontReach) SeatWontReach++;
                else if (r.Hint == ChallengeHint.NotReady) SeatNotReady++;
                // In range by the report's distance: NotReady is reported out to 3 m (Codex on #145).
                inRange |= r.Hint != ChallengeHint.TooFar && r.Distance <= challengeRange;
            }

            foreach (SimEvent e in events)
            {
                if (e.Subject != id || id == 0) continue;
                if (e.Kind == SimEventKind.ChallengeStarted) SeatChallenges++;
                else if (e.Kind == SimEventKind.ChallengeWon) SeatChallengesWon++;
            }

            if (inRange && !pressing) SeatAttempts++;
            return inRange;
        }

        /// <summary>Counts a seat's switch event; <paramref name="cause"/> is why the switcher asked for it.</summary>
        public void Count(SimEvent e, int seatController, SwitchCause cause)
        {
            if (e.Kind == SimEventKind.CommandRejected && e.Subject == seatController && e.Rejection == CommandRejection.SwitchRefused) SwitchesRefused++;
            if (e.Kind != SimEventKind.Switched) return;
            Switches++;
            if (cause == SwitchCause.Pass) SwitchesPass++;
            else if (cause == SwitchCause.MateHasBall) SwitchesMate++;
            else if (cause == SwitchCause.Turnover) SwitchesTurnover++;
            else if (cause == SwitchCause.Asked) SwitchesAsked++;
        }

        /// <summary>Substitutes each side brought on (#110), and each one's minute, the match scaled to 90.</summary>
        public int HomeSubstitutions, AwaySubstitutions;

        public List<double> SubstitutionMinutes = new List<double>();
    }

    /// <summary>
    /// Whole N-a-side CPU-against-CPU matches (#92): every footballer a CPU footballer, the
    /// reference halves, every brain running. Records passes, possessions, spacing, bunching and
    /// the score. Technical evidence, not feel.
    /// </summary>
    public static class SquadTrials
    {
        public const int Seeds = 12;

        /// <summary>The human seat's controller: home's first outfielder's (#93).</summary>
        public const int SeatController = 1;

        public static List<SquadTrial> Run(SimConfig config, int perSide, int seeds = Seeds, bool seat = false, SwitchMode mode = SwitchMode.Auto)
        {
            var trials = new List<SquadTrial>();
            for (int i = 1; i <= seeds; i++) trials.Add(One(config, perSide, 0x92000000UL + (ulong)i, seat, mode));
            return trials;
        }

        /// <summary>
        /// The human seat's command (#95), a harness driver and not an AI (it reads the state, as a
        /// test does): the stick at the ball; within reach, a full kick at the goal his side
        /// attacks. Its switches are <see cref="SwitchController"/>'s, as the host's are.
        /// </summary>
        public static Command Seat(Simulator sim, int controllerId, int switchTo)
        {
            SimState s = sim.PeekState();
            int me = Array.FindIndex(s.Players, p => p.OwnerControllerId == controllerId);
            if (me < 0) return new Command(controllerId, s.Tick, Vec2.Zero, switchTo: switchTo);
            PlayerState body = s.Players[me];
            SimConfig config = sim.Config;
            Vec2 toBall = s.Ball.Position.Horizontal - body.Position;
            double away = Eleven.Simulation.Maths.SimMath.Sqrt(toBall.LengthSquared);
            Vec2 move = away > 0.3 ? toBall * (1 / away) : Vec2.Zero;
            KickIntent? kick = null;
            bool ready = body.WindUpTicks == 0 && body.KickCooldown == 0 && s.Ball.HeldBy == 0;
            if (ready && away <= 0.8 * config.KickReach && s.Ball.Position.Z <= config.KickMaxBallHeight)
            {
                int sign = Array.Find(s.Teams, t => t.Id == body.TeamId).AttackSign;
                kick = new KickIntent(new Vec2(sign * config.GoalLineX, 0) - s.Ball.Position.Horizontal, 1, 0.12);
            }

            return new Command(controllerId, s.Tick, move, kick, switchTo: switchTo);
        }

        public static SquadTrial One(SimConfig config, int perSide, ulong seed, bool seat = false, SwitchMode mode = SwitchMode.Auto)
        {
            var sim = new Simulator(config, Squads.Match(config, seed, perSide, bothCpu: !seat));
            var recorder = new MatchRecorder();
            var trial = new SquadTrial { Seed = seed, Seat = seat };
            SwitchController? switcher = seat ? new SwitchController(SeatController, mode) : null;
            bool pressing = false;
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            int limit = 3 * 2 * config.HalfTicks;
            int lastTeam = 0, passer = 0, passTeam = 0;
            double spacing = 0;
            int samples = 0;
            long bunchedFrom = -1, bunched = 0;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                SwitchCause cause = SwitchCause.None;
                if (seat)
                {
                    commands.Add(Seat(sim, SeatController, switcher!.Decide(sim, events, manual: false)));
                    cause = switcher.LastCause;
                }

                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                recorder.Observe(config, s, events);
                if (seat) pressing = trial.CountPress(s, sim.ChallengeReports, events, SeatController, pressing, config.ChallengeRange);
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Substituted)
                    {
                        // The substitute's side, and the match clock's minute as a share of the match scaled to 90.
                        bool home = TeamOf(s, e.Subject) == Squads.HomeTeam;
                        if (home) trial.HomeSubstitutions++;
                        else trial.AwaySubstitutions++;
                        trial.SubstitutionMinutes.Add(90 * ManagerControllers.Observe(s, config, home ? Squads.HomeTeam : Squads.AwayTeam).MatchShare);
                    }

                    trial.Count(e, SeatController, cause);
                    int team = TeamOf(s, e.Subject);
                    if (e.Kind == SimEventKind.QuickPassResolved)
                    {
                        trial.Passes++;
                        passer = e.Subject;
                        passTeam = team;
                    }
                    else if ((e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.ChallengeWon) && team != 0)
                    {
                        if (passer != 0 && e.Subject != passer)
                        {
                            if (team == passTeam) trial.PassesCompleted++;
                            passer = 0;
                        }

                        if (lastTeam != 0 && team != lastTeam) trial.Possessions++;
                        lastTeam = team;
                    }
                }

                bool live = s.Restart.Phase == RestartPhase.Live && (s.Match.Phase == MatchPhase.FirstHalf || s.Match.Phase == MatchPhase.SecondHalf);
                if (live && s.Tick % config.TickRate == 0 && perSide > 1)
                {
                    spacing += Spacing(s, Squads.HomeTeam) + Spacing(s, Squads.AwayTeam);
                    samples += 2;
                }

                bool bunchedNow = live && perSide > 1 && (Spacing(s, Squads.HomeTeam) < 2 || Spacing(s, Squads.AwayTeam) < 2);
                if (bunchedNow && bunchedFrom < 0) bunchedFrom = s.Tick;
                if (!bunchedNow && bunchedFrom >= 0)
                {
                    bunched = Math.Max(bunched, s.Tick - bunchedFrom);
                    bunchedFrom = -1;
                }
            }

            trial.Finished = recorder.Result != null;
            if (recorder.Result != null)
            {
                trial.HomeGoals = recorder.Result.HomeGoals;
                trial.AwayGoals = recorder.Result.AwayGoals;
            }

            trial.MeanSpacing = samples > 0 ? spacing / samples : 0;
            trial.LongestBunchedSeconds = bunched * config.TickSeconds;
            trial.LengthSeconds = sim.Tick * config.TickSeconds;
            trial.FinalHash = sim.StateHash();
            return trial;
        }

        private static int TeamOf(SimState s, int id)
        {
            foreach (PlayerState p in s.Players)
            {
                if (p.Id == id) return p.TeamId;
            }

            return 0;
        }

        // The mean distance between a side's outfielders.
        internal static double Spacing(SimState s, int team)
        {
            double total = 0;
            int pairs = 0;
            for (int i = 0; i < s.Players.Length; i++)
            {
                if (s.Players[i].TeamId != team || IsKeeper(s, s.Players[i].Id)) continue;
                for (int j = i + 1; j < s.Players.Length; j++)
                {
                    if (s.Players[j].TeamId != team || IsKeeper(s, s.Players[j].Id)) continue;
                    total += Eleven.Simulation.Maths.SimMath.Sqrt((s.Players[i].Position - s.Players[j].Position).LengthSquared);
                    pairs++;
                }
            }

            return pairs > 0 ? total / pairs : 0;
        }

        private static bool IsKeeper(SimState s, int id) => Array.FindIndex(s.Keepers, k => k.PlayerId == id) >= 0;

        public static void WriteCsv(System.IO.TextWriter w, SimConfig config, IReadOnlyList<SquadTrial> trials)
        {
            w.WriteLine("config_hash,seed,seat,finished,home,away,passes,passes_completed,possessions,mean_spacing_m,longest_bunched_s,switches,switches_refused,switches_pass,switches_mate,switches_turnover,switches_asked,seat_attempts,seat_challenges,seat_won,seat_too_far,seat_not_closing,seat_wont_reach,seat_not_ready,length_s,final_hash");
            foreach (SquadTrial t in trials)
            {
                w.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:x16},{1:x},{2},{3},{4},{5},{6},{7},{8},{9:F2},{10:F2},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24:F2},{25:x16}",
                    config.Hash, t.Seed, t.Seat ? "yes" : "no", t.Finished ? "yes" : "no", t.HomeGoals, t.AwayGoals, t.Passes, t.PassesCompleted, t.Possessions, t.MeanSpacing, t.LongestBunchedSeconds,
                    t.Switches, t.SwitchesRefused, t.SwitchesPass, t.SwitchesMate, t.SwitchesTurnover, t.SwitchesAsked,
                    t.SeatAttempts, t.SeatChallenges, t.SeatChallengesWon, t.SeatTooFar, t.SeatNotClosing, t.SeatWontReach, t.SeatNotReady, t.LengthSeconds, t.FinalHash));
            }
        }

        public static string KeyNumbers(IReadOnlyList<SquadTrial> trials, int perSide)
        {
            int finished = 0, goals = 0, passes = 0, completed = 0, possessions = 0, switches = 0;
            double spacing = 0, bunched = 0;
            foreach (SquadTrial t in trials)
            {
                if (t.Finished) finished++;
                goals += t.HomeGoals + t.AwayGoals;
                passes += t.Passes;
                completed += t.PassesCompleted;
                possessions += t.Possessions;
                switches += t.Switches;
                spacing += t.MeanSpacing;
                bunched = Math.Max(bunched, t.LongestBunchedSeconds);
            }

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}v{0}: {1} of {2} finished; {3} goals; {4} passes, {5} completed; {6} possessions; mean spacing {7:F1} m; longest bunched {8:F1} s; {9} switches",
                perSide, finished, trials.Count, goals, passes, completed, possessions, spacing / Math.Max(1, trials.Count), bunched, switches)
                + string.Format(System.Globalization.CultureInfo.InvariantCulture, " (pass {0}, teammate {1}, turnover {2}, asked {3}; {4} refused)",
                    trials.Sum(t => t.SwitchesPass), trials.Sum(t => t.SwitchesMate), trials.Sum(t => t.SwitchesTurnover), trials.Sum(t => t.SwitchesAsked), trials.Sum(t => t.SwitchesRefused))
                + string.Format(System.Globalization.CultureInfo.InvariantCulture, "; the seat pressed {0} times, challenged {1}, won {2}; steps without a start: too far {3}, not closing {4}, won't reach {5}, not ready {6}",
                    trials.Sum(t => t.SeatAttempts), trials.Sum(t => t.SeatChallenges), trials.Sum(t => t.SeatChallengesWon), trials.Sum(t => t.SeatTooFar), trials.Sum(t => t.SeatNotClosing), trials.Sum(t => t.SeatWontReach), trials.Sum(t => t.SeatNotReady));
        }
    }
}
