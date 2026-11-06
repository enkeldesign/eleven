using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The FREE ball (#59): a player in control pushes an independent ball ahead with discrete,
    /// bounded touches. These pin relationships and invariants, not untuned feel values.
    /// </summary>
    public class DribbleTests
    {
        private const int A = TestWorld.Controller;
        private const int B = 8;

        private static SimConfig Free(Func<SimConfig, SimConfig>? tune = null)
        {
            var config = new SimConfig(controlModel: ControlModel.Free);
            return tune == null ? config : tune(config);
        }

        private static SimState AtFeet(SimConfig config, double ballControl = 0.7, Vec2? ballAt = null, Vec3? ballVelocity = null)
        {
            SimState state = TestWorld.State(config, new Vec3((ballAt ?? new Vec2(0.6, 0)).X, (ballAt ?? new Vec2(0.6, 0)).Y, config.BallRadius), ballVelocity);
            state.Players[0].Capabilities = PlayerCapabilities.Reference.With("ballControl", ballControl);
            return state;
        }

        /// <summary>One run: the state before each step and the events of each step.</summary>
        private sealed class Run
        {
            public readonly List<SimState> Before = new List<SimState>();
            public readonly List<List<SimEvent>> Events = new List<List<SimEvent>>();
            public Simulator Sim = null!;

            public int Touches(int from = 0, int? to = null) => Events.Skip(from).Take((to ?? Events.Count) - from).Sum(e => e.Count(x => x.Kind == SimEventKind.Touched));

            public SimState After => Sim.PeekState();
        }

        private static Run Play(SimConfig config, SimState start, Func<long, Command[]> commands, double seconds)
        {
            var run = new Run { Sim = new Simulator(config, start) };
            long ticks = (long)(seconds * config.TickRate);
            for (long t = 0; t < ticks; t++)
            {
                run.Before.Add(run.Sim.PeekState().Clone());
                var events = new List<SimEvent>();
                run.Sim.Step(commands(run.Sim.Tick), events);
                run.Events.Add(events);
            }

            return run;
        }

        private static Func<long, Command[]> Stick(double x, double y) => tick => new[] { new Command(A, tick, new Vec2(x, y)) };

        // The ball's offset ahead of the player, along the player's direction of travel.
        private static double Lead(SimState s)
        {
            PlayerState p = s.Players[0];
            Vec2 offset = s.Ball.Position.Horizontal - p.Position;
            double speed = Math.Sqrt(p.Velocity.LengthSquared);
            if (speed < 1e-6) return Math.Sqrt(offset.LengthSquared);
            return (offset.X * p.Velocity.X + offset.Y * p.Velocity.Y) / speed;
        }

        private static double Separation(SimState s) => Math.Sqrt((s.Ball.Position.Horizontal - s.Players[0].Position).LengthSquared);

        [Fact]
        public void A_controlled_player_runs_with_the_ball()
        {
            SimConfig config = Free();
            Run run = Play(config, AtFeet(config), Stick(0.6, 0), 4);

            Assert.True(run.Touches() > 4, $"{run.Touches()} touches");
            Assert.True(run.After.Ball.Position.X > 12, $"ball at {run.After.Ball.Position.X:F2}");
            Assert.True(Separation(run.After) <= config.ControlReach + 0.3, $"separation {Separation(run.After):F2} m");
            Assert.True(Lead(run.After) > 0, $"lead {Lead(run.After):F2} m");
        }

        [Fact]
        public void The_ball_runs_further_ahead_at_speed()
        {
            SimConfig config = Free();
            double MeanLead(double stick)
            {
                Run run = Play(config, AtFeet(config), Stick(stick, 0), 4);
                return run.Before.Skip(2 * config.TickRate).Average(Lead);
            }

            double jog = MeanLead(0.4), run = MeanLead(1);
            Assert.InRange(jog, 0.2, config.ControlReach + 0.3);
            Assert.InRange(run, 0.2, config.ControlReach + 0.5);
            Assert.True(run > jog + 0.05, $"lead at full stick {run:F3} m, at 0.4 stick {jog:F3} m");
        }

        // Runs with the ball along +x, then turns the stick by `degrees`; returns the largest
        // separation over the two seconds after the turn.
        private static double SeparationAfterTurn(SimConfig config, double degrees, double ballControl, double stick = 1)
        {
            double r = degrees * Math.PI / 180;
            Run run = Play(config, AtFeet(config, ballControl), tick => new[]
            {
                new Command(A, tick, (tick < 2 * config.TickRate ? new Vec2(1, 0) : new Vec2(Math.Cos(r), Math.Sin(r))) * stick),
            }, 4);

            SimState atTurn = run.Before[2 * config.TickRate];
            Assert.True(Separation(atTurn) <= config.ControlReach + 0.3, $"not in control before the turn: {Separation(atTurn):F2} m");
            return run.Before.Skip(2 * config.TickRate).Max(Separation);
        }

        [Fact]
        public void A_sharper_turn_at_speed_separates_the_ball_more()
        {
            SimConfig config = Free();
            double at45 = SeparationAfterTurn(config, 45, 0.3), at90 = SeparationAfterTurn(config, 90, 0.3);
            Assert.True(at90 > at45 + 0.1, $"control 0.3, after 90°: {at90:F2} m; after 45°: {at45:F2} m");
        }

        [Fact]
        public void The_same_turn_is_harder_at_speed()
        {
            SimConfig config = Free();
            double jog = SeparationAfterTurn(config, 90, 0.3, 0.5), sprint = SeparationAfterTurn(config, 90, 0.3);
            Assert.True(sprint > jog + 0.1, $"control 0.3, 90° at full stick: {sprint:F2} m; at half stick: {jog:F2} m");
        }

        [Fact]
        public void Better_ball_control_keeps_a_sharp_turn_tighter()
        {
            SimConfig config = Free();
            double good = SeparationAfterTurn(config, 90, 0.9), poor = SeparationAfterTurn(config, 90, 0.3);
            Assert.True(poor > good + 0.1, $"control 0.3: {poor:F2} m; control 0.9: {good:F2} m");
        }

        [Fact]
        public void A_slow_ball_is_settled_and_a_fast_one_beats_the_first_touch()
        {
            SimConfig config = Free();

            // A ball rolled at a standing player from 4 m: slow, it is brought under control.
            Run slow = Play(config, AtFeet(config, ballAt: new Vec2(4, 0), ballVelocity: new Vec3(-4, 0, 0)), Stick(0, 0), 2);
            Assert.True(slow.Touches() > 0, "no touch on the slow ball");
            Assert.True(Separation(slow.After) <= config.ControlReach, $"slow ball {Separation(slow.After):F2} m away");
            Assert.True(Math.Sqrt(slow.After.Ball.Velocity.LengthSquared) < 0.5, $"slow ball still moving at {Math.Sqrt(slow.After.Ball.Velocity.LengthSquared):F2} m/s");

            // Fast, head-on at a player jogging on to it, the first touch can't kill it: it gets away
            // and isn't reattached beyond reach.
            Run fast = Play(config, AtFeet(config, ballAt: new Vec2(10, 0), ballVelocity: new Vec3(-16, 0, 0)), Stick(0.5, 0), 3);
            Assert.True(fast.Touches() > 0, "no touch on the fast ball");
            Assert.True(Separation(fast.After) > config.ControlReach + 1, $"fast ball only {Separation(fast.After):F2} m away");
        }

        [Fact]
        public void A_kick_leaves_the_ball_free_and_no_touch_reacquires_it()
        {
            SimConfig config = Free();
            int kickAt = 2 * config.TickRate;
            Run run = Play(config, AtFeet(config), tick => new[]
            {
                tick == kickAt ? new Command(A, tick, new Vec2(0.6, 0), KickIntent.AlongHeading(0.6, 0)) : new Command(A, tick, new Vec2(0.6, 0)),
            }, 3);

            Assert.True(run.Touches(0, kickAt) > 0, "not dribbling before the kick");
            int kicked = run.Events.FindIndex(e => e.Any(x => x.Kind == SimEventKind.Kicked));
            Assert.True(kicked >= kickAt, "the kick didn't connect");
            Assert.Equal(0, run.Touches(kickAt, kicked + config.KickCooldownTicks + 1));
        }

        [Fact]
        public void Touches_are_deterministic_and_survive_snapshot_and_restore()
        {
            SimConfig config = Free();
            Func<long, Command[]> weave = tick => new[] { new Command(A, tick, new Vec2(Math.Cos(tick * 0.03), Math.Sin(tick * 0.03))) };
            Run first = Play(config, AtFeet(config, 0.4), weave, 3);
            Run second = Play(config, AtFeet(config, 0.4), weave, 3);
            Assert.True(first.Touches() > 0, "no touches");
            Assert.Equal(first.Sim.StateHash(), second.Sim.StateHash());

            var sim = new Simulator(config, AtFeet(config, 0.4));
            for (int t = 0; t < config.TickRate; t++) sim.Step(weave(sim.Tick), new List<SimEvent>());
            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), config);
            for (int t = config.TickRate; t < 3 * config.TickRate; t++)
            {
                sim.Step(weave(sim.Tick), new List<SimEvent>());
                restored.Step(weave(restored.Tick), new List<SimEvent>());
            }

            Assert.Equal(sim.StateHash(), restored.StateHash());
            Assert.Equal(first.Sim.StateHash(), sim.StateHash());
        }

        [Fact]
        public void The_ball_only_moves_by_its_velocity_and_is_touched_only_within_reach()
        {
            SimConfig config = Free();
            Func<long, Command[]> weave = tick => new[] { new Command(A, tick, new Vec2(Math.Cos(tick * 0.05), Math.Sin(tick * 0.05))) };
            Run run = Play(config, AtFeet(config, 0.3), weave, 4);
            Assert.True(run.Touches() > 0, "no touches");

            for (int i = 0; i + 1 < run.Before.Count; i++)
            {
                SimState before = run.Before[i], after = run.Before[i + 1];
                Vec3 moved = after.Ball.Position - before.Ball.Position;
                double stepped = Math.Sqrt(after.Ball.Velocity.LengthSquared) * config.TickSeconds;
                Assert.True(Math.Sqrt(moved.LengthSquared) <= stepped + 1e-9, $"tick {i}: the ball moved {Math.Sqrt(moved.LengthSquared):F4} m on a {stepped:F4} m step");

                if (!run.Events[i].Any(e => e.Kind == SimEventKind.Touched)) continue;
                PlayerState p = before.Players[0];
                double reachable = config.ControlReach + Math.Sqrt(p.Velocity.LengthSquared) * config.TickSeconds + 1e-9;
                Assert.True(Separation(before) <= reachable + config.Gravity * config.TickSeconds * config.TickSeconds,
                    $"tick {i}: touched a ball {Separation(before):F2} m away");
            }
        }

        [Fact]
        public void At_most_one_player_touches_the_ball_on_a_step()
        {
            SimConfig config = Free();
            SimState state = AtFeet(config, ballAt: new Vec2(0, 0));
            state.Players = new[]
            {
                new PlayerState { Id = 1, OwnerControllerId = A, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(-0.5, 0) },
                new PlayerState { Id = 2, OwnerControllerId = B, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(0.5, 0), Heading = Math.PI },
            };

            Run run = Play(config, state, tick => new[] { new Command(A, tick, new Vec2(1, 0)), new Command(B, tick, new Vec2(-1, 0)) }, 2);
            Assert.True(run.Touches() > 0, "no touches");
            Assert.All(run.Events, e => Assert.True(e.Count(x => x.Kind == SimEventKind.Touched) <= 1));
        }

        [Fact]
        public void A_standing_player_with_the_ball_at_rest_leaves_it_alone()
        {
            SimConfig config = Free();
            Run run = Play(config, AtFeet(config), Stick(0, 0), 1);
            Assert.Equal(0, run.Touches());
            Assert.Equal(new Vec3(0.6, 0, config.BallRadius), run.After.Ball.Position);
        }

        [Fact]
        public void Under_off_nobody_touches_the_ball()
        {
            var config = new SimConfig();
            Assert.Equal(ControlModel.Off, config.ControlModel);
            Run run = Play(config, AtFeet(config), Stick(0.6, 0), 2);
            Assert.Equal(0, run.Touches());
        }

        [Fact]
        public void The_model_and_its_values_are_part_of_the_config_identity()
        {
            var off = new SimConfig();
            SimConfig free = off.WithControlModel(ControlModel.Free);
            Assert.NotEqual(off.Hash, free.Hash);
            Assert.Equal(free.Hash, SimConfig.Decode(free.Encode()).Hash);
            Assert.Equal(ControlModel.Free, SimConfig.Decode(free.Encode()).ControlModel);
            Assert.NotEqual(free.Hash, free.With("dribbleLeadFast", 1.5).Hash);
            Assert.Equal(ControlModel.Free, free.With("touchError", 0.1).ControlModel);
        }
    }
}
