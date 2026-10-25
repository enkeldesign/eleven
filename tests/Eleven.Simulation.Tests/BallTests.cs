using System;
using System.Linq;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>Ball behaviour, stated as properties rather than exact positions.</summary>
    public class BallTests
    {
        [Fact]
        public void An_airborne_ball_falls_under_gravity()
        {
            var config = Config();
            var sim = Simulator(config, State(config, ballPosition: new Vec3(0, 0, 10)));
            Step(sim);

            Assert.Equal(-config.Gravity * config.TickSeconds, sim.PeekState().Ball.Velocity.Z, 12);
        }

        [Fact]
        public void A_bounce_returns_less_upward_speed_than_it_arrived_with()
        {
            var config = Config();
            var sim = Simulator(config, State(config, ballPosition: new Vec3(0, 0, 2), ballVelocity: new Vec3(3, 0, 0)));
            double before = 0;
            for (int t = 0; t < 600; t++)
            {
                before = sim.PeekState().Ball.Velocity.Z;
                if (Step(sim).Any(e => e.Kind == SimEventKind.Bounced)) break;
            }

            BallState ball = sim.PeekState().Ball;
            Assert.True(before < 0);
            Assert.InRange(ball.Velocity.Z, 0, -before);
            Assert.Equal(config.BallRadius, ball.Position.Z);
            Assert.True(ball.Velocity.X < 3);
        }

        [Fact]
        public void Bouncing_settles_into_rolling_and_rolling_friction_stops_the_ball()
        {
            var config = Config();
            var sim = Simulator(config, State(config, ballPosition: new Vec3(0, 0, 3), ballVelocity: new Vec3(6, 0, 0)));
            double previousSpeed = double.MaxValue;
            bool rolled = false;
            for (int t = 0; t < 60 * 30; t++)
            {
                Step(sim);
                BallState ball = sim.PeekState().Ball;
                bool grounded = ball.Position.Z == config.BallRadius && ball.Velocity.Z == 0;
                if (!grounded) continue;

                rolled = true;
                double speed = ball.Velocity.X;
                Assert.True(speed < previousSpeed || speed == 0, $"rolling speed rose at tick {t}");
                previousSpeed = speed;
            }

            Assert.True(rolled);
            Assert.Equal(0, sim.PeekState().Ball.Velocity.X);
        }

        [Fact]
        public void Sidespin_curves_the_ball_towards_spin_cross_velocity()
        {
            var config = Config();
            var spun = Simulator(config, State(config, ballPosition: new Vec3(0, 0, 5), ballVelocity: new Vec3(20, 0, 0), ballSpin: new Vec3(0, 0, 10)));
            var plain = Simulator(config, State(config, ballPosition: new Vec3(0, 0, 5), ballVelocity: new Vec3(20, 0, 0)));
            for (int t = 0; t < 40; t++)
            {
                Step(spun);
                Step(plain);
            }

            // ω = +z, v = +x, so ω × v points along +y.
            Assert.True(spun.PeekState().Ball.Position.Y > 0.1);
            Assert.Equal(0, plain.PeekState().Ball.Position.Y);
        }

        [Fact]
        public void A_kick_launches_the_ball_along_the_heading_with_loft_and_spin()
        {
            var config = Config();
            var sim = Simulator(config, State(config, ballPosition: new Vec3(0.5, 0, config.BallRadius)));
            var events = Step(sim, Move(0, 1, 0, kick: true));

            BallState ball = sim.PeekState().Ball;
            Assert.Contains(new SimEvent(0, SimEventKind.Kicked, DotId), events);
            Assert.True(ball.Velocity.Z > 0);
            Assert.True(ball.Spin.Z > 0);
            double heading = Math.Atan2(ball.Velocity.Y, ball.Velocity.X);
            Assert.InRange(heading, -config.KickDirectionError, config.KickDirectionError);
            Assert.Equal(config.KickCooldownTicks, sim.PeekState().Dots[0].KickCooldown);
        }

        [Fact]
        public void A_ball_out_of_reach_is_not_kicked()
        {
            var config = Config();
            var sim = Simulator(config, State(config, ballPosition: new Vec3(config.KickReach + 0.5, 0, config.BallRadius)));
            var events = Step(sim, Move(0, 0, 0, kick: true));

            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Kicked);
            Assert.Equal(0, sim.PeekState().Ball.Velocity.X);
        }

        [Fact]
        public void A_dot_cooling_down_cannot_kick()
        {
            var config = Config();
            SimState state = State(config, ballPosition: new Vec3(0.5, 0, config.BallRadius));
            state.Dots[0].KickCooldown = 2;
            var sim = Simulator(config, state);

            Assert.DoesNotContain(Step(sim, Move(0, 0, 0, kick: true)), e => e.Kind == SimEventKind.Kicked);
            Assert.Contains(Step(sim, Move(1, 0, 0, kick: true)), e => e.Kind == SimEventKind.Kicked);
        }
    }
}
