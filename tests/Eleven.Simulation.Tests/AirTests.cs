using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Eleven.Simulation.Harness;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #94: headers and volleys. The ball's height at contact decides what a kick is: on the ground
    /// a kick, up to volley height a volley, in the head's band a header, and between the bands or
    /// above them a miss. A volley errs more the faster the ball comes; a header is slower than a
    /// kick and errs by the player's heading.
    /// </summary>
    public class AirTests
    {
        private static readonly SimConfig C = Config();

        // A player at the origin facing +x, one step from the end of a wind-up along +x, with the
        // ball where given: the next step connects or misses.
        private static Simulator AtContact(Vec3 ball, Vec3 velocity = default, double kickError = 0, double heading = 0.6,
            double strength = 1, double loft = 0, double line = 0, ulong seed = 1)
        {
            SimState s = State(C, ballPosition: ball, ballVelocity: velocity);
            s.Seed = seed;
            PlayerCapabilities caps = PlayerCapabilities.Reference;
            caps.KickError = kickError;
            caps.Heading = heading;
            s.Players[0].Capabilities = caps;
            s.Players[0].WindUpTicks = 1;
            s.Players[0].WindUpDirection = line;
            s.Players[0].WindUpStrength = strength;
            s.Players[0].WindUpLoft = loft;
            return Simulator(C, s);
        }

        private static (List<SimEvent> Events, BallState Ball) Contact(Simulator sim)
        {
            List<SimEvent> events = Step(sim);
            return (events, sim.PeekState().Ball);
        }

        private static double Speed(Vec3 v) => Math.Sqrt(v.LengthSquared);

        private static double Off(Vec3 v, double line)
        {
            double d = Math.Atan2(v.Y, v.X) - line;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            return Math.Abs(d);
        }

        [Fact]
        public void A_ball_in_the_heads_band_is_headed_slower_than_it_would_be_kicked()
        {
            (List<SimEvent> e, BallState ball) = Contact(AtContact(new Vec3(0.4, 0, 1.8)));
            Assert.Contains(e, x => x.Kind == SimEventKind.Kicked);
            Assert.Contains(e, x => x.Kind == SimEventKind.Headed);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.Volleyed);

            (List<SimEvent> g, BallState kicked) = Contact(AtContact(new Vec3(0.4, 0, C.BallRadius)));
            Assert.Contains(g, x => x.Kind == SimEventKind.Kicked);
            Assert.DoesNotContain(g, x => x.Kind == SimEventKind.Headed || x.Kind == SimEventKind.Volleyed);
            Assert.True(Speed(ball.Velocity) < Speed(kicked.Velocity), $"header {Speed(ball.Velocity)} m/s, kick {Speed(kicked.Velocity)} m/s");
        }

        [Fact]
        public void A_better_header_of_the_ball_heads_it_harder()
        {
            double weak = Speed(Contact(AtContact(new Vec3(0.4, 0, 1.8), heading: 0)).Ball.Velocity);
            double strong = Speed(Contact(AtContact(new Vec3(0.4, 0, 1.8), heading: 1)).Ball.Velocity);
            Assert.True(strong > weak, $"heading 1: {strong} m/s, heading 0: {weak} m/s");
        }

        [Fact]
        public void A_ball_below_the_heads_band_and_above_the_feet_is_volleyed()
        {
            (List<SimEvent> e, BallState _) = Contact(AtContact(new Vec3(0.4, 0, 0.8), new Vec3(-8, 0, 0)));
            Assert.Contains(e, x => x.Kind == SimEventKind.Kicked);
            Assert.Contains(e, x => x.Kind == SimEventKind.Volleyed);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.Headed);
        }

        [Theory]
        [InlineData(1.25)] // between the volley and the head
        [InlineData(2.7)] // over the head
        public void A_ball_no_part_of_the_body_can_play_is_missed(double z)
        {
            (List<SimEvent> e, BallState _) = Contact(AtContact(new Vec3(0.4, 0, z)));
            Assert.Contains(e, x => x.Kind == SimEventKind.KickMissed);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.Kicked);
        }

        [Fact]
        public void A_header_reaches_less_far_than_a_foot()
        {
            double between = (C.HeaderReach + C.KickReach) / 2;
            Assert.Contains(Contact(AtContact(new Vec3(between, 0, 1.8))).Events, x => x.Kind == SimEventKind.KickMissed);
            Assert.Contains(Contact(AtContact(new Vec3(between, 0, C.BallRadius))).Events, x => x.Kind == SimEventKind.Kicked);
        }

        [Fact]
        public void A_volley_errs_more_the_faster_the_ball_comes_and_stays_within_its_bound()
        {
            double Spread(double incoming)
            {
                double most = 0;
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    BallState b = Contact(AtContact(new Vec3(0.4, 0, 0.8), new Vec3(-incoming, 0, 0), seed: seed)).Ball;
                    double off = Off(b.Velocity, 0);
                    Assert.True(off <= C.VolleyError * incoming + 1e-9, $"{off} rad off at {incoming} m/s");
                    most = Math.Max(most, off);
                }

                return most;
            }

            Assert.True(Spread(16) > Spread(2), "a faster ball volleys wider");
        }

        [Fact]
        public void A_header_errs_by_the_players_heading_within_its_bound()
        {
            double Spread(double heading)
            {
                double most = 0;
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    BallState b = Contact(AtContact(new Vec3(0.4, 0, 1.8), heading: heading, seed: seed)).Ball;
                    double off = Off(b.Velocity, 0);
                    Assert.True(off <= C.HeaderError * (1 - heading) + 1e-9, $"{off} rad off at heading {heading}");
                    most = Math.Max(most, off);
                }

                return most;
            }

            Assert.Equal(0, Spread(1), 12);
            Assert.True(Spread(0) > 0.05);
        }

        // A ball lofted at a CPU outfielder standing at the centre spot, from 14 m up the pitch:
        // the events of the next two seconds, the brains running.
        private static List<SimEvent> LoftedAt(double speed, double lift)
        {
            SimConfig config = SpikeScenario.Config();
            SimState s = OneVOne.Match(config, seed: 9, bothCpu: true);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.AwardedTeam = 0;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            s.Players[0].Position = Vec2.Zero;
            s.Players[2].Position = new Vec2(30, 25);
            s.Ball.Position = new Vec3(14, 0, config.BallRadius);
            s.Ball.Velocity = new Vec3(-speed, 0, lift);
            var sim = new Simulator(config, s);
            var events = new List<SimEvent>();
            for (int i = 0; i < 2 * 60; i++)
            {
                var commands = new List<Command>();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                sim.Step(commands, events);
            }

            return events;
        }

        [Fact]
        public void A_cpu_outfielder_heads_and_volleys_a_ball_that_comes_to_him_in_the_air()
        {
            int headed = 0, volleyed = 0;
            for (double speed = 10; speed <= 16; speed += 2)
            {
                for (double lift = 2; lift <= 8; lift += 0.5)
                {
                    List<SimEvent> e = LoftedAt(speed, lift);
                    headed += e.Count(x => x.Kind == SimEventKind.Headed && x.Subject == OneVOne.HomePlayer);
                    volleyed += e.Count(x => x.Kind == SimEventKind.Volleyed && x.Subject == OneVOne.HomePlayer);
                }
            }

            Assert.True(headed > 0, "he never headed it");
            Assert.True(volleyed > 0, "he never volleyed it");
        }

        [Fact]
        public void A_kick_on_the_ground_is_what_it_was()
        {
            // No header or volley error for a ball at the feet, however fast it comes.
            for (ulong seed = 1; seed <= 20; seed++)
            {
                BallState b = Contact(AtContact(new Vec3(0.4, 0, C.BallRadius), new Vec3(-12, 0, 0), seed: seed)).Ball;
                Assert.Equal(0, Off(b.Velocity, 0), 12);
            }
        }
    }
}
