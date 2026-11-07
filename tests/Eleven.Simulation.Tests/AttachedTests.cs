using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The ATTACHED comparator (#60): while a player has the ball, a bounded controller steers its
    /// velocity toward a lead point ahead of them, and the ball still moves under its own physics.
    /// These pin relationships and invariants, not untuned feel values.
    /// </summary>
    public class AttachedTests
    {
        private const int A = TestWorld.Controller;
        private const int B = 8;

        private static SimConfig Attached() => new SimConfig(controlModel: ControlModel.Attached);

        private static SimState AtFeet(SimConfig config, double ballControl = 0.7, Vec2? ballAt = null, Vec3? ballVelocity = null)
        {
            Vec2 at = ballAt ?? new Vec2(0.6, 0);
            SimState state = TestWorld.State(config, new Vec3(at.X, at.Y, config.BallRadius), ballVelocity);
            state.Players[0].Capabilities = PlayerCapabilities.Reference.With("ballControl", ballControl);
            return state;
        }

        private sealed class Run
        {
            public readonly List<SimState> Before = new List<SimState>();
            public readonly List<List<SimEvent>> Events = new List<List<SimEvent>>();
            public Simulator Sim = null!;

            public int Count(SimEventKind kind, int from = 0, int? to = null) =>
                Events.Skip(from).Take((to ?? Events.Count) - from).Sum(e => e.Count(x => x.Kind == kind));

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

        private static double Separation(SimState s) => Math.Sqrt((s.Ball.Position.Horizontal - s.Players[0].Position).LengthSquared);

        private static double Lead(SimState s)
        {
            PlayerState p = s.Players[0];
            Vec2 offset = s.Ball.Position.Horizontal - p.Position;
            double speed = Math.Sqrt(p.Velocity.LengthSquared);
            return speed < 1e-6 ? Math.Sqrt(offset.LengthSquared) : (offset.X * p.Velocity.X + offset.Y * p.Velocity.Y) / speed;
        }

        [Fact]
        public void A_player_takes_the_ball_and_runs_with_it()
        {
            SimConfig config = Attached();
            Run run = Play(config, AtFeet(config), Stick(1, 0), 4);
            Assert.True(run.Count(SimEventKind.Attached) > 0, "never attached");
            Assert.Equal(0, run.Count(SimEventKind.Released));
            Assert.Equal(1, run.After.Ball.AttachedTo);
            Assert.True(run.After.Players[0].Position.X > 15, $"the player only reached x = {run.After.Players[0].Position.X:F1} m");
            Assert.All(run.Before.Skip(config.TickRate), s => Assert.True(Separation(s) <= config.AttachedLossReach, $"tick {s.Tick}: the ball {Separation(s):F2} m away"));
        }

        [Fact]
        public void The_ball_runs_further_ahead_at_speed()
        {
            SimConfig config = Attached();
            double MeanLead(double stick)
            {
                Run run = Play(config, AtFeet(config), Stick(stick, 0), 5);
                return run.Before.Skip(3 * config.TickRate).Average(Lead);
            }

            double jog = MeanLead(0.4), run = MeanLead(1);
            Assert.True(run > jog + 0.1, $"lead at full stick {run:F3} m, at 0.4 stick {jog:F3} m");
        }

        // Runs with the ball along +x, then turns the stick by `degrees`; returns the largest
        // separation over the two seconds after the turn.
        private static double SeparationAfterTurn(double degrees, double ballControl, double stick = 1)
        {
            SimConfig config = Attached();
            double r = degrees * Math.PI / 180;
            Run run = Play(config, AtFeet(config, ballControl), tick => new[]
            {
                new Command(A, tick, (tick < 2 * config.TickRate ? new Vec2(1, 0) : new Vec2(Math.Cos(r), Math.Sin(r))) * stick),
            }, 4);

            SimState atTurn = run.Before[2 * config.TickRate];
            Assert.Equal(1, atTurn.Ball.AttachedTo);
            return run.Before.Skip(2 * config.TickRate).Max(Separation);
        }

        [Fact]
        public void A_sharper_turn_at_speed_separates_the_ball_more()
        {
            double at45 = SeparationAfterTurn(45, 0.3), at90 = SeparationAfterTurn(90, 0.3);
            Assert.True(at90 > at45 + 0.1, $"control 0.3, after 90°: {at90:F2} m; after 45°: {at45:F2} m");
        }

        [Fact]
        public void The_same_turn_is_harder_at_speed()
        {
            double jog = SeparationAfterTurn(90, 0.3, 0.5), sprint = SeparationAfterTurn(90, 0.3);
            Assert.True(sprint > jog + 0.1, $"control 0.3, 90° at full stick: {sprint:F2} m; at half stick: {jog:F2} m");
        }

        [Fact]
        public void Better_ball_control_keeps_a_sharp_turn_tighter()
        {
            double good = SeparationAfterTurn(90, 0.9), poor = SeparationAfterTurn(90, 0.3);
            Assert.True(poor > good + 0.1, $"control 0.3: {poor:F2} m; control 0.9: {good:F2} m");
        }

        [Fact]
        public void A_sharp_turn_at_speed_can_lose_the_ball()
        {
            SimConfig config = Attached();
            Run run = Play(config, AtFeet(config, 0.3), tick => new[]
            {
                new Command(A, tick, tick < 2 * config.TickRate ? new Vec2(1, 0) : new Vec2(-1, 0.2)),
            }, 4);

            Assert.True(run.Count(SimEventKind.Released, 2 * config.TickRate) > 0, "a reversal at a sprint never lost the ball at control 0.3");
        }

        [Fact]
        public void The_correction_is_bounded_and_the_ball_moves_only_by_its_velocity()
        {
            SimConfig config = Attached();
            Func<long, Command[]> weave = tick => new[] { new Command(A, tick, new Vec2(Math.Cos(tick * 0.05), Math.Sin(tick * 0.05))) };
            Run run = Play(config, AtFeet(config, 1), weave, 4);
            Assert.True(run.Count(SimEventKind.Attached) > 0, "never attached");

            double dt = config.TickSeconds;
            double cap = config.AttachedAcceleration * dt + config.RollingFriction * config.Gravity * dt + 1e-9;
            for (int i = 0; i + 1 < run.Before.Count; i++)
            {
                SimState before = run.Before[i], after = run.Before[i + 1];
                Vec3 moved = after.Ball.Position - before.Ball.Position;
                double stepped = Math.Sqrt(after.Ball.Velocity.LengthSquared) * dt;
                Assert.True(Math.Sqrt(moved.LengthSquared) <= stepped + 1e-9, $"tick {i}: the ball moved {Math.Sqrt(moved.LengthSquared):F4} m on a {stepped:F4} m step");

                if (before.Ball.AttachedTo == 0 || run.Events[i].Any(e => e.Kind == SimEventKind.Attached)) continue;
                double change = Math.Sqrt((after.Ball.Velocity.Horizontal - before.Ball.Velocity.Horizontal).LengthSquared);
                Assert.True(change <= cap, $"tick {i}: the ball's velocity changed by {change:F3} m/s in one step, more than {cap:F3}");
            }
        }

        [Fact]
        public void A_ball_far_from_its_target_isnt_snapped_back()
        {
            SimConfig config = Attached();
            SimState state = AtFeet(config, 1, ballAt: new Vec2(0, 1.2));
            state.Ball.AttachedTo = 1;
            Run run = Play(config, state, Stick(0, 0), 1.0 / 60);

            Assert.Equal(1, run.After.Ball.AttachedTo);
            double moved = Math.Sqrt((run.After.Ball.Position - state.Ball.Position).LengthSquared);
            Assert.True(moved < 0.01, $"the ball moved {moved:F3} m in one step from 1.2 m off its target");
            Assert.True(Math.Sqrt(run.After.Ball.Velocity.LengthSquared) > 0, "no correction at all");
        }

        [Fact]
        public void A_slow_ball_is_settled_and_a_fast_one_beats_the_first_touch()
        {
            SimConfig config = Attached();
            Run slow = Play(config, AtFeet(config, ballAt: new Vec2(10, 0), ballVelocity: new Vec3(-8, 0, 0)), Stick(0.5, 0), 3);
            Assert.True(slow.Count(SimEventKind.Attached) > 0, "the 8 m/s ball was never taken");
            Assert.Equal(1, slow.After.Ball.AttachedTo);
            Assert.True(Separation(slow.After) <= config.ControlReach, $"8 m/s ball {Separation(slow.After):F2} m away");

            Run fast = Play(config, AtFeet(config, ballAt: new Vec2(10, 0), ballVelocity: new Vec3(-16, 0, 0)), Stick(0.5, 0), 3);
            Assert.Equal(0, fast.After.Ball.AttachedTo);
            Assert.True(Separation(fast.After) > config.AttachedLossReach + 1, $"16 m/s ball only {Separation(fast.After):F2} m away");
        }

        [Fact]
        public void A_kick_releases_the_ball_and_nothing_takes_it_back_on_its_step()
        {
            SimConfig config = Attached();
            int kickAt = 2 * config.TickRate;
            Run run = Play(config, AtFeet(config), tick => new[]
            {
                tick == kickAt ? new Command(A, tick, new Vec2(0.6, 0), KickIntent.AlongHeading(0.6, 0)) : new Command(A, tick, new Vec2(0.6, 0)),
            }, 3);

            Assert.Equal(1, run.Before[kickAt].Ball.AttachedTo);
            int kicked = run.Events.FindIndex(e => e.Any(x => x.Kind == SimEventKind.Kicked));
            Assert.True(kicked >= kickAt, "the kick didn't connect");
            Assert.Contains(run.Events[kicked], x => x.Kind == SimEventKind.Released);
            Assert.Equal(0, run.Count(SimEventKind.Attached, kicked, kicked + config.KickCooldownTicks + 1));
            Assert.True(Math.Sqrt(run.Before[kicked + 1].Ball.Velocity.LengthSquared) > 8, "the kick was held back");
        }

        [Fact]
        public void A_standing_player_stops_and_holds_the_ball()
        {
            SimConfig config = Attached();
            Run run = Play(config, AtFeet(config, ballAt: new Vec2(3, 0), ballVelocity: new Vec3(-3, 0, 0)), Stick(0, 0), 3);
            Assert.Equal(1, run.After.Ball.AttachedTo);
            Assert.True(Separation(run.After) <= config.ControlReach, $"the ball {Separation(run.After):F2} m away");
            Assert.True(Math.Sqrt(run.After.Ball.Velocity.LengthSquared) < 0.2, $"the ball still moving at {Math.Sqrt(run.After.Ball.Velocity.LengthSquared):F2} m/s");
        }

        [Fact]
        public void Releasing_the_stick_alone_keeps_the_ball()
        {
            SimConfig config = Attached();
            Run run = Play(config, AtFeet(config), tick => new[] { new Command(A, tick, tick < 2 * config.TickRate ? new Vec2(1, 0) : Vec2.Zero) }, 4);
            Assert.Equal(0, run.Count(SimEventKind.Released));
            Assert.Equal(1, run.After.Ball.AttachedTo);
            Assert.True(Separation(run.After) <= config.ControlReach, $"the ball {Separation(run.After):F2} m away");
        }

        [Fact]
        public void The_ball_is_taken_within_the_control_reach_and_lost_beyond_the_release_reach()
        {
            SimConfig config = Attached();
            double between = (config.ControlReach + config.AttachedLossReach) / 2;

            // Loose, between the two reaches: not taken.
            Run loose = Play(config, AtFeet(config, ballAt: new Vec2(between, 0)), Stick(0, 0), 1.0 / 60);
            Assert.Equal(0, loose.Count(SimEventKind.Attached));

            // Held at the same distance: kept.
            SimState held = AtFeet(config, ballAt: new Vec2(between, 0));
            held.Ball.AttachedTo = 1;
            Run kept = Play(config, held, Stick(0, 0), 1.0 / 60);
            Assert.Equal(0, kept.Count(SimEventKind.Released));
            Assert.Equal(1, kept.After.Ball.AttachedTo);

            // Held beyond the release reach: lost, and not taken again on the same step.
            SimState far = AtFeet(config, ballAt: new Vec2(config.AttachedLossReach + 0.1, 0));
            far.Ball.AttachedTo = 1;
            Run lost = Play(config, far, Stick(0, 0), 1.0 / 60);
            Assert.Equal(1, lost.Count(SimEventKind.Released));
            Assert.Equal(0, lost.After.Ball.AttachedTo);
        }

        [Fact]
        public void Only_a_player_whose_controller_sent_an_accepted_command_takes_the_ball()
        {
            SimConfig config = Attached();
            SimState Start()
            {
                SimState state = AtFeet(config);
                state.Players = new[]
                {
                    state.Players[0],
                    new PlayerState { Id = 2, OwnerControllerId = B, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(20, 0) },
                };
                return state;
            }

            Run others = Play(config, Start(), tick => new[] { new Command(B, tick, new Vec2(1, 0)) }, 1);
            Assert.Equal(0, others.Count(SimEventKind.Attached));

            Run own = Play(config, Start(), tick => new[] { new Command(A, tick, Vec2.Zero), new Command(B, tick, new Vec2(1, 0)) }, 1);
            Assert.True(own.Count(SimEventKind.Attached) > 0, "the owner's command didn't take the ball");
        }

        [Fact]
        public void A_nearer_eligible_player_takes_the_ball_over()
        {
            SimConfig config = Attached();
            SimState state = AtFeet(config, ballAt: new Vec2(0.8, 0));
            state.Ball.AttachedTo = 1;
            state.Players = new[]
            {
                state.Players[0],
                new PlayerState { Id = 2, OwnerControllerId = B, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(1.1, 0) },
            };

            Run run = Play(config, state, tick => new[] { new Command(A, tick, Vec2.Zero), new Command(B, tick, Vec2.Zero) }, 1.0 / 60);
            Assert.Contains(run.Events[0], x => x.Kind == SimEventKind.Released && x.Subject == 1);
            Assert.Contains(run.Events[0], x => x.Kind == SimEventKind.Attached && x.Subject == 2);
            Assert.Equal(2, run.After.Ball.AttachedTo);
        }

        [Fact]
        public void Better_control_drifts_less_at_the_same_speed()
        {
            SimConfig config = Attached();
            double Drift(double ballControl)
            {
                Func<long, Command[]> weave = tick => new[] { new Command(A, tick, new Vec2(Math.Cos(tick * 0.04), Math.Sin(tick * 0.04))) };
                Run run = Play(config, AtFeet(config, ballControl), weave, 6);
                return run.Before.Skip(2 * config.TickRate).Average(Separation);
            }

            double good = Drift(0.9), poor = Drift(0.3);
            Assert.True(poor > good + 0.02, $"mean separation in a weave, control 0.3: {poor:F3} m; control 0.9: {good:F3} m");
        }

        [Fact]
        public void Attached_play_is_deterministic_and_survives_snapshot_and_restore()
        {
            SimConfig config = Attached();
            Func<long, Command[]> weave = tick => new[] { new Command(A, tick, new Vec2(Math.Cos(tick * 0.03), Math.Sin(tick * 0.03))) };
            Run first = Play(config, AtFeet(config, 0.4), weave, 3);
            Run second = Play(config, AtFeet(config, 0.4), weave, 3);
            Assert.True(first.Count(SimEventKind.Attached) > 0, "never attached");
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
        public void Other_models_never_attach_the_ball()
        {
            foreach (SimConfig config in new[] { new SimConfig(controlModel: ControlModel.Free), new SimConfig() })
            {
                SimState state = AtFeet(config);
                state.Ball.AttachedTo = 1;
                Run run = Play(config, state, Stick(0.6, 0), 2);
                Assert.Equal(0, run.Count(SimEventKind.Attached));
                Assert.Equal(0, run.Before[1].Ball.AttachedTo);
            }
        }

        [Fact]
        public void The_model_its_values_and_the_attachment_are_part_of_the_identity()
        {
            SimConfig free = new SimConfig(controlModel: ControlModel.Free), attached = free.WithControlModel(ControlModel.Attached);
            Assert.NotEqual(free.Hash, attached.Hash);
            Assert.Equal(ControlModel.Attached, SimConfig.Decode(attached.Encode()).ControlModel);
            Assert.NotEqual(attached.Hash, attached.With("attachedAcceleration", 20).Hash);
            Assert.Equal(attached.ControlReach, attached.With("attachedReleaseReach", attached.ControlReach - 0.1).AttachedLossReach);

            SimState loose = AtFeet(attached), held = AtFeet(attached);
            held.Ball.AttachedTo = 1;
            Assert.NotEqual(new Simulator(attached, loose).StateHash(), new Simulator(attached, held).StateHash());
            SimState unknown = AtFeet(attached);
            unknown.Ball.AttachedTo = 5;
            Assert.Throws<ArgumentException>(() => new Simulator(attached, unknown));
        }
    }
}
