using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #135, from Erik's session: "I can't reliably tackle or get the ball back." A standing
    /// challenge has no button, and nothing said why one didn't start. The simulation now reports,
    /// for each human footballer pressing toward a ball an opponent controls, what became of his
    /// challenge on the step: started, too far, not closing, won't reach, or not ready. The report
    /// drives nothing (INV-12).
    /// </summary>
    public class ChallengeHintTests
    {
        private static readonly SimConfig Free = SpikeScenario.Config().With("shieldSpeed", 0);

        private const int A = 1, B = 2;

        // A has the ball at his feet; B, an opponent, is `gap` m beyond it along +x. Neither is the CPU's.
        private static SimState World(double gap, Vec2? dribblerVelocity = null)
        {
            Vec2 v = dribblerVelocity ?? Vec2.Zero;
            var ball = new Vec2(0.6, 0);
            return new SimState
            {
                Seed = 3,
                Ball = new BallState { Id = 100, Position = new Vec3(ball.X, ball.Y, Free.BallRadius), Velocity = new Vec3(v.X, v.Y, 0), LastPlayedBy = A },
                Players = new[]
                {
                    new PlayerState { Id = A, OwnerControllerId = A, TeamId = 1, Capabilities = PlayerCapabilities.Reference, Velocity = v },
                    new PlayerState { Id = B, OwnerControllerId = B, TeamId = 2, Capabilities = PlayerCapabilities.Reference, Position = ball + new Vec2(gap, 0), Heading = Math.PI },
                },
            };
        }

        private static ChallengeReport? Step(Simulator sim, Vec2 a, Vec2 b, List<SimEvent>? events = null)
        {
            sim.Step(new[] { new Command(A, sim.Tick, a), new Command(B, sim.Tick, b) }, events ?? new List<SimEvent>());
            Assert.DoesNotContain(sim.ChallengeReports, r => r.PlayerId == A); // A has the ball: nothing to challenge
            return sim.ChallengeReports.Where(r => r.PlayerId == B).Select(r => (ChallengeReport?)r).FirstOrDefault();
        }

        private static readonly Vec2 West = new Vec2(-1, 0);

        [Fact]
        public void A_challenge_that_starts_is_reported_with_its_distance()
        {
            var sim = new Simulator(Free, World(0.8));
            var events = new List<SimEvent>();
            ChallengeReport? r = Step(sim, Vec2.Zero, West, events);
            Assert.Contains(events, e => e.Kind == SimEventKind.ChallengeStarted && e.Subject == B);
            Assert.Equal(ChallengeHint.Started, r?.Hint);
            Assert.InRange(r!.Value.Distance, 0.7, 0.9);
        }

        [Fact]
        public void Beyond_challenge_range_he_is_too_far_and_further_off_not_reported()
        {
            ChallengeReport? r = Step(new Simulator(Free, World(1.6)), Vec2.Zero, West);
            Assert.Equal(ChallengeHint.TooFar, r?.Hint);
            Assert.InRange(r!.Value.Distance, 1.5, 1.7);
            Assert.Null(Step(new Simulator(Free, World(6)), Vec2.Zero, West));
        }

        [Fact]
        public void In_range_with_the_stick_not_toward_the_ball_he_is_not_closing()
        {
            Assert.Equal(ChallengeHint.NotClosing, Step(new Simulator(Free, World(0.5)), Vec2.Zero, new Vec2(0, 1))?.Hint);
        }

        [Fact]
        public void A_neutral_stick_is_no_press_and_is_not_reported()
        {
            Assert.Null(Step(new Simulator(Free, World(0.5)), Vec2.Zero, Vec2.Zero));
        }

        [Fact]
        public void Running_alongside_a_dribbler_he_wont_reach_and_is_told_by_how_much_until_he_can()
        {
            double speed = 6.4;
            SimState s = World(0, dribblerVelocity: new Vec2(speed, 0));
            s.Players[1].Position = new Vec2(-0.3, 0.6);
            s.Players[1].Velocity = new Vec2(7, 0);
            s.Players[1].Heading = 0;
            var sim = new Simulator(Free, s);
            var hints = new List<ChallengeReport>();
            var events = new List<SimEvent>();
            for (int i = 0; i < 4 * Free.TickRate && !events.Any(e => e.Kind == SimEventKind.ChallengeStarted); i++)
            {
                SimState now = sim.PeekState();
                Vec2 to = now.Ball.Position.Horizontal - now.Players[1].Position;
                ChallengeReport? r = Step(sim, new Vec2(1, 0), to * (1 / Math.Sqrt(to.LengthSquared)), events);
                if (r.HasValue) hints.Add(r.Value);
            }

            Assert.NotEmpty(hints);
            Assert.Equal(ChallengeHint.WontReach, hints[0].Hint);
            Assert.True(hints[0].Short > 0, "by how much he'd end short");
            Assert.Equal(ChallengeHint.Started, hints[hints.Count - 1].Hint);
        }

        [Fact]
        public void Committing_or_recovering_he_is_not_ready()
        {
            var sim = new Simulator(Free, World(0.8));
            Step(sim, Vec2.Zero, West);
            Assert.Equal(ChallengeHint.NotReady, Step(sim, Vec2.Zero, West)?.Hint);
        }

        [Fact]
        public void A_cpu_footballer_is_never_reported()
        {
            SimConfig c = SpikeScenario.Config();
            var sim = new Simulator(c, Squads.Match(c, 3, perSide: 5, bothCpu: true));
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            int started = 0;
            for (int i = 0; i < 30 * c.TickRate; i++)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                started += events.Count(e => e.Kind == SimEventKind.ChallengeStarted);
                Assert.Empty(sim.ChallengeReports);
            }

            Assert.True(started > 0, "the CPU challenged in the run");
        }

        [Fact]
        public void A_snapshot_gives_the_same_reports_on_the_next_step()
        {
            var sim = new Simulator(Free, World(1.6));
            Step(sim, Vec2.Zero, West);
            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), Free);
            ChallengeReport? a = Step(sim, Vec2.Zero, West), b = Step(restored, Vec2.Zero, West);
            Assert.Equal(a?.ToString(), b?.ToString());
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }

        // The alongside world of #96, the defender B chasing the dribbler A.
        private static (ChallengeReport? First, Simulator Sim) Alongside(SimConfig c)
        {
            SimState s = World(0, dribblerVelocity: new Vec2(6.4, 0));
            s.Players[1].Position = new Vec2(-0.3, 0.6);
            s.Players[1].Velocity = new Vec2(7, 0);
            s.Players[1].Heading = 0;
            var sim = new Simulator(c, s);
            Vec2 to = s.Ball.Position.Horizontal - s.Players[1].Position;
            return (Step(sim, new Vec2(1, 0), to * (1 / Math.Sqrt(to.LengthSquared))), sim);
        }

        [Fact]
        public void HumanChallengeLeeway_lets_a_human_commit_that_would_end_that_much_short()
        {
            ChallengeReport? strict = Alongside(Free).First;
            Assert.Equal(ChallengeHint.WontReach, strict?.Hint);
            double shortBy = strict!.Value.Short;
            Assert.True(shortBy > 0);
            Assert.Equal(ChallengeHint.WontReach, Alongside(Free.With("humanChallengeLeeway", shortBy * 0.5)).First?.Hint);
            Assert.Equal(ChallengeHint.Started, Alongside(Free.With("humanChallengeLeeway", shortBy * 1.5)).First?.Hint);
        }

        [Fact]
        public void HumanChallengeLeeway_never_changes_a_cpu_match()
        {
            SimConfig c = SpikeScenario.Config();
            ulong Run(SimConfig config)
            {
                var sim = new Simulator(config, Squads.Match(config, 3, perSide: 5, bothCpu: true));
                var commands = new List<Command>();
                for (int i = 0; i < 40 * config.TickRate; i++)
                {
                    commands.Clear();
                    KeeperControllers.Append(sim, commands);
                    CpuControllers.Append(sim, commands);
                    RestartControllers.Append(sim, commands);
                    sim.Step(commands, new List<SimEvent>());
                }

                return sim.StateHash();
            }

            Assert.Equal(Run(c), Run(c.With("humanChallengeLeeway", 1.5)));
        }

        [Fact]
        public void A_seat_recovering_beyond_challenge_range_is_not_pressing_in_range()
        {
            // Codex on #145: NotReady is reported out to 3 m; only within challengeRange is it an attempt.
            var trial = new SquadTrial();
            var none = new SimEvent[0];
            double range = Free.ChallengeRange;
            bool pressing = trial.CountPressOf(B, new[] { new ChallengeReport(B, ChallengeHint.NotReady, range + 0.5) }, none, false, range);
            Assert.False(pressing);
            Assert.Equal(0, trial.SeatAttempts);
            pressing = trial.CountPressOf(B, new[] { new ChallengeReport(B, ChallengeHint.NotReady, range - 0.1) }, none, pressing, range);
            Assert.True(pressing);
            Assert.Equal(1, trial.SeatAttempts);
        }

        [Fact]
        public void On_a_switch_step_the_seat_is_the_footballer_it_had_during_the_step()
        {
            // Codex on #145: challenges are decided before a switch at the step's end. The seat had
            // B; A, the CPU's until then, is the seat's only after it. A's challenge isn't the seat's.
            double range = Free.ChallengeRange;
            var trial = new SquadTrial();
            var reports = new[] { new ChallengeReport(B, ChallengeHint.NotClosing, range - 0.2) };
            var events = new[] { new SimEvent(0, SimEventKind.ChallengeStarted, A), new SimEvent(0, SimEventKind.Switched, A, other: B) };
            bool pressing = trial.CountPressOf(B, reports, events, false, range);
            Assert.True(pressing);
            Assert.Equal((1, 1, 0), (trial.SeatAttempts, trial.SeatNotClosing, trial.SeatChallenges));
        }

        [Fact]
        public void A_defending_seat_counts_its_attempts_starts_and_wins_over_a_match()
        {
            SquadTrial t = SquadTrials.One(SpikeScenario.Config(), 5, 0x135000, seat: true);
            Assert.True(t.SeatAttempts > 0, "the seat pressed the ball");
            Assert.InRange(t.SeatChallenges, 1, t.SeatAttempts);
            Assert.InRange(t.SeatChallengesWon, 0, t.SeatChallenges);
            Assert.True(t.SeatTooFar + t.SeatNotClosing + t.SeatWontReach + t.SeatNotReady > 0, "steps a challenge didn't start, by reason");
        }
    }
}
