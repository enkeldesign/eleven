using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #98: the slide tackle. A kick intent without the ball, near an opponent's ball, is a slide
    /// along its line: committed, carrying the footballer at slide speed and slowing, then on the
    /// ground. Meeting the ball first wins it; meeting the man first is a foul (#99 adjudicates).
    /// </summary>
    public class SlideTests
    {
        private const int A = 1, B = 2;

        // The slide's own mechanics, shielding off (#97).
        private static readonly SimConfig C = SpikeScenario.Config().With("shieldSpeed", 0);

        // A has the ball, at `ball`, moving at `velocity`; B is where given.
        private static SimState World(Vec2 ball, Vec2 b, Vec2 velocity = default)
        {
            return new SimState
            {
                Seed = 5,
                Ball = new BallState { Id = 100, Position = new Vec3(ball.X, ball.Y, C.BallRadius), Velocity = new Vec3(velocity.X, velocity.Y, 0), LastPlayedBy = A },
                Players = new[]
                {
                    new PlayerState { Id = A, OwnerControllerId = A, TeamId = 1, Capabilities = PlayerCapabilities.Reference, Velocity = velocity },
                    new PlayerState { Id = B, OwnerControllerId = B, TeamId = 2, Capabilities = PlayerCapabilities.Reference, Position = b },
                },
            };
        }

        // B asks for a kick along `line` on tick 0; A runs on with the stick `a`.
        private static (List<SimEvent> Events, List<SimState> States) Run(SimState s, Vec2 line, Vec2 a, int ticks = 120)
        {
            var sim = new Simulator(C, s);
            var events = new List<SimEvent>();
            var states = new List<SimState>();
            for (int t = 0; t < ticks; t++)
            {
                Command b = t == 0 ? new Command(B, t, Vec2.Zero, new KickIntent(line, 1, 0)) : new Command(B, t, Vec2.Zero);
                sim.Step(new[] { new Command(A, t, a), b }, events);
                states.Add(sim.PeekState().Clone());
            }

            return (events, states);
        }

        [Fact]
        public void A_kick_intent_without_the_ball_near_an_opponents_ball_slides_along_its_line_and_ends_on_the_ground()
        {
            (List<SimEvent> e, List<SimState> states) = Run(World(new Vec2(0.6, 0), new Vec2(3.0, 1.2)), new Vec2(-1, -0.45), Vec2.Zero);
            Assert.Contains(e, x => x.Kind == SimEventKind.SlideStarted && x.Subject == B);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.KickMissed && x.Subject == B);

            // Along the line, fast, then slowing; then on the ground, still, for slideRecover.
            PlayerState first = states[0].Players[1];
            Assert.True(Math.Sqrt(first.Velocity.LengthSquared) > C.SlideSpeed * 0.8, $"{Math.Sqrt(first.Velocity.LengthSquared):F2} m/s");
            int down = states.FindIndex(s => s.Players[1].Challenge == ChallengePhase.Grounded);
            Assert.InRange(down, 1, C.SlideTicks + 2);
            Assert.All(states.Skip(down).Take(C.SlideRecoverTicks - 1), s => Assert.Equal(Vec2.Zero, s.Players[1].Velocity));
        }

        [Fact]
        public void Far_from_an_opponents_ball_the_same_intent_is_a_kick_as_ever()
        {
            (List<SimEvent> e, _) = Run(World(new Vec2(0.6, 0), new Vec2(8, 0)), new Vec2(-1, 0), Vec2.Zero, 30);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.SlideStarted);
            Assert.Contains(e, x => x.Kind == SimEventKind.KickMissed && x.Subject == B);
        }

        [Fact]
        public void A_slide_that_meets_the_ball_first_wins_it()
        {
            // From in front and to the side, the line through the ball before the man.
            (List<SimEvent> e, _) = Run(World(new Vec2(0.6, 0), new Vec2(2.4, 1.4)), new Vec2(-1.8, -1.4), Vec2.Zero);
            Assert.Contains(e, x => x.Kind == SimEventKind.SlideWon && x.Subject == B && x.Other == A);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.SlideFouled);
        }

        [Fact]
        public void A_slide_that_meets_the_man_first_is_a_foul()
        {
            // From behind a dribbler running on: the man is in the way of the ball.
            (List<SimEvent> e, _) = Run(World(new Vec2(0.7, 0), new Vec2(-1.2, 0), new Vec2(3, 0)), new Vec2(1, 0), new Vec2(0.45, 0));
            Assert.Contains(e, x => x.Kind == SimEventKind.SlideFouled && x.Subject == B && x.Other == A);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.SlideWon);
        }
    }
}
