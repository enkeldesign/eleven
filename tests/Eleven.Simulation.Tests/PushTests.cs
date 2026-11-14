using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Push-and-chase under FREE (#63, an experiment): with pushSpeed on, a sprint's touch sends the
    /// ball ahead faster than the footballer. It runs beyond their reach, slows under its own rolling
    /// friction, and they catch it up by running. These pin relationships, not untuned feel values.
    /// </summary>
    public class PushTests
    {
        private const int A = TestWorld.Controller;

        private static SimConfig Free(double pushSpeed, double pushFromSpeed = 0.9) =>
            new SimConfig(controlModel: ControlModel.Free, pushSpeed: pushSpeed, pushFromSpeed: pushFromSpeed);

        private static SimState AtFeet(SimConfig config, double ballControl = 1)
        {
            SimState state = TestWorld.State(config, new Vec3(0.6, 0, config.BallRadius));
            state.Players[0].Capabilities = PlayerCapabilities.Reference.With("ballControl", ballControl);
            return state;
        }

        private sealed class Run
        {
            public readonly List<SimState> After = new List<SimState>();
            public readonly List<List<SimEvent>> Events = new List<List<SimEvent>>();

            public int Count(SimEventKind kind) => Events.Sum(e => e.Count(x => x.Kind == kind));

            public List<int> TicksOf(SimEventKind kind) => Enumerable.Range(0, Events.Count).Where(i => Events[i].Any(x => x.Kind == kind)).ToList();
        }

        private static Run Play(SimConfig config, SimState start, Func<long, Command> command, double seconds)
        {
            var run = new Run();
            var sim = new Simulator(config, start);
            long ticks = (long)(seconds * config.TickRate);
            for (long t = 0; t < ticks; t++)
            {
                var events = new List<SimEvent>();
                sim.Step(new[] { command(sim.Tick) }, events);
                run.Events.Add(events);
                run.After.Add(sim.PeekState().Clone());
            }

            return run;
        }

        private static Func<long, Command> Sprint => tick => new Command(A, tick, new Vec2(1, 0));

        private static double Separation(SimState s) => Math.Sqrt((s.Ball.Position.Horizontal - s.Players[0].Position).LengthSquared);

        [Fact]
        public void With_the_push_off_free_is_unchanged()
        {
            // pushSpeed 0 is FREE's short touches: the same states, step for step, as a config that
            // never heard of the push, and no Pushed event.
            SimConfig off = Free(0);
            Run run = Play(off, AtFeet(off), Sprint, 4);
            Assert.Equal(0, run.Count(SimEventKind.Pushed));
            Assert.True(run.After.All(s => Separation(s) <= off.ControlReach + 0.15), "the ball left reach with the push off");
            Assert.True(run.Count(SimEventKind.Touched) >= 10);
        }

        [Fact]
        public void A_sprint_touch_pushes_the_ball_beyond_reach_and_the_footballer_catches_it_by_running()
        {
            SimConfig config = Free(1.0);
            Run run = Play(config, AtFeet(config), Sprint, 10);

            List<int> pushes = run.TicksOf(SimEventKind.Pushed);
            Assert.True(pushes.Count >= 2, $"{pushes.Count} push(es) in 10 s");

            // Between two pushes the ball is beyond ordinary reach, and nothing touches it there.
            int first = pushes[0], second = pushes[1];
            double peak = Enumerable.Range(first, second - first).Max(i => Separation(run.After[i]));
            Assert.True(peak > config.ControlReach + 0.2, $"peak separation {peak:F2} m, reach {config.ControlReach}");
            Assert.Equal(0, Enumerable.Range(first + 1, second - first - 1).Sum(i => run.Events[i].Count(e => e.Kind == SimEventKind.Touched)));

            // The catch is a chase: the footballer never exceeds their max speed, and the ball is
            // only ever slowed by its own friction between touches.
            double max = run.After[0].Players[0].Capabilities.MaxSpeed;
            Assert.True(run.After.All(s => Math.Sqrt(s.Players[0].Velocity.LengthSquared) <= max + 1e-9), "the footballer outran their max speed");
            for (int i = first + 1; i < second; i++)
            {
                double before = Math.Sqrt(run.After[i - 1].Ball.Velocity.Horizontal.LengthSquared), after = Math.Sqrt(run.After[i].Ball.Velocity.Horizontal.LengthSquared);
                Assert.True(after <= before + 1e-9, $"the ball sped up at step {i} with nobody touching it");
            }

            // And the next touch finds it back within reach: no snap, the same independent ball.
            Assert.True(Separation(run.After[second - 1]) <= config.ControlReach + 0.3, "the next push came from beyond reach");
        }

        [Fact]
        public void A_harder_push_runs_further_ahead_and_takes_longer_to_catch()
        {
            (double Peak, double Cycle) Measure(double pushSpeed)
            {
                SimConfig config = Free(pushSpeed);
                Run run = Play(config, AtFeet(config), Sprint, 20);
                List<int> pushes = run.TicksOf(SimEventKind.Pushed);
                Assert.True(pushes.Count >= 2, $"pushSpeed {pushSpeed}: {pushes.Count} push(es) in 20 s");
                double peak = Enumerable.Range(pushes[0], pushes[1] - pushes[0]).Max(i => Separation(run.After[i]));
                return (peak, (pushes[1] - pushes[0]) / (double)config.TickRate);
            }

            (double peakSoft, double cycleSoft) = Measure(0.75);
            (double peakHard, double cycleHard) = Measure(1.5);
            Assert.True(peakHard > peakSoft + 0.3, $"peaks {peakSoft:F2} and {peakHard:F2} m");
            Assert.True(cycleHard > cycleSoft + 0.5, $"cycles {cycleSoft:F2} and {cycleHard:F2} s");
        }

        [Fact]
        public void Only_a_sprint_pushes()
        {
            // At a jog, under the push's speed threshold, touches stay short and the ball in reach.
            SimConfig config = Free(1.0);
            Run jog = Play(config, AtFeet(config), tick => new Command(A, tick, new Vec2(0.5, 0)), 5);
            Assert.Equal(0, jog.Count(SimEventKind.Pushed));
            Assert.True(jog.After.All(s => Separation(s) <= config.ControlReach + 0.15));
        }

        [Fact]
        public void A_kick_connects_just_after_a_push_and_misses_in_the_middle_of_the_chase()
        {
            // The kick's own reach is unchanged. So whether a kick connects depends on when in the
            // chase it is asked for: the ball must be within kick reach when the wind-up ends.
            SimConfig config = Free(1.5);
            Run chase = Play(config, AtFeet(config), Sprint, 20);
            List<int> pushes = chase.TicksOf(SimEventKind.Pushed);
            Assert.True(pushes.Count >= 2);
            int cycle = pushes[1] - pushes[0];

            bool Connects(int ticksAfterPush)
            {
                long kickAt = pushes[0] + ticksAfterPush;
                Run run = Play(config, AtFeet(config), tick => new Command(A, tick, new Vec2(1, 0), tick == kickAt ? KickIntent.AlongHeading(0.6, 0) : (KickIntent?)null), (pushes[0] + cycle) / (double)config.TickRate + 1);
                return run.Count(SimEventKind.Kicked) > 0;
            }

            Assert.True(Connects(1), "a kick asked for right after the push missed");
            Assert.False(Connects(cycle / 2), "a kick asked for in the middle of the chase connected from beyond reach");
        }

        [Fact]
        public void An_overpush_is_lost()
        {
            // Pushed much faster than the footballer runs, the ball isn't caught within the time a
            // moderate push takes many times over.
            SimConfig config = Free(4);
            Run run = Play(config, AtFeet(config), Sprint, 8);
            List<int> pushes = run.TicksOf(SimEventKind.Pushed);
            Assert.Single(pushes);
            Assert.True(Separation(run.After[run.After.Count - 1]) > config.ControlReach + 2, "the overpushed ball was back within reach inside 8 s");
        }

        [Fact]
        public void A_poor_footballer_pushes_less_truly_than_a_good_one()
        {
            // The push is a touch: its direction error grows as ball control falls. Over the same
            // seeds, a poor footballer's pushes leave their line by more.
            double Drift(double ballControl)
            {
                double worst = 0;
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    SimConfig config = Free(1.5);
                    SimState start = AtFeet(config, ballControl);
                    start.Seed = seed;
                    Run run = Play(config, start, Sprint, 2);
                    worst = Math.Max(worst, run.After.Max(s => Math.Abs(s.Ball.Position.Y - s.Players[0].Position.Y)));
                }

                return worst;
            }

            double good = Drift(0.9), poor = Drift(0.3);
            Assert.True(poor > good * 1.5 && poor > 0.1, $"sideways drift: good {good:F3} m, poor {poor:F3} m");
        }

        [Fact]
        public void The_push_replays_exactly()
        {
            SimConfig config = Free(1.5);
            Run a = Play(config, AtFeet(config, 0.5), Sprint, 6), b = Play(config, AtFeet(config, 0.5), Sprint, 6);
            Assert.Equal(a.After.Select(s => s.Ball.Position), b.After.Select(s => s.Ball.Position));
            Assert.Equal(a.TicksOf(SimEventKind.Pushed), b.TicksOf(SimEventKind.Pushed));
        }
    }
}
