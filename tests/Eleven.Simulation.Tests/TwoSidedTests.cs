using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Two sides and two goals (#82). The match's far goal, at −x, is the near one turned half a
    /// turn about the centre spot, so each test here plays a case toward +x and the same case turned
    /// toward −x, and asks for the same football: the frame, the line, the score and the keeper.
    /// Practice, with no sides, keeps its one goal.
    /// </summary>
    public class TwoSidedTests
    {
        private static readonly SimConfig C = new SimConfig();

        private static double R => C.BallRadius;

        // ---- Half a turn about the centre spot -------------------------------------------------

        private static Vec2 Turn(Vec2 v) => new Vec2(-v.X, -v.Y);

        private static Vec3 Turn(Vec3 v) => new Vec3(-v.X, -v.Y, v.Z);

        private static double TurnAngle(double a) => a > 0 ? a - Math.PI : a + Math.PI;

        /// <summary>The state turned half a turn about the centre spot, each side now attacking the other way.</summary>
        internal static SimState Turned(SimState state)
        {
            SimState t = state.Clone();
            t.Ball.Position = Turn(t.Ball.Position);
            t.Ball.Velocity = Turn(t.Ball.Velocity);
            t.Ball.Spin = Turn(t.Ball.Spin);
            for (int i = 0; i < t.Players.Length; i++)
            {
                t.Players[i].Position = Turn(t.Players[i].Position);
                t.Players[i].Velocity = Turn(t.Players[i].Velocity);
                t.Players[i].Heading = TurnAngle(t.Players[i].Heading);
                t.Players[i].WindUpDirection = TurnAngle(t.Players[i].WindUpDirection);
                t.Players[i].AftertouchDirection = TurnAngle(t.Players[i].AftertouchDirection);
            }

            for (int i = 0; i < t.Keepers.Length; i++) t.Keepers[i].DiveVelocity = Turn(t.Keepers[i].DiveVelocity);
            for (int i = 0; i < t.Teams.Length; i++) t.Teams[i].AttackSign = -t.Teams[i].AttackSign;
            return t;
        }

        private static Command Turned(Command c) =>
            new Command(c.ControllerId, c.Tick, Turn(c.Move), c.Kick.HasValue ? new KickIntent(Turn(c.Kick.Value.Direction), c.Kick.Value.Strength, c.Kick.Value.Loft) : (KickIntent?)null,
                c.Aftertouch, c.QuickPass, c.Dive.HasValue ? Turn(c.Dive.Value) : (Vec3?)null);

        private static void AssertTurned(SimState a, SimState b, double tolerance, string where)
        {
            void Near(double x, double y, string what) => Assert.True(Math.Abs(x - y) <= tolerance, $"{where}: {what} {x:R} against {y:R}");
            void Near2(Vec2 x, Vec2 y, string what)
            {
                Near(x.X, -y.X, what + ".x");
                Near(x.Y, -y.Y, what + ".y");
            }

            Near(a.Ball.Position.X, -b.Ball.Position.X, "ball x");
            Near(a.Ball.Position.Y, -b.Ball.Position.Y, "ball y");
            Near(a.Ball.Position.Z, b.Ball.Position.Z, "ball z");
            Near(a.Ball.Velocity.X, -b.Ball.Velocity.X, "ball vx");
            Near(a.Ball.Velocity.Y, -b.Ball.Velocity.Y, "ball vy");
            Near(a.Ball.Velocity.Z, b.Ball.Velocity.Z, "ball vz");
            Assert.True(a.Ball.Line == b.Ball.Line, $"{where}: line {a.Ball.Line} against {b.Ball.Line}");
            Assert.True(a.Ball.HeldBy == b.Ball.HeldBy, $"{where}: held by {a.Ball.HeldBy} against {b.Ball.HeldBy}");
            for (int i = 0; i < a.Players.Length; i++)
            {
                Near2(a.Players[i].Position, b.Players[i].Position, $"player {a.Players[i].Id} position");
                Near2(a.Players[i].Velocity, b.Players[i].Velocity, $"player {a.Players[i].Id} velocity");
            }

            for (int i = 0; i < a.Keepers.Length; i++)
            {
                Assert.True(a.Keepers[i].Phase == b.Keepers[i].Phase, $"{where}: keeper {a.Keepers[i].PlayerId} {a.Keepers[i].Phase} against {b.Keepers[i].Phase}");
            }
        }

        // ---- Worlds ---------------------------------------------------------------------------

        private static TeamState[] Sides(int homeAttacks = 1) => new[]
        {
            new TeamState { Id = 1, AttackSign = homeAttacks },
            new TeamState { Id = 2, AttackSign = -homeAttacks },
        };

        // A ball alone on a pitch with two sides.
        private static SimState BallOnly(Vec3 at, Vec3 velocity, bool sides = true) => new SimState
        {
            Seed = 7,
            Ball = new BallState { Id = 100, Position = at, Velocity = velocity },
            Teams = sides ? Sides() : Array.Empty<TeamState>(),
        };

        private static List<SimEvent> Run(Simulator sim, int ticks, List<SimState>? states = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                sim.Step(Array.Empty<Command>(), events);
                states?.Add(sim.PeekState().Clone());
            }

            return events;
        }

        private static string Kinds(IEnumerable<SimEvent> events) => string.Join(" ", events.Select(e => $"{e.Tick}:{e.Kind}:{e.Other}"));

        // Shots at the +x goal, 6 m out: through the middle, inside and outside each post, onto
        // each post, onto the bar, under and over it, and off the inside of a post.
        public static IEnumerable<object[]> Shots()
        {
            double post = C.PostAxisY, bar = C.CrossbarAxisZ, reach = R + C.PostWidth / 2;
            yield return new object[] { "middle", 0.0, R, 0.0, 12.0, 0.0 };
            yield return new object[] { "inside the right post", -(post - reach - 0.02), R, 0.0, 12.0, 0.0 };
            yield return new object[] { "inside the left post", post - reach - 0.02, R, 0.0, 12.0, 0.0 };
            yield return new object[] { "outside the right post", -(post + reach + 0.02), R, 0.0, 12.0, 0.0 };
            yield return new object[] { "outside the left post", post + reach + 0.02, R, 0.0, 12.0, 0.0 };
            yield return new object[] { "onto the right post", -post, R, 0.0, 20.0, 0.0 };
            yield return new object[] { "onto the left post", post, R, 0.0, 20.0, 0.0 };
            yield return new object[] { "off the inside of the right post", -(post - reach * 0.8), R, 0.0, 15.0, 0.0 };
            yield return new object[] { "off the inside of the left post", post - reach * 0.8, R, 0.0, 15.0, 0.0 };
            yield return new object[] { "onto the bar", 0.3, bar - 0.02, 0.0, 25.0, 1.0 };
            yield return new object[] { "under the bar", 0.3, bar - reach - 0.3, 0.0, 25.0, 1.0 };
            yield return new object[] { "over the bar", 0.3, bar + reach + 0.3, 0.0, 25.0, 1.0 };
            yield return new object[] { "across the goal, wide", 2.0, R, 9.0, 14.0, 0.0 };
            yield return new object[] { "across the goal, in off the far post", -2.0, R, 4.2, 14.0, 0.0 };
        }

        // ---- The far goal -----------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Shots))]
        public void A_shot_at_the_far_goal_is_the_shot_at_the_near_goal_turned(string name, double y, double z, double vy, double vx, double vz)
        {
            SimState near = BallOnly(new Vec3(C.GoalLineX - 6, y, z), new Vec3(vx, vy, vz));
            var nearStates = new List<SimState>();
            var farStates = new List<SimState>();
            List<SimEvent> nearEvents = Run(new Simulator(C, near), 180, nearStates);
            List<SimEvent> farEvents = Run(new Simulator(C, Turned(near)), 180, farStates);

            // The same events on the same ticks, a post named as the attacker sees it; and the ball,
            // step for step, exactly the near shot's turned.
            Assert.True(nearEvents.Count(e => e.Kind != SimEventKind.Bounced) > 0 || name.Contains("over"), $"{name}: nothing happened at the near goal");
            // The turned world's sides attack the other way, so the same side scores.
            Assert.Equal(Kinds(nearEvents), Kinds(farEvents));
            Assert.All(nearEvents.Where(e => e.Kind == SimEventKind.Goal), e => Assert.Equal(1, e.Other));
            for (int i = 0; i < nearStates.Count; i++) AssertTurned(nearStates[i], farStates[i], 0, $"{name}, step {i}");
        }

        [Fact]
        public void A_goal_is_scored_for_the_side_attacking_that_end()
        {
            // Home (1) attacks +x, away (2) attacks −x.
            List<SimEvent> near = Run(new Simulator(C, BallOnly(new Vec3(C.GoalLineX - 3, 0, R), new Vec3(12, 0, 0))), 60);
            Assert.Equal(1, Assert.Single(near, e => e.Kind == SimEventKind.Goal).Other);

            List<SimEvent> far = Run(new Simulator(C, BallOnly(new Vec3(-(C.GoalLineX - 3), 0, R), new Vec3(-12, 0, 0))), 60);
            Assert.Equal(2, Assert.Single(far, e => e.Kind == SimEventKind.Goal).Other);
            Assert.Single(far, e => e.Kind == SimEventKind.GoalLineCrossed);

            // With the sides attacking the other way, the same two balls score for the other side.
            SimState turnedSides = BallOnly(new Vec3(C.GoalLineX - 3, 0, R), new Vec3(12, 0, 0));
            turnedSides.Teams = Sides(homeAttacks: -1);
            Assert.Equal(2, Assert.Single(Run(new Simulator(C, turnedSides), 60), e => e.Kind == SimEventKind.Goal).Other);
        }

        [Fact]
        public void Both_goals_score_in_one_run_each_once()
        {
            var sim = new Simulator(C, BallOnly(new Vec3(C.GoalLineX - 3, 0, R), new Vec3(12, 0, 0)));
            List<SimEvent> events = Run(sim, 60);
            Assert.Equal(LineState.Goal, sim.PeekState().Ball.Line);

            // The same ball, put down in front of the other goal and rolled in.
            SimState next = sim.PeekState().Clone();
            next.Ball.Position = new Vec3(-(C.GoalLineX - 3), 1, R);
            next.Ball.Velocity = new Vec3(-12, 0, 0);
            next.Ball.Line = LineState.InPlay;
            var second = new Simulator(C, next);
            events.AddRange(Run(second, 60));
            Assert.Equal(new[] { 1, 2 }, events.Where(e => e.Kind == SimEventKind.Goal).Select(e => e.Other));
            Assert.Equal(LineState.Goal, second.PeekState().Ball.Line);
        }

        [Fact]
        public void The_far_goals_posts_are_named_as_its_attacker_sees_them()
        {
            // Attacking −x, the attacker's right is +y.
            double post = C.PostAxisY;
            List<SimEvent> right = Run(new Simulator(C, BallOnly(new Vec3(-(C.GoalLineX - 6), post, R), new Vec3(-20, 0, 0))), 60);
            Assert.Equal((int)FramePart.RightPost, right.First(e => e.Kind == SimEventKind.HitFrame).Other);
            List<SimEvent> left = Run(new Simulator(C, BallOnly(new Vec3(-(C.GoalLineX - 6), -post, R), new Vec3(-20, 0, 0))), 60);
            Assert.Equal((int)FramePart.LeftPost, left.First(e => e.Kind == SimEventKind.HitFrame).Other);
        }

        [Fact]
        public void A_prediction_with_both_goals_is_the_step_at_either_end()
        {
            // INV-13: what a step does to a ball no one touches, a prediction says, at −x too.
            foreach (double y in new[] { 0.0, C.PostAxisY, -(C.PostAxisY - 0.1) })
            {
                var at = new Vec3(-(C.GoalLineX - 6), y, R);
                var velocity = new Vec3(-20, 0.3, 2);
                var states = new List<SimState>();
                Run(new Simulator(C, BallOnly(at, velocity)), 90, states);
                var predicted = new List<Vec3>();
                BallFlight.Predict(C, at, velocity, Vec3.Zero, 90, predicted, bothGoals: true);
                Assert.Equal(states.Select(s => s.Ball.Position), predicted);
            }
        }

        [Fact]
        public void The_same_shots_score_hit_the_frame_and_are_saved_alike_at_both_ends()
        {
            List<SideCount> ends = SideParity.Measure(SpikeScenario.Config());
            SideCount near = ends[0], far = ends[1];
            Assert.Equal(156, near.Shots);
            Assert.Equal((near.Shots, near.Goals, near.FrameHits, near.Held, near.Parried), (far.Shots, far.Goals, far.FrameHits, far.Held, far.Parried));

            // Every kind of outcome happens, so the parity means something.
            Assert.True(near.Goals > 0 && near.FrameHits > 0 && near.Held > 0 && near.Parried > 0, near.ToString());

            // Home attacks the end shot at, both times.
            Assert.Equal((near.Goals, 0), (near.GoalsForHome, near.GoalsForAway));
            Assert.Equal((far.Goals, 0), (far.GoalsForHome, far.GoalsForAway));

            // The same again gives the same runs.
            Assert.Equal(near.Digest, SideParity.Measure(SpikeScenario.Config())[0].Digest);
        }

        // ---- Practice keeps its one goal ------------------------------------------------------------

        [Fact]
        public void With_no_sides_there_is_one_goal_and_its_goal_names_no_side()
        {
            List<SimEvent> near = Run(new Simulator(C, BallOnly(new Vec3(C.GoalLineX - 3, 0, R), new Vec3(12, 0, 0), sides: false)), 60);
            Assert.Equal(0, Assert.Single(near, e => e.Kind == SimEventKind.Goal).Other);

            // Nothing stands at −x: a ball at where a post would be rolls through, and no line is crossed.
            var states = new List<SimState>();
            List<SimEvent> far = Run(new Simulator(C, BallOnly(new Vec3(-(C.GoalLineX - 6), C.PostAxisY, R), new Vec3(-20, 0, 0), sides: false)), 60, states);
            Assert.Empty(far);
            Assert.True(states[states.Count - 1].Ball.Position.X < -C.GoalLineX - 1);
            Assert.Equal(LineState.InPlay, states[states.Count - 1].Ball.Line);
        }

        // ---- The sides are plain, checked data --------------------------------------------------------

        [Fact]
        public void The_sides_round_trip_through_a_snapshot()
        {
            SimState start = OneVOne.Start(C, seed: 11, homeAttackSign: -1);
            var sim = new Simulator(C, start);
            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), C);
            Assert.Equal(sim.StateHash(), restored.StateHash());
            TeamState[] teams = restored.PeekState().Teams;
            Assert.Equal(new[] { (1, -1), (2, 1) }, teams.Select(t => (t.Id, t.AttackSign)));

            // The way a side attacks is state: the same world with the sides the other way hashes differently.
            SimState other = start.Clone();
            other.Teams = Sides(homeAttacks: 1);
            Assert.NotEqual(sim.StateHash(), new Simulator(C, other).StateHash());
        }

        [Fact]
        public void A_state_has_no_sides_or_two_that_attack_opposite_ways_with_every_player_on_one()
        {
            SimState With(Action<SimState> change)
            {
                SimState s = OneVOne.Start(C, seed: 11);
                change(s);
                return s;
            }

            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Teams = new[] { s.Teams[0] })));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Teams[1].AttackSign = 1)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Teams[0].AttackSign = 0)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Teams[1].Id = 1)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Teams[0].Id = 0)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Players[0].TeamId = 9)));
            new Simulator(C, With(s => s.Teams = Array.Empty<TeamState>()));
        }

        // ---- Each keeper guards his own goal ----------------------------------------------------------

        [Fact]
        public void The_one_v_one_world_has_two_sides_two_keepers_and_a_controller_each()
        {
            SimState s = OneVOne.Start(C, seed: 11);
            new Simulator(C, s);
            Assert.Equal(4, s.Players.Length);
            Assert.Equal(4, s.Players.Select(p => p.OwnerControllerId).Distinct().Count());
            Assert.Equal(new[] { 1, 1, 2, 2 }, s.Players.Select(p => p.TeamId));
            Assert.Equal(new[] { OneVOne.HomeKeeper, OneVOne.AwayKeeper }, s.Keepers.Select(k => k.PlayerId));

            // Home attacks +x, so home's keeper stands in front of −x and away's in front of +x.
            Assert.Equal(-(C.GoalLineX - OneVOne.KeeperStandOff), s.Players[1].Position.X);
            Assert.Equal(C.GoalLineX - OneVOne.KeeperStandOff, s.Players[3].Position.X);

            // The other way round, the whole start is turned.
            SimState turned = OneVOne.Start(C, seed: 11, homeAttackSign: -1);
            AssertTurned(s, turned, 0, "start");
        }

        [Fact]
        public void Each_keeper_stays_at_his_own_goal_and_only_his_controller_commands_him()
        {
            var sim = new Simulator(C, OneVOne.Start(C, seed: 11));
            for (int i = 0; i < 300; i++)
            {
                var commands = new List<Command>();
                KeeperControllers.Append(sim, commands);
                Assert.Equal(new[] { OneVOne.HomeKeeperController, OneVOne.AwayKeeperController }, commands.Select(c => c.ControllerId));
                sim.Step(commands, new List<SimEvent>());
            }

            SimState s = sim.PeekState();
            Assert.InRange(s.Players[1].Position.X, -C.GoalLineX, -C.GoalLineX + 3);
            Assert.InRange(s.Players[3].Position.X, C.GoalLineX - 3, C.GoalLineX);
        }

        [Fact]
        public void A_keeper_at_the_far_goal_sees_the_pitch_from_his_own_goal()
        {
            SimState s = OneVOne.Start(C, seed: 11);
            s.Ball.Position = new Vec3(-30, 4, 1.2);
            s.Ball.Velocity = new Vec3(-18, 1, 2);
            s.Ball.Spin = new Vec3(1, 2, 3);

            // Away's keeper, at +x, sees the world as it is. Home's, at −x, sees it turned: his
            // own goal at +x, as every keeper's is to him.
            KeeperObservation away = Perception.OfKeeper(s, 1);
            Assert.Equal(s.Ball.Position, away.BallPosition);
            Assert.Equal(s.Players[3].Position, away.Position);

            KeeperObservation home = Perception.OfKeeper(s, 0);
            Assert.Equal(new Vec3(30, -4, 1.2), home.BallPosition);
            Assert.Equal(new Vec3(18, -1, 2), home.BallVelocity);
            Assert.Equal(new Vec3(-1, -2, 3), home.BallSpin);
            Assert.Equal(new Vec2(C.GoalLineX - OneVOne.KeeperStandOff, 0), home.Position);

            // The same observation as the +x keeper's of the turned world.
            KeeperObservation turned = Perception.OfKeeper(Turned(s), 0);
            Assert.Equal(turned.BallPosition, home.BallPosition);
            Assert.Equal(turned.Position, home.Position);
        }

        [Theory]
        [InlineData(1.5, 0.3, 22.0)]
        [InlineData(-2.5, 0.3, 24.0)]
        [InlineData(3.2, 1.6, 26.0)]
        [InlineData(0.2, 0.2, 14.0)]
        public void The_far_keeper_saves_what_the_near_keeper_saves(double aimY, double aimZ, double speed)
        {
            // A shot from 16 m at the goal away's keeper guards, and the same shot turned at home's.
            SimState near = OneVOne.Start(C, seed: 11);
            Vec3 from = new Vec3(C.GoalLineX - 16, 0, R);
            Vec3 to = new Vec3(C.GoalLineX, aimY, aimZ);
            Vec3 line = to - from;
            double length = Math.Sqrt(line.LengthSquared);
            near.Ball.Position = from;
            near.Ball.Velocity = line * (speed / length) + new Vec3(0, 0, 1.5);
            near.Players[0].Position = new Vec2(0, 20);
            near.Players[2].Position = new Vec2(0, -20);

            var nearStates = new List<SimState>();
            var farStates = new List<SimState>();
            List<SimEvent> nearEvents = RunWithBrains(new Simulator(C, near), 150, nearStates);
            List<SimEvent> farEvents = RunWithBrains(new Simulator(C, Turned(near)), 150, farStates);

            Assert.Contains(nearEvents, e => e.Kind == SimEventKind.KeeperDived || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.KeeperParried);
            // The turned world keeps its footballers, so the same keeper, away's, faces both shots:
            // at +x, then at −x.
            Assert.Equal(Kinds(nearEvents), Kinds(farEvents));
            Assert.Equal(string.Join(" ", nearEvents.Select(e => e.Subject)), string.Join(" ", farEvents.Select(e => e.Subject)));
            Assert.True(farStates[0].Players[3].Position.X < 0);
            for (int i = 0; i < nearStates.Count; i++) AssertTurned(nearStates[i], farStates[i], 1e-6, $"step {i}");
        }

        private static List<SimEvent> RunWithBrains(Simulator sim, int ticks, List<SimState>? states = null, List<ulong>? hashes = null, List<Command[]>? recorded = null, Func<long, IEnumerable<Command>>? others = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var commands = new List<Command>();
                if (others != null) commands.AddRange(others(sim.Tick));
                KeeperControllers.Append(sim, commands);
                recorded?.Add(commands.ToArray());
                sim.Step(commands, events);
                states?.Add(sim.PeekState().Clone());
                hashes?.Add(sim.StateHash());
            }

            return events;
        }

        // ---- Four actors, deterministic ---------------------------------------------------------------

        // The bench's config: FREE ball control, so the outfielders touch the ball as they run.
        private static readonly SimConfig Free = SpikeScenario.Config();

        // Home runs at the ball and shoots at +x; away runs at the ball and shoots at −x.
        private static IEnumerable<Command> Script(long tick)
        {
            Vec2 homeMove = tick < 150 ? new Vec2(1, 0.1) : new Vec2(0.4, -1);
            KickIntent? homeKick = tick % 60 == 30 ? new KickIntent(new Vec2(1, 0.08), 1, 0.3) : (KickIntent?)null;
            yield return new Command(OneVOne.HomeController, tick, homeMove, homeKick);
            Vec2 awayMove = tick < 200 ? new Vec2(-1, 0.05) : new Vec2(-0.5, 1);
            KickIntent? awayKick = tick % 90 == 45 ? new KickIntent(new Vec2(-1, -0.05), 0.9, 0.2) : (KickIntent?)null;
            yield return new Command(OneVOne.AwayController, tick, awayMove, awayKick);
        }

        [Fact]
        public void A_one_v_one_run_is_the_same_twice_from_a_snapshot_and_from_its_recorded_commands()
        {
            const int Ticks = 900;
            var first = new List<ulong>();
            var recorded = new List<Command[]>();
            var sim = new Simulator(Free, OneVOne.Start(Free, seed: 0xE11));
            List<SimEvent> events = RunWithBrains(sim, 300, hashes: first, recorded: recorded, others: Script);
            byte[] snapshot = Snapshot.Capture(sim);
            events.AddRange(RunWithBrains(sim, Ticks - 300, hashes: first, recorded: recorded, others: Script));

            // Something happened with every actor.
            Assert.Contains(events, e => e.Kind == SimEventKind.Kicked && e.Subject == OneVOne.HomePlayer);
            SimState end = sim.PeekState();
            Assert.NotEqual(OneVOne.OpponentDistance, end.Players[2].Position.X);

            // Twice.
            var second = new List<ulong>();
            RunWithBrains(new Simulator(Free, OneVOne.Start(Free, seed: 0xE11)), Ticks, hashes: second, others: Script);
            Assert.Equal(first, second);

            // From the snapshot at tick 300.
            var resumed = new List<ulong>();
            RunWithBrains(Snapshot.Restore(snapshot, Free), Ticks - 300, hashes: resumed, others: Script);
            Assert.Equal(first.Skip(300), resumed);

            // From the recorded commands, with no brain running.
            var replay = new Simulator(Free, OneVOne.Start(Free, seed: 0xE11));
            var replayed = new List<ulong>();
            foreach (Command[] commands in recorded)
            {
                replay.Step(commands, new List<SimEvent>());
                replayed.Add(replay.StateHash());
            }

            Assert.Equal(first, replayed);
        }

        [Fact]
        public void A_one_v_one_run_turned_is_the_same_football_the_other_way()
        {
            var a = new List<SimState>();
            var b = new List<SimState>();
            List<SimEvent> ea = RunWithBrains(new Simulator(Free, OneVOne.Start(Free, seed: 0xE11)), 600, states: a, others: Script);
            List<SimEvent> eb = RunWithBrains(new Simulator(Free, OneVOne.Start(Free, seed: 0xE11, homeAttackSign: -1)), 600, states: b, others: t => Script(t).Select(Turned));

            // Kick errors are drawn as angles, so the two runs agree to rounding, not to the bit.
            for (int i = 0; i < a.Count; i++) AssertTurned(a[i], b[i], 1e-6, $"step {i}");
            Assert.Equal(Kinds(ea.Where(e => e.Kind != SimEventKind.HitFrame)), Kinds(eb.Where(e => e.Kind != SimEventKind.HitFrame)));
        }
    }
}
