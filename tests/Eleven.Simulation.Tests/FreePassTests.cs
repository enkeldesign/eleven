using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The quick pass with no receiver (#65): the free pass along the heading is weighted for the
    /// passer's own run, as #38 weights a pass for a running receiver, so a sprinting passer's tap
    /// goes ahead of them instead of being caught.
    /// </summary>
    public class FreePassTests
    {
        private const int A = TestWorld.Controller;

        private static SimState AtFeet(SimConfig config, Vec2? ballAt = null)
        {
            Vec2 at = ballAt ?? new Vec2(0.6, 0);
            return TestWorld.State(config, new Vec3(at.X, at.Y, config.BallRadius));
        }

        private static double Speed(Vec3 v) => Math.Sqrt(v.Horizontal.LengthSquared);

        // The free pass's launch speed for a passer whose run along the line is `run`.
        private static double Launch(SimConfig config, double run)
        {
            double arrival = config.PassArrivalSpeed + run;
            return Math.Sqrt(arrival * arrival + 2 * config.RollingFriction * config.Gravity * config.PassFreeDistance);
        }

        [Theory]
        [InlineData(ControlModel.Free)]
        [InlineData(ControlModel.Attached)]
        public void A_sprinting_passers_free_pass_runs_ahead_of_them(ControlModel model)
        {
            var config = new SimConfig(controlModel: model);
            var sim = new Simulator(config, AtFeet(config));
            int tapAt = 2 * config.TickRate;
            var states = new List<SimState>();
            var events = new List<List<SimEvent>>();
            for (int t = 0; t < 4 * config.TickRate; t++)
            {
                var step = new List<SimEvent>();
                sim.Step(new[] { new Command(A, sim.Tick, new Vec2(1, 0), quickPass: t == tapAt) }, step);
                states.Add(sim.PeekState().Clone());
                events.Add(step);
            }

            int contact = events.FindIndex(e => e.Any(x => x.Kind == SimEventKind.Kicked));
            Assert.True(contact >= tapAt, $"{model}: the tap never kicked");

            // The ball's lead over the passer along the pass line (the sprint is along +x).
            double Lead(int i) => states[i].Ball.Position.X - states[i].Players[0].Position.X;
            int q = config.TickRate / 4;
            double at0 = Lead(contact), at25 = Lead(contact + q), at50 = Lead(contact + 2 * q), at100 = Lead(contact + 4 * q);
            Assert.True(at25 > at0 && at50 > at25 && at100 > at50,
                $"{model}: lead {at0:F2} m at contact, {at25:F2} at 0.25 s, {at50:F2} at 0.5 s, {at100:F2} at 1 s");
            Assert.True(at100 > at0 + 1, $"{model}: the ball gained only {at100 - at0:F2} m on the passer in 1 s");
            Assert.DoesNotContain(events.Skip(contact).Take(config.TickRate).SelectMany(e => e), x => x.Kind == SimEventKind.Touched || x.Kind == SimEventKind.Attached);
        }

        [Fact]
        public void A_standing_passers_free_pass_keeps_its_weighting()
        {
            var config = new SimConfig();
            var sim = new Simulator(config, AtFeet(config));
            SimState? after = null;
            for (int t = 0; t < config.TickRate && after == null; t++)
            {
                var step = new List<SimEvent>();
                sim.Step(new[] { new Command(A, sim.Tick, Vec2.Zero, quickPass: t == 0) }, step);
                if (step.Any(x => x.Kind == SimEventKind.Kicked)) after = sim.PeekState();
            }

            Assert.NotNull(after);
            double rolled = Launch(config, 0) - config.RollingFriction * config.Gravity * config.TickSeconds;
            Assert.True(Math.Abs(Speed(after!.Ball.Velocity) - rolled) < 0.02, $"launched at {Speed(after.Ball.Velocity):F3} m/s; the standing weighting gives {rolled:F3}");
        }

        // The free pass goes along the heading, and the heading is the direction of travel, so a
        // passer never runs against the line: braking, reversing or turning when tapping can only
        // shrink the boost toward a standing passer's weighting, never below it.
        [Theory]
        [InlineData("braking", 0.0, 0.0)]
        [InlineData("reversing", -1.0, 0.0)]
        [InlineData("turning", 0.0, 1.0)]
        public void A_free_pass_is_never_slower_than_a_standing_passers(string motion, double x, double y)
        {
            var config = new SimConfig(controlModel: ControlModel.Free);
            var sim = new Simulator(config, AtFeet(config));
            int changeAt = 2 * config.TickRate;
            SimState? after = null;
            for (int t = 0; t < 4 * config.TickRate && after == null; t++)
            {
                Vec2 move = t < changeAt ? new Vec2(1, 0) : new Vec2(x, y);
                var step = new List<SimEvent>();
                sim.Step(new[] { new Command(A, sim.Tick, move, quickPass: t == changeAt + 3) }, step);
                if (step.Any(e => e.Kind == SimEventKind.Kicked)) after = sim.PeekState();
            }

            Assert.NotNull(after);
            double standing = Launch(config, 0) - config.RollingFriction * config.Gravity * config.TickSeconds;
            Assert.True(Speed(after!.Ball.Velocity) >= standing - 0.02, $"{motion}: launched at {Speed(after.Ball.Velocity):F3} m/s; a standing passer's free pass is {standing:F3}");
        }
    }
}
