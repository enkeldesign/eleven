using System;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Maths;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>INV-5 config hashing and the canonical state encoding behind every hash.</summary>
    public class EncodingTests
    {
        [Fact]
        public void Config_hash_is_stable_and_pinned()
        {
            // Golden values: they change only when the config encoding or defaults change, which
            // needs its own commit with a stated reason.
            Assert.Equal(0xd572d88ff7150194UL, new SimConfig(mathBackend: MathBackend.Portable).Hash);
            Assert.Equal(new SimConfig().Hash, new SimConfig().Hash);
        }

        [Fact]
        public void Maths_backend_and_every_value_are_part_of_the_config_hash()
        {
            ulong baseline = new SimConfig().Hash;
            Assert.NotEqual(baseline, new SimConfig(mathBackend: MathBackend.SystemMath).Hash);
            Assert.NotEqual(baseline, new SimConfig(tickRate: 50).Hash);
            Assert.NotEqual(baseline, new SimConfig(goalLineX: 52.5000001).Hash);
            Assert.NotEqual(baseline, new SimConfig(kickCooldownTicks: 31).Hash);
        }

        [Fact]
        public void Non_finite_config_values_are_refused()
        {
            Assert.Throws<InvalidOperationException>(() => new SimConfig(gravity: double.NaN));
        }

        [Fact]
        public void Non_finite_state_values_fail_validation_instead_of_being_encoded()
        {
            var config = Config();
            SimState state = State(config, ballVelocity: new Vec3(double.PositiveInfinity, 0, 0));

            var error = Assert.Throws<InvalidOperationException>(() => new Simulator(config, state));
            Assert.Contains("Ball.Velocity", error.Message);
        }

        [Fact]
        public void Encoding_is_little_endian_ieee_754()
        {
            var w = new CanonicalWriter();
            w.WriteInt32(0x01020304);
            w.WriteDouble(1.0, "one");
            w.WriteBool(true);

            Assert.Equal(new byte[] { 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0xF0, 0x3F, 1 }, w.ToArray());
        }

        [Fact]
        public void Negative_zero_is_distinct_state()
        {
            var config = Config();
            var positive = Simulator(config, State(config, ballVelocity: new Vec3(0.0, 0, 0)));
            var negative = Simulator(config, State(config, ballVelocity: new Vec3(-0.0, 0, 0)));

            Assert.NotEqual(positive.StateHash(), negative.StateHash());
        }

        [Fact]
        public void State_round_trips_through_its_encoding()
        {
            var config = Config();
            SimState state = State(config, ballPosition: new Vec3(1.5, -2.25, 3), ballVelocity: new Vec3(4, 5, 6), ballSpin: new Vec3(0, 0, 7));
            state.Tick = 1234;
            state.Ball.BeyondGoalLine = true;
            state.Dots[0].Heading = -1.25;
            state.Dots[0].KickCooldown = 3;

            var w = new CanonicalWriter();
            state.Encode(w);
            SimState decoded = SimState.Decode(new CanonicalReader(w.ToArray()));

            var again = new CanonicalWriter();
            decoded.Encode(again);
            Assert.Equal(w.ToArray(), again.ToArray());
        }

        [Fact]
        public void Every_state_field_is_part_of_the_hash()
        {
            var config = Config();
            ulong baseline = new Simulator(config, State(config)).StateHash();

            void Differs(Action<SimState> change)
            {
                SimState state = State(config);
                change(state);
                Assert.NotEqual(baseline, new Simulator(config, state).StateHash());
            }

            Differs(s => s.Tick = 1);
            Differs(s => s.Seed = 1);
            Differs(s => s.Ball.Id = 1);
            Differs(s => s.Ball.Position = new Vec3(0, 20, 1));
            Differs(s => s.Ball.Velocity = new Vec3(0, 0, 1));
            Differs(s => s.Ball.Spin = new Vec3(1, 0, 0));
            Differs(s => s.Ball.BeyondGoalLine = true);
            Differs(s => s.Dots[0].Id = 2);
            Differs(s => s.Dots[0].OwnerControllerId = 2);
            Differs(s => s.Dots[0].Position = new Vec2(0, 1));
            Differs(s => s.Dots[0].Velocity = new Vec2(1, 0));
            Differs(s => s.Dots[0].Heading = 1);
            Differs(s => s.Dots[0].KickCooldown = 1);
            Differs(s => s.Dots = new DotState[0]);
        }
    }
}
