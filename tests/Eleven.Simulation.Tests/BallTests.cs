using System;
using System.Collections.Generic;
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

        // #33 (SOL's research answer on #24): curve shapes the ball in the air; a rolling ball
        // keeps its line. Its spin still decays, for later bounce, presentation and aftertouch work.
        [Fact]
        public void A_rolling_ball_with_sidespin_keeps_its_line()
        {
            var config = Config();
            var spun = Simulator(config, State(config, ballPosition: new Vec3(0, 0, config.BallRadius), ballVelocity: new Vec3(10, 0, 0), ballSpin: new Vec3(0, 0, 20)));
            for (int t = 0; t < 60; t++) Step(spun);

            BallState ball = spun.PeekState().Ball;
            Assert.Equal(0, ball.Position.Y);
            Assert.Equal(0, ball.Velocity.Y);
            Assert.True(ball.Position.X > 5);
            Assert.InRange(ball.Spin.Z, 0.001, 19.999);
        }

        // ---- Bounce spin (#40) ------------------------------------------------------------------------

        // Falls from 2 m with spin, with air and ground spin decay off so only the bounce changes spin.
        // Returns the spin just before the first bounce's step, after it, and over the next steps.
        private static (Vec3 Before, Vec3 After, List<Vec3> Later) BounceSpin(SimConfig config)
        {
            var sim = Simulator(config, State(config, ballPosition: new Vec3(0, 0, 2), ballSpin: new Vec3(3, -4, 20)));
            for (int t = 0; t < 600; t++)
            {
                Vec3 before = sim.PeekState().Ball.Spin;
                if (!Step(sim).Any(e => e.Kind == SimEventKind.Bounced)) continue;

                Vec3 after = sim.PeekState().Ball.Spin;
                var later = new List<Vec3>();
                for (int k = 0; k < 10; k++)
                {
                    if (Step(sim).Any(e => e.Kind == SimEventKind.Bounced)) break;
                    later.Add(sim.PeekState().Ball.Spin);
                }

                return (before, after, later);
            }

            throw new InvalidOperationException("The ball never bounced.");
        }

        private static SimConfig NoSpinDecay(double retention) =>
            Config().With("airSpinDecay", 0).With("groundSpinDecay", 0).With("bounceSpinRetention", retention);

        [Fact]
        public void A_bounce_keeps_the_configured_fraction_of_spin_exactly_once()
        {
            SimConfig config = NoSpinDecay(0.25);
            (Vec3 before, Vec3 after, List<Vec3> later) = BounceSpin(config);

            Assert.Equal(before * 0.25, after);
            Assert.NotEmpty(later);
            Assert.All(later, spin => Assert.Equal(after, spin));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(0.0)]
        public void Retention_one_keeps_spin_through_a_bounce_and_zero_strips_it(double retention)
        {
            (Vec3 before, Vec3 after, _) = BounceSpin(NoSpinDecay(retention));
            Assert.Equal(before * retention, after);
            Assert.Equal(retention == 0, after.X == 0 && after.Y == 0 && after.Z == 0);
        }

        [Fact]
        public void The_technical_reference_keeps_a_quarter_inside_the_research_starting_range()
        {
            TunableParameter p = SimConfig.Tunables.Single(t => t.Name == "bounceSpinRetention");
            Assert.Equal((0.0, 1.0, 0.15, 0.35), (p.Min, p.Max, p.StartMin!.Value, p.StartMax!.Value));
            Assert.Equal(0.25, Config().BounceSpinRetention);
        }

        [Fact]
        public void Curve_in_the_air_before_the_first_bounce_is_unchanged()
        {
            SimConfig kept = Config().With("bounceSpinRetention", 1), cut = Config();
            Simulator a = Simulator(kept, State(kept, ballPosition: new Vec3(0, 0, kept.BallRadius), ballVelocity: new Vec3(18, 0, 7), ballSpin: new Vec3(0, 0, 12)));
            Simulator b = Simulator(cut, State(cut, ballPosition: new Vec3(0, 0, cut.BallRadius), ballVelocity: new Vec3(18, 0, 7), ballSpin: new Vec3(0, 0, 12)));
            int flown = 0;
            for (; flown < 600; flown++)
            {
                bool bounced = Step(a).Any(e => e.Kind == SimEventKind.Bounced);
                Step(b);
                if (bounced) break;
                Assert.Equal(a.PeekState().Ball.Position, b.PeekState().Ball.Position);
                Assert.Equal(a.PeekState().Ball.Velocity, b.PeekState().Ball.Velocity);
                Assert.Equal(a.PeekState().Ball.Spin, b.PeekState().Ball.Spin);
            }

            Assert.True(flown > 30);
            Assert.NotEqual(a.PeekState().Ball.Spin, b.PeekState().Ball.Spin);
        }

        // #40: on a lively, slick pitch the bending cross used to keep curving on every hop.
        [Fact]
        public void Repeated_hops_no_longer_carry_full_strength_curve()
        {
            SimConfig lively = Config().With(new[]
            {
                new KeyValuePair<string, double>("rollingFriction", 0.05),
                new KeyValuePair<string, double>("bounceRestitution", 0.75),
                new KeyValuePair<string, double>("bounceTangentialRetention", 0.85),
                new KeyValuePair<string, double>("magnusCoefficient", 0.008),
            });

            double After(SimConfig c) => Harness.BallCurves.Kick(c, "action", 20, 20, 40).CurveAfterLandingMetres;
            double kept = After(lively.With("bounceSpinRetention", 1));
            double cut = After(lively);

            Assert.True(kept > 5, $"kept {kept}");
            Assert.True(Math.Abs(cut) < 0.3 * Math.Abs(kept), $"cut {cut}, kept {kept}");
        }
    }
}
