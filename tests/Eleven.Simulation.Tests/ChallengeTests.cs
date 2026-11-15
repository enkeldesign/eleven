using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>The standing challenge (#84): a contest for a ball an opponent controls, decided by where the ball is.</summary>
    public class ChallengeTests
    {
        private static readonly SimConfig Free = SpikeScenario.Config();
        private static readonly SimConfig Attached = Free.WithControlModel(ControlModel.Attached);

        private const int A = 1, B = 2;

        // A has the ball at his feet; B, an opponent, is `gap` m beyond it along +x.
        private static SimState World(SimConfig c, double gap, double tackling = 0.7, Vec2? dribblerVelocity = null)
        {
            Vec2 v = dribblerVelocity ?? Vec2.Zero;
            var ball = new Vec2(0.6, 0);
            return new SimState
            {
                Seed = 3,
                Ball = new BallState { Id = 100, Position = new Vec3(ball.X, ball.Y, c.BallRadius), Velocity = new Vec3(v.X, v.Y, 0), LastPlayedBy = A, AttachedTo = c.ControlModel == ControlModel.Attached ? A : 0 },
                Players = new[]
                {
                    new PlayerState { Id = A, OwnerControllerId = A, TeamId = 1, Capabilities = PlayerCapabilities.Reference, Velocity = v },
                    new PlayerState { Id = B, OwnerControllerId = B, TeamId = 2, Capabilities = PlayerCapabilities.Reference.With("tackling", tackling), Position = ball + new Vec2(gap, 0), Heading = Math.PI },
                },
            };
        }

        private static List<SimEvent> Run(Simulator sim, int ticks, Func<long, Vec2> a, Func<long, Vec2> b, List<SimState>? states = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                sim.Step(new[] { new Command(A, sim.Tick, a(sim.Tick)), new Command(B, sim.Tick, b(sim.Tick)) }, events);
                states?.Add(sim.PeekState().Clone());
            }

            return events;
        }

        private static Vec2 Still(long _) => Vec2.Zero;

        private static Vec2 West(long _) => new Vec2(-1, 0);

        private static PlayerState P(SimState s, int id) => s.Players.First(p => p.Id == id);

        [Fact]
        public void A_close_challenge_commits_reaches_the_ball_knocks_it_away_and_recovers()
        {
            var sim = new Simulator(Free, World(Free, 0.8));
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 60, Still, West, states);

            SimEvent start = Assert.Single(events, e => e.Kind == SimEventKind.ChallengeStarted);
            Assert.Equal((0L, B), (start.Tick, start.Subject));
            int commit = Simulator.CommitTicksFor(0.7, Free);
            SimEvent won = Assert.Single(events, e => e.Kind == SimEventKind.ChallengeWon);
            Assert.Equal(((long)commit, B, A), (won.Tick, won.Subject, won.Other));
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.ChallengeMissed);

            // Through the commit he keeps his line and can't steer; then he recovers, braking, and is ready again.
            Assert.All(states.Take(commit), s => Assert.Equal(ChallengePhase.Commit, P(s, B).Challenge));
            SimState after = states[(int)won.Tick];
            Assert.Equal(ChallengePhase.Recover, P(after, B).Challenge);
            Assert.Equal(B, after.Ball.LastPlayedBy);

            // Knocked away at the impulse's speed, less the step's rolling friction. From in front, his line runs into A's feet, so
            // the ball comes off them, back past B's side.
            Vec2 kick = after.Ball.Velocity.Horizontal;
            Assert.InRange(Math.Sqrt(kick.LengthSquared), Free.ChallengeImpulse - 0.05, Free.ChallengeImpulse);
            Assert.True(kick.X > 0, $"knocked {kick.X},{kick.Y}");

            int ready = (int)won.Tick + Free.ChallengeRecoverTicks;
            Assert.Equal(ChallengePhase.Recover, P(states[ready - 1], B).Challenge);
            Assert.Equal(ChallengePhase.Ready, P(states[ready], B).Challenge);
            for (int i = (int)won.Tick + 1; i < ready; i++) Assert.True(P(states[i], B).Velocity.LengthSquared <= P(states[i - 1], B).Velocity.LengthSquared + 1e-12, $"step {i}: he sped up while recovering");
        }

        [Fact]
        public void Too_far_off_or_not_closing_there_is_no_challenge_and_no_steal()
        {
            // 1.5 m off and closing: nothing until within range.
            List<SimEvent> far = Run(new Simulator(Free, World(Free, 1.5)), 1, Still, West);
            Assert.DoesNotContain(far, e => e.Kind == SimEventKind.ChallengeStarted);

            // Within range but moving away, or standing: no challenge, and no touch of a ball A controls.
            foreach (Func<long, Vec2> stick in new Func<long, Vec2>[] { Still, _ => new Vec2(1, 0), _ => new Vec2(0, 1) })
            {
                var sim = new Simulator(Free, World(Free, 0.5));
                List<SimEvent> events = Run(sim, 30, Still, stick);
                Assert.DoesNotContain(events, e => e.Kind == SimEventKind.ChallengeStarted);
                Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Touched && e.Subject == B);
                Assert.Equal(A, sim.PeekState().Ball.LastPlayedBy);
            }

            // A teammate never challenges.
            SimState mate = World(Free, 0.8);
            mate.Players[1].TeamId = 1;
            Assert.DoesNotContain(Run(new Simulator(Free, mate), 30, Still, West), e => e.Kind == SimEventKind.ChallengeStarted);
        }

        [Fact]
        public void A_loose_ball_is_anyones_to_touch_and_no_ones_to_challenge()
        {
            // No one has played it, or the last to play it is well away from it.
            foreach (int last in new[] { 0, A })
            {
                SimState s = World(Free, 0.6);
                s.Ball.LastPlayedBy = last;
                s.Players[0].Position = new Vec2(-5, 0);
                List<SimEvent> events = Run(new Simulator(Free, s), 30, Still, West);
                Assert.DoesNotContain(events, e => e.Kind == SimEventKind.ChallengeStarted);
                Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Subject == B);
            }
        }

        [Fact]
        public void Movement_avoids_the_challenge()
        {
            // A takes the ball away across B at a sprint: B's commit wouldn't reach it, so he doesn't
            // commit (#96), or commits and misses; either way A keeps it.
            SimState s = World(Free, 0.8);
            s.Players[0].Velocity = new Vec2(0, 7);
            s.Ball.Velocity = new Vec3(0, 7, 0);
            s.Players[1].Position = new Vec2(1.3, 0);
            var sim = new Simulator(Free, s);
            List<SimEvent> events = Run(sim, 60, _ => new Vec2(0, 1), t => t == 0 ? new Vec2(-1, 0) : new Vec2(-1, 0));
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.ChallengeWon);
            Assert.Equal(events.Count(e => e.Kind == SimEventKind.ChallengeStarted), events.Count(e => e.Kind == SimEventKind.ChallengeMissed));
            Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Subject == A && e.Tick > 30);
        }

        // #96: a defender running alongside a dribbler, as a chase ends. He commits only when his
        // commit will reach the ball, so he waits until he can, and then wins it.
        [Theory]
        [InlineData(ControlModel.Free)]
        [InlineData(ControlModel.Attached)]
        public void Chasing_alongside_he_commits_only_when_he_can_reach_the_ball_and_wins_it(ControlModel model)
        {
            SimConfig c = Free.WithControlModel(model);
            double speed = 6.4;
            SimState s = World(c, 0, dribblerVelocity: new Vec2(speed, 0));
            s.Players[1].Position = new Vec2(-0.3, 0.6);
            s.Players[1].Velocity = new Vec2(7, 0);
            s.Players[1].Heading = 0;
            var sim = new Simulator(c, s);
            List<SimEvent> events = Run(sim, 4 * c.TickRate, _ => new Vec2(1, 0), t =>
            {
                SimState now = sim.PeekState();
                Vec2 to = now.Ball.Position.Horizontal - P(now, B).Position;
                return to * (1 / Math.Sqrt(to.LengthSquared));
            });

            Assert.Contains(events, e => e.Kind == SimEventKind.ChallengeWon && e.Subject == B);
            long won = events.First(e => e.Kind == SimEventKind.ChallengeWon && e.Subject == B).Tick;
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.ChallengeMissed && e.Tick < won);
        }

        [Fact]
        public void Better_tackling_reaches_further_and_commits_sooner()
        {
            // B one step from the end of a commit, the ball 0.8 m off: within a good tackler's reach, beyond a poor one's.
            SimEventKind Ends(double tackling)
            {
                SimState s = World(Free, 0.8, tackling);
                s.Players[1].Challenge = ChallengePhase.Commit;
                s.Players[1].ChallengeTicks = 1;
                s.Players[1].ChallengeLine = new Vec2(-1, 0);
                List<SimEvent> events = Run(new Simulator(Free, s), 1, Still, West);
                return Assert.Single(events, e => e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.ChallengeMissed).Kind;
            }

            Assert.Equal(SimEventKind.ChallengeWon, Ends(1.0));
            Assert.Equal(SimEventKind.ChallengeMissed, Ends(0.0));
            Assert.True(Simulator.CommitTicksFor(0.9, Free) < Simulator.CommitTicksFor(0.3, Free));

            // Repeatably: the same case, the same outcome and the same ball.
            Assert.Equal(Ends(0.6), Ends(0.6));
        }

        [Fact]
        public void The_beaten_footballer_may_play_the_loose_ball_after_a_stride()
        {
            // B wins it from the side; A, with a stride's touch cooldown, then chases and plays it.
            SimState s = World(Free, 0);
            s.Players[1].Position = new Vec2(0.6, -0.8);
            var sim = new Simulator(Free, s);
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 120, t => t < 14 ? Vec2.Zero : Direction(sim.PeekState()), _ => new Vec2(0, 1), states);
            SimEvent won = Assert.Single(events, e => e.Kind == SimEventKind.ChallengeWon);
            Assert.Equal(Free.TouchIntervalTicks, P(states[(int)won.Tick], A).TouchCooldown);
            SimEvent back = events.First(e => e.Kind == SimEventKind.Touched && e.Subject == A && e.Tick > won.Tick);
            Assert.True(back.Tick - won.Tick >= Free.TouchIntervalTicks);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Touched && e.Subject == B && e.Tick < won.Tick + Free.ChallengeRecoverTicks);

            static Vec2 Direction(SimState st)
            {
                Vec2 d = st.Ball.Position.Horizontal - st.Players[0].Position;
                double l = Math.Sqrt(d.LengthSquared);
                return l > 0 ? d * (1 / l) : Vec2.Zero;
            }
        }

        [Fact]
        public void No_challenge_takes_a_kicked_ball_and_a_wind_up_carries_on()
        {
            // A kicks as B commits: the kick connects first, and the ball in its flight is no one's.
            var sim = new Simulator(Free, World(Free, 0.95));
            var events = new List<SimEvent>();
            for (int i = 0; i < 40; i++)
            {
                KickIntent? kick = i == 0 ? new KickIntent(new Vec2(0, 1), 1, 0) : (KickIntent?)null;
                sim.Step(new[] { new Command(A, sim.Tick, Vec2.Zero, kick), new Command(B, sim.Tick, new Vec2(-1, 0)) }, events);
            }

            Assert.Contains(events, e => e.Kind == SimEventKind.Kicked && e.Subject == A);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.ChallengeWon);

            // A footballer in a challenge starts no kick.
            SimState s = World(Free, 0.8);
            s.Players[1].Challenge = ChallengePhase.Recover;
            s.Players[1].ChallengeTicks = 5;
            s.Players[1].ChallengeLine = new Vec2(-1, 0);
            s.Ball.Position = new Vec3(1.2, 0, Free.BallRadius);
            s.Ball.LastPlayedBy = 0;
            var recovering = new Simulator(Free, s);
            var e2 = new List<SimEvent>();
            recovering.Step(new[] { new Command(B, 0, Vec2.Zero, new KickIntent(new Vec2(-1, 0), 1, 0)) }, e2);
            Assert.Equal(0, P(recovering.PeekState(), B).WindUpTicks);
        }

        [Fact]
        public void Under_attached_a_challenge_frees_the_ball_and_an_opponent_cannot_take_it_by_being_nearer()
        {
            // Nearer than A, B still doesn't take it: only a challenge does.
            SimState near = World(Attached, 0.4);
            near.Players[1].Position = new Vec2(0.75, 0.05);
            var held = new Simulator(Attached, near);
            List<SimEvent> stand = Run(held, 10, Still, _ => new Vec2(0, 1));
            Assert.DoesNotContain(stand, e => e.Kind == SimEventKind.Attached && e.Subject == B);
            Assert.Equal(A, held.PeekState().Ball.AttachedTo);

            var sim = new Simulator(Attached, World(Attached, 0.8));
            List<SimEvent> events = Run(sim, 30, Still, West);
            SimEvent won = Assert.Single(events, e => e.Kind == SimEventKind.ChallengeWon);
            Assert.Contains(events, e => e.Kind == SimEventKind.Released && e.Subject == A && e.Tick == won.Tick);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Attached && e.Subject == B && e.Tick <= won.Tick + Attached.ChallengeRecoverTicks);
        }

        [Fact]
        public void A_challenge_moves_no_one_faster_than_their_legs_and_the_kick_only_model_has_none()
        {
            var states = new List<SimState>();
            Run(new Simulator(Free, World(Free, 0.8)), 60, Still, West, states);
            for (int i = 1; i < states.Count; i++)
            {
                Vec2 moved = P(states[i], B).Position - P(states[i - 1], B).Position;
                Assert.True(Math.Sqrt(moved.LengthSquared) <= PlayerCapabilities.Reference.MaxSpeed * Free.TickSeconds + 1e-9, $"step {i}: he moved {Math.Sqrt(moved.LengthSquared)} m");
            }

            // Under the kick-only model no one controls the ball, so no one challenges.
            var none = new SimConfig();
            Assert.DoesNotContain(Run(new Simulator(none, World(none, 0.8)), 60, Still, West), e => e.Kind == SimEventKind.ChallengeStarted);
        }

        [Fact]
        public void A_challenge_round_trips_through_a_snapshot_and_runs_the_same_twice()
        {
            var sim = new Simulator(Free, World(Free, 0.8));
            Run(sim, 5, Still, West);
            Assert.Equal(ChallengePhase.Commit, P(sim.PeekState(), B).Challenge);
            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), Free);
            Assert.Equal(sim.StateHash(), restored.StateHash());

            var first = new List<SimState>();
            var second = new List<SimState>();
            Run(sim, 40, Still, West, first);
            Run(restored, 40, Still, West, second);
            Assert.Equal(first.Select(s => new Simulator(Free, s).StateHash()), second.Select(s => new Simulator(Free, s).StateHash()));
        }

        [Fact]
        public void A_bad_challenge_state_is_refused()
        {
            SimState With(Action<SimState> change)
            {
                SimState s = World(Free, 0.8);
                change(s);
                return s;
            }

            Assert.Throws<ArgumentException>(() => new Simulator(Free, With(s => { s.Players[1].Challenge = ChallengePhase.Commit; s.Players[1].ChallengeTicks = 0; s.Players[1].ChallengeLine = new Vec2(1, 0); })));
            Assert.Throws<ArgumentException>(() => new Simulator(Free, With(s => { s.Players[1].Challenge = ChallengePhase.Commit; s.Players[1].ChallengeTicks = 2; s.Players[1].ChallengeLine = new Vec2(2, 0); })));
            Assert.Throws<ArgumentException>(() => new Simulator(Free, With(s => s.Players[1].ChallengeLine = new Vec2(1, 0))));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(Free, With(s => { s.Players[1].Challenge = ChallengePhase.Recover; s.Players[1].ChallengeTicks = 999; s.Players[1].ChallengeLine = new Vec2(1, 0); })));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(Free, With(s => s.Players[1].Capabilities.Tackling = 1.5)));
        }

        [Fact]
        public void Fuzzed_a_won_challenge_was_within_reach_and_knocks_the_ball_at_the_impulse()
        {
            var random = new Random(84);
            int won = 0;
            for (int trial = 0; trial < 300; trial++)
            {
                double tackling = random.NextDouble();
                SimState s = World(Free, 0.6 + random.NextDouble() * 0.5, tackling, new Vec2((random.NextDouble() - 0.5) * 8, (random.NextDouble() - 0.5) * 8));
                s.Players[1].Position += new Vec2(0, (random.NextDouble() - 0.5) * 1.0);
                var sim = new Simulator(Free, s);
                Vec2 aStick = new Vec2(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1);
                double angle = Math.PI + (random.NextDouble() - 0.5) * 1.5;
                Vec2 bStick = new Vec2(Math.Cos(angle), Math.Sin(angle));
                var events = new List<SimEvent>();
                for (int i = 0; i < 40; i++)
                {
                    SimState before = sim.PeekState().Clone();
                    events.Clear();
                    sim.Step(new[] { new Command(A, sim.Tick, aStick), new Command(B, sim.Tick, bStick) }, events);
                    if (!events.Any(e => e.Kind == SimEventKind.ChallengeWon)) continue;
                    won++;
                    SimState after = sim.PeekState();
                    double reach = Free.ChallengeReach * (0.75 + 0.25 * tackling);
                    double d = Math.Sqrt((before.Ball.Position.Horizontal - P(after, B).Position).LengthSquared);
                    Assert.True(d <= reach + 8 * Free.TickSeconds + 1e-9, $"trial {trial}: won from {d} m with reach {reach}");
                    // At the impulse, less the step's own rolling friction.
                    Assert.InRange(Math.Sqrt(after.Ball.Velocity.Horizontal.LengthSquared), Free.ChallengeImpulse - 0.05, Free.ChallengeImpulse);
                }
            }

            Assert.True(won > 30, $"only {won} challenges won");
        }

        [Fact]
        public void The_challenge_bench_shows_the_contest_and_matches_its_reference()
        {
            List<ChallengeCase> cases = ChallengeBench.Characterise(Free);
            Assert.Equal(2 * ChallengeBench.Approaches.Count * ChallengeBench.Speeds.Count * 3, cases.Count);

            // Against a standing dribbler every challenge wins; a sprinting one avoids most, and more
            // than a jogging one (since #96 a challenge commits only when it will reach the ball, so
            // a well-timed one can win at a sprint); good tacklers win at least as often as poor ones.
            Assert.All(cases.Where(c => c.DribblerSpeed == 0), c => Assert.Equal("won", c.Outcome));
            int WonAt(double speed) => cases.Count(c => c.DribblerSpeed == speed && c.Outcome == "won");
            int sprints = cases.Count(c => c.DribblerSpeed == 7);
            Assert.True(WonAt(7) * 2 < sprints, $"{WonAt(7)} of {sprints} sprinting dribblers lost the ball");
            Assert.True(WonAt(7) < WonAt(3.5), $"won {WonAt(7)} at a sprint, {WonAt(3.5)} at a jog");
            int Won(string profile) => cases.Count(c => c.Profile == profile && c.Outcome == "won");
            Assert.True(Won("good") > Won("poor"), $"good {Won("good")}, poor {Won("poor")}");

            var csv = new System.IO.StringWriter();
            ChallengeBench.WriteCsv(csv, Free, cases);
            string reference = System.IO.Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "challenge-reference.csv");
            Assert.Equal(System.IO.File.ReadAllText(reference), csv.ToString());
        }
    }
}
