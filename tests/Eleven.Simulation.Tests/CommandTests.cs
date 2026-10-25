using System;
using System.Linq;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>INV-10: owned commands, applied at their tick.</summary>
    public class CommandTests
    {
        [Fact]
        public void A_command_takes_effect_at_its_tick_and_not_before()
        {
            var sim = Simulator();
            for (long t = 0; t < 5; t++)
            {
                Step(sim);
                Assert.Equal(0, sim.PeekState().Dots[0].Velocity.X);
            }

            Step(sim, Move(5, 1, 0));
            Assert.True(sim.PeekState().Dots[0].Velocity.X > 0);
        }

        [Fact]
        public void An_unowned_command_cannot_control_the_dot()
        {
            var sim = Simulator();
            var events = Step(sim, Move(0, 1, 0, kick: true, controller: Controller + 1));

            Assert.Equal(Vec2.Zero.X, sim.PeekState().Dots[0].Velocity.X);
            Assert.Equal(Vec2.Zero.Y, sim.PeekState().Dots[0].Velocity.Y);
            Assert.Equal(new SimEvent(0, SimEventKind.CommandRejected, Controller + 1, CommandRejection.Unowned), Assert.Single(events));
        }

        [Fact]
        public void Stale_and_early_commands_are_rejected()
        {
            var sim = Simulator();
            Step(sim);
            Step(sim);

            var events = Step(sim, Move(1, 1, 0), Move(3, 1, 0));

            Assert.Equal(0, sim.PeekState().Dots[0].Velocity.X);
            Assert.Equal(
                new[] { CommandRejection.Stale, CommandRejection.Early },
                events.Select(e => e.Rejection).ToArray());
        }

        [Fact]
        public void Duplicate_commands_for_one_controller_are_all_rejected()
        {
            var sim = Simulator();
            var events = Step(sim, Move(0, 1, 0), Move(0, -1, 0));

            Assert.Equal(0, sim.PeekState().Dots[0].Velocity.X);
            Assert.Equal(2, events.Count(e => e.Rejection == CommandRejection.Duplicate));
        }

        [Fact]
        public void Non_finite_moves_are_rejected()
        {
            var sim = Simulator();
            var events = Step(sim, Move(0, double.NaN, 0));

            Assert.Equal(CommandRejection.Invalid, Assert.Single(events).Rejection);
        }

        [Fact]
        public void Submission_order_does_not_matter()
        {
            Command[] commands = { Move(0, 0.3, 0.4), Move(0, 1, 0, controller: 3), Move(0, 1, 1, controller: 9) };

            var a = Simulator();
            var b = Simulator();
            var eventsA = Step(a, commands);
            var eventsB = Step(b, commands.Reverse().ToArray());

            Assert.Equal(a.StateHash(), b.StateHash());
            Assert.Equal(eventsA.OrderBy(e => e.Subject), eventsB.OrderBy(e => e.Subject));
        }

        [Fact]
        public void Move_vectors_longer_than_one_are_clamped_to_full_stick()
        {
            var config = Config();
            var full = Simulator(config);
            var over = Simulator(config);
            var diagonal = Simulator(config);
            for (long t = 0; t < 60; t++)
            {
                Step(full, Move(t, 0, 1));
                Step(over, Move(t, 0, 3));
                Step(diagonal, Move(t, 30, 40));
            }

            Assert.Equal(full.StateHash(), over.StateHash());
            Vec2 v = diagonal.PeekState().Dots[0].Velocity;
            Assert.Equal(config.DotMaxSpeed, Math.Sqrt(v.LengthSquared), 9);
        }
    }
}
