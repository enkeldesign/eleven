using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.AI;
using Xunit;
using static Eleven.Simulation.Tests.KeeperWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The keeper's brain (#69): it decides on his Observation alone (INV-9), predicts with the
    /// simulation's own ball (INV-13), and plays a shot from the penalty spot. docs/simulation/keeper.md.
    /// </summary>
    public class KeeperBrainTests
    {
        private static readonly SimConfig C = new SimConfig();

        private static KeeperObservation Seen(Vec2 keeper, Vec3 ball, Vec3 ballVelocity, KeeperPhase phase = KeeperPhase.Position, KeeperCapabilities? capabilities = null) =>
            new KeeperObservation(0, keeper, Vec2.Zero, Math.PI, phase, 0, capabilities ?? KeeperCapabilities.Reference, PlayerCapabilities.Reference.MaxSpeed,
                ball, ballVelocity, Vec3.Zero, false, false);

        // A shot along the ground from (x, y) to where it crosses the goal line at `to`.
        private static (Vec3 At, Vec3 Velocity) Shot(double x, double y, double to, double speed)
        {
            var at = new Vec3(x, y, C.BallRadius);
            Vec2 line = new Vec2(C.GoalLineX, to) - at.Horizontal;
            Vec2 v = line * (speed / Math.Sqrt(line.LengthSquared));
            return (at, new Vec3(v.X, v.Y, 0));
        }

        [Fact]
        public void His_observation_carries_no_command_and_no_state()
        {
            var allowed = new[] { typeof(long), typeof(int), typeof(double), typeof(bool), typeof(Vec2), typeof(Vec3), typeof(KeeperPhase), typeof(KeeperCapabilities) };
            foreach (var property in typeof(KeeperObservation).GetProperties())
            {
                Assert.Contains(property.PropertyType, allowed);
            }

            Assert.Empty(typeof(KeeperObservation).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        }

        [Fact]
        public void On_his_feet_he_steps_onto_the_line_from_the_ball_to_the_goal()
        {
            Vec2 spot = Spot(C);
            var ball = new Vec3(C.GoalLineX - 20, 8, C.BallRadius);
            KeeperDecision d = KeeperBrain.Decide(Seen(spot, ball, Vec3.Zero), C);

            // The line from the goal's centre toward the ball: he moves toward it, to the ball's side.
            Assert.True(d.Move.Y > 0, $"he moved {d.Move} for a ball out to +y");
            Assert.Null(d.Dive);
        }

        [Fact]
        public void For_a_shot_at_the_corner_he_dives_for_where_it_will_cross()
        {
            Vec2 spot = Spot(C);
            (Vec3 at, Vec3 v) = Shot(C.GoalLineX - 11, 0, 3.0, 16);
            KeeperDecision d = KeeperBrain.Decide(Seen(spot, at, v), C);

            Assert.True(d.Dive.HasValue, "he didn't dive");
            // Where the shot crosses his line, x = his x, by the ball's own flight.
            var path = new List<Vec3>();
            BallFlight.Predict(C, at, v, Vec3.Zero, 2 * C.TickRate, path);
            Vec3 crossing = path.First(p => p.X >= spot.X);
            Assert.True(Math.Abs(d.Dive!.Value.Y - crossing.Y) < 0.3, $"he dived for y = {d.Dive.Value.Y:F2}; the shot crosses at {crossing.Y:F2}");
        }

        [Fact]
        public void A_shot_well_wide_draws_no_dive()
        {
            (Vec3 at, Vec3 v) = Shot(C.GoalLineX - 11, 0, 6.5, 16);
            Assert.Null(KeeperBrain.Decide(Seen(Spot(C), at, v), C).Dive);
        }

        [Fact]
        public void A_shot_straight_at_him_draws_no_dive()
        {
            (Vec3 at, Vec3 v) = Shot(C.GoalLineX - 11, 0, 0, 16);
            Assert.Null(KeeperBrain.Decide(Seen(Spot(C), at, v), C).Dive);
        }

        [Fact]
        public void He_predicts_with_the_simulations_own_ball()
        {
            // A spinning, lofted shot that meets the post: the prediction is the step's ball, bit for bit.
            var at = new Vec3(C.GoalLineX - 8, 1, C.BallRadius);
            var v = new Vec3(14, 3.6, 3);
            var spin = new Vec3(0, 0, 8);
            var predicted = new List<Vec3>();
            BallFlight.Predict(C, at, v, spin, 2 * C.TickRate, predicted);

            var sim = new Simulator(C, new SimState { Ball = new BallState { Id = 100, Position = at, Velocity = v, Spin = spin } });
            var stepped = new List<Vec3>();
            for (int i = 0; i < 2 * C.TickRate; i++)
            {
                sim.Step(Array.Empty<Command>(), new List<SimEvent>());
                stepped.Add(sim.PeekState().Ball.Position);
            }

            Assert.Equal(stepped, predicted);
        }

        // Headless save scenarios: a penalty from the spot, the keeper played by his brain.
        [Fact]
        public void The_reference_keeper_saves_a_central_penalty_and_a_slow_one_is_beaten_in_the_corner()
        {
            SimState central = Penalty(C);
            List<SimEvent> saved = RunWithBrain(new Simulator(C, central), 3 * C.TickRate,
                t => new Command(ShooterController, t, Vec2.Zero, t == 0 ? KeeperWorld.Shot(C, central, 0.5, 0.8, 0.1) : (KickIntent?)null));
            Assert.True(Saved(saved), "the reference keeper didn't save a central penalty");
            Assert.DoesNotContain(saved, e => e.Kind == SimEventKind.Goal);

            SimState corner = Penalty(C, KeeperCapabilities.Slow);
            List<SimEvent> beaten = RunWithBrain(new Simulator(C, corner), 3 * C.TickRate,
                t => new Command(ShooterController, t, Vec2.Zero, t == 0 ? KeeperWorld.Shot(C, corner, 3.2, 0.9) : (KickIntent?)null));
            Assert.Contains(beaten, e => e.Kind == SimEventKind.KeeperDived);
            Assert.Contains(beaten, e => e.Kind == SimEventKind.Goal);
        }

        [Fact]
        public void A_save_runs_the_same_twice_and_its_recording_replays_without_the_brain()
        {
            SimState start = Penalty(C);
            Func<long, Command?> shooter = t => new Command(ShooterController, t, Vec2.Zero, t == 0 ? KeeperWorld.Shot(C, start, 1.6, 0.8) : (KickIntent?)null);
            int ticks = 3 * C.TickRate;

            var first = new List<ulong>();
            var recorded = new List<Command[]>();
            List<SimEvent> events = RunWithBrain(new Simulator(C, start), ticks, shooter, first, recorded);
            var second = new List<ulong>();
            RunWithBrain(new Simulator(C, start), ticks, shooter, second);
            Assert.True(Saved(events), "no save to replay");
            Assert.Equal(first, second);

            // The keeper's commands are recorded with the shooter's, so a replay runs no brain.
            var replay = new Simulator(C, start);
            var replayed = new List<ulong>();
            foreach (Command[] step in recorded)
            {
                replay.Step(step, new List<SimEvent>());
                replayed.Add(replay.StateHash());
            }

            Assert.Equal(first, replayed);
        }
    }
}
