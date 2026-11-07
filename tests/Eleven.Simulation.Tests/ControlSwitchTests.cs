using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The live FREE ↔ ATTACHED switch (#60): at a step boundary, the same state under the same
    /// config with only the model changed. No teleport, nothing lost or duplicated, nothing backdated.
    /// </summary>
    public class ControlSwitchTests
    {
        private const int A = TestWorld.Controller;

        private static readonly SimConfig Free = new SimConfig(controlModel: ControlModel.Free);

        private static SimState AtFeet(SimConfig config, Vec2? ballAt = null, Vec3? ballVelocity = null)
        {
            Vec2 at = ballAt ?? new Vec2(0.6, 0);
            return TestWorld.State(config, new Vec3(at.X, at.Y, config.BallRadius), ballVelocity);
        }

        // Steps `sim` for `ticks`, returning each step's events; `switchAt` maps a tick to the model
        // to switch to before stepping it.
        private static List<List<SimEvent>> Play(ref Simulator sim, Func<long, Command[]> commands, long ticks, Func<long, ControlModel?>? switchAt = null, List<ulong>? hashes = null)
        {
            var all = new List<List<SimEvent>>();
            for (long t = 0; t < ticks; t++)
            {
                ControlModel? to = switchAt?.Invoke(sim.Tick);
                if (to.HasValue) sim = ControlSwitch.To(sim, to.Value);
                var events = new List<SimEvent>();
                sim.Step(commands(sim.Tick), events);
                all.Add(events);
                hashes?.Add(sim.StateHash());
            }

            return all;
        }

        private static Func<long, Command[]> Stick(double x, double y) => tick => new[] { new Command(A, tick, new Vec2(x, y)) };

        [Fact]
        public void A_switch_keeps_the_state_and_its_hash_and_changes_only_the_model()
        {
            var sim = new Simulator(Free, AtFeet(Free));
            Play(ref sim, Stick(1, 0), 2 * Free.TickRate);

            Simulator switched = ControlSwitch.To(sim, ControlModel.Attached);
            Assert.Equal(sim.StateHash(), switched.StateHash());
            Assert.Equal(sim.Tick, switched.Tick);
            Assert.Equal(ControlModel.Attached, switched.Config.ControlModel);
            Assert.Equal(Free.WithControlModel(ControlModel.Attached).Hash, switched.Config.Hash);
        }

        [Fact]
        public void Across_a_switch_each_way_the_ball_moves_only_by_its_velocity()
        {
            foreach ((ControlModel from, ControlModel to) in new[] { (ControlModel.Free, ControlModel.Attached), (ControlModel.Attached, ControlModel.Free) })
            {
                SimConfig config = Free.WithControlModel(from);
                var sim = new Simulator(config, AtFeet(config));
                Play(ref sim, Stick(1, 0), 2 * config.TickRate);

                SimState before = sim.PeekState().Clone();
                sim = ControlSwitch.To(sim, to);
                sim.Step(Stick(1, 0)(sim.Tick), new List<SimEvent>());
                SimState after = sim.PeekState();

                Vec3 moved = after.Ball.Position - before.Ball.Position;
                double stepped = Math.Sqrt(after.Ball.Velocity.LengthSquared) * config.TickSeconds;
                Assert.True(Math.Sqrt(moved.LengthSquared) <= stepped + 1e-9, $"{from} → {to}: the ball moved {Math.Sqrt(moved.LengthSquared):F4} m on a {stepped:F4} m step");
            }
        }

        [Fact]
        public void Switching_to_attached_takes_a_ball_within_reach_on_the_next_eligible_step()
        {
            var sim = new Simulator(Free, AtFeet(Free));
            Play(ref sim, Stick(1, 0), 2 * Free.TickRate);
            Assert.True(Math.Sqrt((sim.PeekState().Ball.Position.Horizontal - sim.PeekState().Players[0].Position).LengthSquared) <= Free.ControlReach);

            sim = ControlSwitch.To(sim, ControlModel.Attached);
            List<List<SimEvent>> events = Play(ref sim, Stick(1, 0), 1);
            Assert.Contains(events[0], e => e.Kind == SimEventKind.Attached && e.Subject == 1);
            Assert.Equal(1, sim.PeekState().Ball.AttachedTo);
        }

        [Fact]
        public void Switching_to_attached_never_treats_a_distant_loose_ball_as_held()
        {
            var sim = new Simulator(Free, AtFeet(Free, ballAt: new Vec2(5, 0), ballVelocity: new Vec3(3, 0, 0)));
            sim = ControlSwitch.To(sim, ControlModel.Attached);
            SimState before = sim.PeekState().Clone();
            List<List<SimEvent>> events = Play(ref sim, Stick(0, 0), 1);

            Assert.DoesNotContain(events[0], e => e.Kind == SimEventKind.Attached);
            Assert.Equal(0, sim.PeekState().Ball.AttachedTo);

            // The ball rolls on as a free ball: the same step under FREE, where no one is near.
            var free = new Simulator(Free, before);
            free.Step(Stick(0, 0)(free.Tick), new List<SimEvent>());
            Assert.Equal(free.PeekState().Ball.Velocity, sim.PeekState().Ball.Velocity);
        }

        [Fact]
        public void Switching_to_free_ends_the_attachment_at_once()
        {
            SimConfig attached = Free.WithControlModel(ControlModel.Attached);
            var sim = new Simulator(attached, AtFeet(attached));
            Play(ref sim, Stick(1, 0), 2 * attached.TickRate);
            Assert.Equal(1, sim.PeekState().Ball.AttachedTo);

            sim = ControlSwitch.To(sim, ControlModel.Free);
            List<List<SimEvent>> events = Play(ref sim, Stick(1, 0), Free.TouchIntervalTicks + 1);
            Assert.Equal(0, sim.PeekState().Ball.AttachedTo);
            Assert.Contains(events.SelectMany(e => e), e => e.Kind == SimEventKind.Touched);
        }

        [Fact]
        public void A_kick_winding_up_across_a_switch_connects_once_at_the_same_step()
        {
            int kickAt = 2 * Free.TickRate;
            Func<long, Command[]> kick = tick => new[]
            {
                tick == kickAt ? new Command(A, tick, new Vec2(0.6, 0), KickIntent.AlongHeading(0.6, 0)) : new Command(A, tick, new Vec2(0.6, 0)),
            };

            var plain = new Simulator(Free, AtFeet(Free));
            List<List<SimEvent>> unswitched = Play(ref plain, kick, 3 * Free.TickRate);

            var sim = new Simulator(Free, AtFeet(Free));
            List<List<SimEvent>> switched = Play(ref sim, kick, 3 * Free.TickRate, tick => tick == kickAt + 2 ? ControlModel.Attached : (ControlModel?)null);

            int Contact(List<List<SimEvent>> run) => run.FindIndex(e => e.Any(x => x.Kind == SimEventKind.Kicked));
            Assert.Equal(1, switched.Sum(e => e.Count(x => x.Kind == SimEventKind.Kicked)));
            Assert.Equal(Contact(unswitched), Contact(switched));
        }

        [Fact]
        public void An_aftertouch_window_open_across_a_switch_still_shapes_the_kick()
        {
            int kickAt = 2;
            Func<long, Command[]> kick = tick => new[]
            {
                new Command(A, tick, Vec2.Zero, tick == kickAt ? KickIntent.AlongHeading(0.6, 0) : (KickIntent?)null, new Vec2(0, 1)),
            };

            // A standing kicker and no one else: after contact no one can take the ball under
            // either model, so the switch must change nothing.
            var plain = new Simulator(Free, AtFeet(Free));
            List<List<SimEvent>> unswitched = Play(ref plain, kick, Free.TickRate);
            int contact = unswitched.FindIndex(e => e.Any(x => x.Kind == SimEventKind.Kicked));
            Assert.True(contact > 0, "the kick didn't connect");

            var sim = new Simulator(Free, AtFeet(Free));
            Play(ref sim, kick, Free.TickRate, tick => tick == contact + 2 ? ControlModel.Attached : (ControlModel?)null);
            Assert.Equal(plain.StateHash(), sim.StateHash());
        }

        [Fact]
        public void A_session_with_switches_is_deterministic_and_each_segment_replays()
        {
            Func<long, Command[]> weave = tick => new[] { new Command(A, tick, new Vec2(Math.Cos(tick * 0.03), Math.Sin(tick * 0.03))) };
            Func<long, ControlModel?> switches = tick => tick == 120 ? ControlModel.Attached : tick == 240 ? ControlModel.Free : tick == 300 ? ControlModel.Attached : (ControlModel?)null;

            var first = new List<ulong>();
            var a = new Simulator(Free, AtFeet(Free));
            Play(ref a, weave, 6 * Free.TickRate, switches, first);

            var second = new List<ulong>();
            var b = new Simulator(Free, AtFeet(Free));
            Play(ref b, weave, 4 * Free.TickRate, switches, second);

            // From a snapshot of the last segment, under its config, the run goes on the same.
            Simulator restored = Snapshot.Restore(Snapshot.Capture(b), b.Config);
            Play(ref b, weave, 2 * Free.TickRate, null, second);
            Play(ref restored, weave, 2 * Free.TickRate);

            Assert.Equal(first, second);
            Assert.Equal(b.StateHash(), restored.StateHash());
            Assert.Equal(ControlModel.Attached, b.Config.ControlModel);
        }
    }
}
