using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #97: shielding. A carrier moving slowly with an opponent near keeps the ball on his far side;
    /// a challenge through him is a contest of strength and tackling, drawn from the counter-based
    /// stream, not a sure thing either way.
    /// </summary>
    public class ShieldTests
    {
        private const int A = 1, B = 2;

        private static SimState World(SimConfig c, Vec2 ball, Vec2 b, ulong seed = 3, double aStrength = 0.6, double bStrength = 0.6)
        {
            return new SimState
            {
                Seed = seed,
                Ball = new BallState { Id = 100, Position = new Vec3(ball.X, ball.Y, c.BallRadius), LastPlayedBy = A, AttachedTo = c.ControlModel == ControlModel.Attached ? A : 0 },
                Players = new[]
                {
                    new PlayerState { Id = A, OwnerControllerId = A, TeamId = 1, Capabilities = PlayerCapabilities.Reference.With("strength", aStrength) },
                    new PlayerState { Id = B, OwnerControllerId = B, TeamId = 2, Capabilities = PlayerCapabilities.Reference.With("strength", bStrength), Position = b, Heading = Math.PI },
                },
            };
        }

        private static List<SimEvent> Run(Simulator sim, int ticks, Func<SimState, Vec2> b)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                SimState s = sim.PeekState();
                sim.Step(new[] { new Command(A, s.Tick, Vec2.Zero), new Command(B, s.Tick, b(s)) }, events);
            }

            return events;
        }

        public static readonly TheoryData<ControlModel> Models = new TheoryData<ControlModel> { ControlModel.Free, ControlModel.Attached };

        [Theory]
        [MemberData(nameof(Models))]
        public void A_slow_carrier_with_an_opponent_near_keeps_the_ball_on_his_far_side(ControlModel model)
        {
            SimConfig c = SpikeScenario.Config().WithControlModel(model);
            var sim = new Simulator(c, World(c, new Vec2(0.6, 0), new Vec2(1.9, 0)));
            Run(sim, c.TickRate, _ => Vec2.Zero);
            SimState s = sim.PeekState();
            Vec2 toBall = s.Ball.Position.Horizontal - s.Players[0].Position;
            Vec2 toOpponent = s.Players[1].Position - s.Players[0].Position;
            Assert.True(toBall.X * toOpponent.X + toBall.Y * toOpponent.Y < 0, $"ball at {toBall.X:F2},{toBall.Y:F2} from him, the opponent at {toOpponent.X:F2},{toOpponent.Y:F2}");
            Assert.True(Math.Sqrt(toBall.LengthSquared) <= c.ControlReach, "still his");
        }

        // B comes at A, who shields the ball behind him, through him: is the ball won?
        private static double WonShare(ControlModel model, double aStrength, double bStrength)
        {
            SimConfig c = SpikeScenario.Config().WithControlModel(model);
            int won = 0, seeds = 60;
            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                var sim = new Simulator(c, World(c, new Vec2(-0.45, 0), new Vec2(1.2, 0), seed, aStrength, bStrength));
                List<SimEvent> events = Run(sim, c.TickRate, s =>
                {
                    Vec2 to = s.Ball.Position.Horizontal - s.Players[1].Position;
                    return to * (1 / Math.Sqrt(to.LengthSquared));
                });
                if (events.Any(e => e.Kind == SimEventKind.ChallengeWon && e.Subject == B)) won++;
            }

            return (double)won / seeds;
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void A_challenge_through_a_shielding_carrier_is_a_contest_of_strength(ControlModel model)
        {
            double even = WonShare(model, 0.6, 0.6);
            Assert.InRange(even, 0.1, 0.9);
            double strong = WonShare(model, 0.2, 1.0), weak = WonShare(model, 1.0, 0.2);
            Assert.True(strong > weak + 0.2, $"a stronger challenger wins {strong:P0}, a weaker {weak:P0}");
        }
    }
}
