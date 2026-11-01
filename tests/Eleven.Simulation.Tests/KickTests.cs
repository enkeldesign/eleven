using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #37: the generic manual kick. An intent winds up in simulation state, launches from the
    /// intent, the kicker's capabilities and the config, never retargets, and can be shaped by
    /// aftertouch only inside its window.
    /// </summary>
    public class KickTests
    {
        private const double Close = 1e-12;

        // The reference player without execution error, so launches can be checked exactly.
        private static PlayerCapabilities Exact
        {
            get
            {
                PlayerCapabilities c = PlayerCapabilities.Reference;
                c.KickError = 0;
                return c;
            }
        }

        // A standing player at the origin, facing +x, with the ball at rest in reach in front.
        private static SimState Ready(SimConfig config, PlayerCapabilities? capabilities = null, Vec3? ball = null, Vec3? ballVelocity = null)
        {
            SimState state = State(config, ballPosition: ball ?? new Vec3(0.5, 0, config.BallRadius), ballVelocity: ballVelocity);
            state.Players[0].Capabilities = capabilities ?? Exact;
            return state;
        }

        private static PlayerState Player(Simulator sim) => sim.PeekState().Players[0];

        private static BallState Ball(Simulator sim) => sim.PeekState().Ball;

        private static double Horizontal(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

        private static double Angle(Vec3 v) => Math.Atan2(v.Y, v.X);

        // Steps with no command until the kick connects or misses; returns the events of that step.
        private static List<SimEvent> UntilContact(Simulator sim)
        {
            for (int i = 0; i < 1000; i++)
            {
                List<SimEvent> events = Step(sim);
                if (events.Any(e => e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KickMissed)) return events;
            }

            Assert.Fail("The kick never connected or missed within 1000 steps of its intent.");
            return new List<SimEvent>();
        }

        // A kick on tick 0, then `aftertouch` held from tick 1 for `ticks` steps in all.
        private static Simulator KickAndShape(SimConfig config, KickIntent intent, Vec2 aftertouch, int ticks, Action<Simulator>? observe = null)
        {
            var sim = Simulator(config, Ready(config));
            Step(sim, Kick(0, intent));
            observe?.Invoke(sim);
            for (int t = 1; t < ticks; t++)
            {
                Step(sim, Aftertouch(t, aftertouch.X, aftertouch.Y));
                observe?.Invoke(sim);
            }

            return sim;
        }

        // ---- Commands -----------------------------------------------------------------------------

        [Theory]
        [InlineData(double.NaN, 0, 1, 0.5, 0, 0)]
        [InlineData(1, double.PositiveInfinity, 1, 0.5, 0, 0)]
        [InlineData(1, 0, double.NaN, 0.5, 0, 0)]
        [InlineData(1, 0, 1, double.NegativeInfinity, 0, 0)]
        [InlineData(1, 0, 1, 0.5, double.NaN, 0)]
        [InlineData(1, 0, 1, 0.5, 0, double.PositiveInfinity)]
        public void A_non_finite_kick_intent_or_aftertouch_rejects_the_whole_command(double dx, double dy, double strength, double loft, double ax, double ay)
        {
            var config = Config();
            var sim = Simulator(config, Ready(config));
            var command = new Command(Controller, 0, new Vec2(1, 0), new KickIntent(new Vec2(dx, dy), strength, loft), new Vec2(ax, ay));

            List<SimEvent> events = Step(sim, command);

            Assert.Equal(new[] { new SimEvent(0, SimEventKind.CommandRejected, Controller, CommandRejection.Invalid) }, events);
            Assert.Equal(Vec2.Zero, Player(sim).Velocity);
            Assert.Equal(0, Player(sim).WindUpTicks);
        }

        [Fact]
        public void Strength_and_loft_are_clamped_and_long_aftertouch_is_scaled_to_full()
        {
            var config = Config();
            Simulator over = KickAndShape(config, new KickIntent(new Vec2(3, 4), 7, -2), new Vec2(0, 5), 60);
            Simulator clamped = KickAndShape(config, new KickIntent(new Vec2(3, 4), 1, 0), new Vec2(0, 1), 60);

            Assert.Equal(clamped.StateHash(), over.StateHash());
            Assert.True(Horizontal(Ball(clamped).Velocity) > 0);
        }

        // ---- Wind-up ------------------------------------------------------------------------------

        [Fact]
        public void A_kick_connects_when_its_wind_up_ends_and_not_before()
        {
            var config = Config();
            int windUp = config.KickWindUpTicks;
            Assert.True(windUp > 1);
            var sim = Simulator(config, Ready(config));

            Assert.DoesNotContain(Step(sim, Kick(0, FullKick)), e => e.Kind == SimEventKind.Kicked);
            Assert.Equal(windUp, Player(sim).WindUpTicks);
            for (int t = 1; t < windUp; t++)
            {
                Assert.DoesNotContain(Step(sim), e => e.Kind == SimEventKind.Kicked);
                Assert.Equal(windUp - t, Player(sim).WindUpTicks);
                Assert.Equal(Vec3.Zero, Ball(sim).Velocity);
            }

            Assert.Contains(new SimEvent(windUp, SimEventKind.Kicked, PlayerId), Step(sim));
            PlayerState player = Player(sim);
            Assert.Equal(0, player.WindUpTicks);
            Assert.Equal(config.KickCooldownTicks, player.KickCooldown);
            Assert.Equal(config.AftertouchTicks, player.AftertouchTicks);
        }

        [Fact]
        public void The_wind_up_holds_the_intent_in_state()
        {
            var config = Config();
            var sim = Simulator(config, Ready(config));
            Step(sim, Kick(0, new KickIntent(new Vec2(0, -2), 0.75, 0.25)));

            PlayerState player = Player(sim);
            Assert.Equal(config.KickWindUpTicks, player.WindUpTicks);
            Assert.Equal(-Math.PI / 2, player.WindUpDirection, 15);
            Assert.Equal(0.75, player.WindUpStrength);
            Assert.Equal(0.25, player.WindUpLoft);
        }

        [Fact]
        public void With_no_wind_up_a_kick_connects_on_its_own_step()
        {
            SimConfig config = Config().With("kickWindUpSeconds", 0);
            var sim = Simulator(config, Ready(config));

            Assert.Contains(new SimEvent(0, SimEventKind.Kicked, PlayerId), Step(sim, Kick(0, FullKick)));
        }

        [Fact]
        public void A_wind_up_that_ends_with_the_ball_out_of_reach_misses_and_costs_no_cooldown()
        {
            var config = Config();
            var sim = Simulator(config, Ready(config, ball: new Vec3(config.KickReach + 0.5, 0, config.BallRadius)));
            Step(sim, Kick(0, FullKick));

            List<SimEvent> contact = UntilContact(sim);

            Assert.Contains(new SimEvent(config.KickWindUpTicks, SimEventKind.KickMissed, PlayerId), contact);
            Assert.DoesNotContain(contact, e => e.Kind == SimEventKind.Kicked);
            Assert.Equal(Vec3.Zero, Ball(sim).Velocity);
            Assert.Equal(0, Player(sim).KickCooldown);
            Assert.Equal(0, Player(sim).AftertouchTicks);

            Step(sim, Kick(sim.Tick, FullKick));
            Assert.Equal(config.KickWindUpTicks, Player(sim).WindUpTicks);
        }

        [Fact]
        public void A_ball_rolling_out_of_reach_during_the_wind_up_is_missed()
        {
            var config = Config();
            var sim = Simulator(config, Ready(config, ballVelocity: new Vec3(8, 0, 0)));
            Step(sim, Kick(0, FullKick));

            Assert.Contains(UntilContact(sim), e => e.Kind == SimEventKind.KickMissed);
        }

        [Fact]
        public void Intents_during_a_wind_up_or_a_cooldown_are_ignored()
        {
            var config = Config();
            var sim = Simulator(config, Ready(config));
            Step(sim, Kick(0, new KickIntent(new Vec2(1, 0), 0.5, 0.5)));
            Step(sim, Kick(1, new KickIntent(new Vec2(0, 1), 1, 1)));

            Assert.Equal(config.KickWindUpTicks - 1, Player(sim).WindUpTicks);
            Assert.Equal(0, Player(sim).WindUpDirection);
            Assert.Equal(0.5, Player(sim).WindUpStrength);

            UntilContact(sim);
            Step(sim, Kick(sim.Tick, FullKick));
            Assert.Equal(0, Player(sim).WindUpTicks);
        }

        [Fact]
        public void The_line_is_fixed_when_the_intent_arrives_even_if_the_player_turns()
        {
            var config = Config();
            SimState state = Ready(config, ball: new Vec3(1.2, 0, config.BallRadius));
            state.Players[0].Velocity = new Vec2(4, 0);
            var sim = Simulator(config, state);

            Step(sim, Kick(0, KickIntent.AlongHeading(1, 0.3), move: new Vec2(1, 0)));
            for (long t = 1; t <= config.KickWindUpTicks; t++) Step(sim, Move(t, 0, 1));

            Assert.True(Player(sim).Heading > 0.3);
            Assert.Equal(0, Angle(Ball(sim).Velocity), 12);
        }

        // ---- Launch -------------------------------------------------------------------------------

        [Fact]
        public void The_launch_follows_strength_kick_power_and_loft()
        {
            var config = Config();
            PlayerCapabilities capabilities = Exact;
            capabilities.KickPower = 0.8;
            var sim = Simulator(config, Ready(config, capabilities));
            Step(sim, Kick(0, new KickIntent(new Vec2(1, 1), 0.5, 0.25)));
            UntilContact(sim);

            double speed = 0.5 * 0.8 * config.KickSpeed;
            double elevation = 0.25 * config.KickLoft;
            Vec3 v = Ball(sim).Velocity;
            Assert.Equal(speed * Math.Cos(elevation), Horizontal(v), 12);
            Assert.Equal(speed * Math.Sin(elevation) - config.Gravity * config.TickSeconds, v.Z, 12);
            Assert.Equal(Math.PI / 4, Angle(v), 12);
        }

        [Fact]
        public void A_kick_launches_without_spin()
        {
            var config = Config();
            SimState state = Ready(config);
            state.Ball.Spin = new Vec3(3, -4, 20);
            var sim = Simulator(config, state);
            Step(sim, Kick(0, FullKick));
            UntilContact(sim);

            Assert.Equal(Vec3.Zero, Ball(sim).Spin);
        }

        [Fact]
        public void An_explicit_direction_is_kept_instead_of_the_heading()
        {
            var config = Config();
            var sim = Simulator(config, Ready(config));
            Step(sim, Kick(0, new KickIntent(new Vec2(-1, 0.5), 1, 0.3)));
            UntilContact(sim);

            Assert.Equal(Math.Atan2(0.5, -1), Angle(Ball(sim).Velocity), 12);
        }

        [Fact]
        public void Execution_error_is_bounded_by_the_kickers_capability_and_uses_it()
        {
            var config = Config();
            var deviations = new List<double>();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                SimState state = Ready(config, PlayerCapabilities.Reference);
                state.Seed = seed;
                var sim = Simulator(config, state);
                Step(sim, Kick(0, FullKick));
                UntilContact(sim);
                deviations.Add(Angle(Ball(sim).Velocity));
            }

            double error = PlayerCapabilities.Reference.KickError;
            Assert.All(deviations, d => Assert.InRange(d, -error - Close, error + Close));
            Assert.True(deviations.Max() > 0.8 * error && deviations.Min() < -0.8 * error);
        }

        [Fact]
        public void Kick_power_and_error_each_make_an_observable_deterministic_difference()
        {
            var config = Config();
            ulong Launch(PlayerCapabilities capabilities)
            {
                var sim = Simulator(config, Ready(config, capabilities));
                Step(sim, Kick(0, FullKick));
                UntilContact(sim);
                return sim.StateHash();
            }

            PlayerCapabilities weaker = PlayerCapabilities.Reference, wilder = PlayerCapabilities.Reference;
            weaker.KickPower = 0.9;
            wilder.KickError = 0.06;

            ulong reference = Launch(PlayerCapabilities.Reference);
            Assert.Equal(reference, Launch(PlayerCapabilities.Reference));

            // Compare the ball alone: the capabilities themselves are in the state hash.
            double Speed(PlayerCapabilities c)
            {
                var sim = Simulator(config, Ready(config, c));
                Step(sim, Kick(0, FullKick));
                UntilContact(sim);
                return Horizontal(Ball(sim).Velocity);
            }

            double Direction(PlayerCapabilities c)
            {
                var sim = Simulator(config, Ready(config, c));
                Step(sim, Kick(0, FullKick));
                UntilContact(sim);
                return Angle(Ball(sim).Velocity);
            }

            Assert.True(Speed(weaker) < Speed(PlayerCapabilities.Reference));
            Assert.NotEqual(Direction(PlayerCapabilities.Reference), Direction(wilder));
        }

        [Fact]
        public void A_manual_kick_is_never_turned_towards_a_teammate()
        {
            var config = Config();
            BallState Launch(Vec2 teammate)
            {
                SimState state = Ready(config, PlayerCapabilities.Reference);
                state.Players = new[]
                {
                    state.Players[0],
                    new PlayerState { Id = 2, OwnerControllerId = 8, Capabilities = PlayerCapabilities.Reference, Position = teammate },
                };
                var sim = Simulator(config, state);
                Step(sim, Kick(0, new KickIntent(new Vec2(1, 0), 0.6, 0)));
                UntilContact(sim);
                return Ball(sim);
            }

            BallState alone = Launch(new Vec2(0, 300));
            foreach (Vec2 teammate in new[] { new Vec2(10, 3), new Vec2(10, -3), new Vec2(4, 1.5), new Vec2(20, 0.5) })
            {
                BallState passed = Launch(teammate);
                Assert.Equal(alone.Velocity, passed.Velocity);
                Assert.Equal(alone.Position, passed.Position);
            }

            Assert.InRange(Angle(alone.Velocity), -PlayerCapabilities.Reference.KickError, PlayerCapabilities.Reference.KickError);
        }

        // ---- Aftertouch ---------------------------------------------------------------------------

        [Fact]
        public void Sideways_aftertouch_turns_a_ground_pass_by_the_bend_without_changing_its_speed()
        {
            var config = Config();
            var pass = KickIntent.AlongHeading(0.6, 0);
            int ticks = config.KickWindUpTicks + config.AftertouchTicks + 1;
            var plain = new List<Vec3>();
            var bent = new List<Vec3>();
            KickAndShape(config, pass, Vec2.Zero, ticks, s => plain.Add(Ball(s).Velocity));
            KickAndShape(config, pass, new Vec2(0, 1), ticks, s => bent.Add(Ball(s).Velocity));

            for (int i = 0; i < ticks; i++) Assert.Equal(Horizontal(plain[i]), Horizontal(bent[i]), 12);
            Assert.Equal(config.AftertouchBend, Angle(bent[ticks - 1]), 12);
            Assert.Equal(0, Angle(plain[ticks - 1]));
        }

        [Fact]
        public void Aftertouch_weights_decay_linearly_and_sum_to_one()
        {
            var config = Config();
            var angles = new List<double>();
            int ticks = config.KickWindUpTicks + config.AftertouchTicks + 3;
            KickAndShape(config, KickIntent.AlongHeading(0.6, 0), new Vec2(0, 1), ticks, s => angles.Add(Angle(Ball(s).Velocity)));

            int a = config.AftertouchTicks;
            int contact = config.KickWindUpTicks;
            for (int k = 0; k < a; k++)
            {
                double turn = angles[contact + 1 + k] - angles[contact + k];
                Assert.Equal(config.AftertouchBend * 2.0 * (a - k) / (a * (a + 1)), turn, 12);
            }

            // Friction rescales each component separately, so the line can move by an ulp, no more.
            Assert.Equal(angles[contact + a], angles[ticks - 1], 15);
        }

        [Fact]
        public void Sideways_aftertouch_adds_up_to_the_configs_sidespin()
        {
            SimConfig config = Config().With("airSpinDecay", 0);
            int ticks = config.KickWindUpTicks + config.AftertouchTicks + 1;
            Simulator left = KickAndShape(config, FullKick, new Vec2(0, 1), ticks);
            Simulator right = KickAndShape(config, FullKick, new Vec2(0, -0.5), ticks);

            Assert.Equal(config.KickSideSpin, Ball(left).Spin.Z, 12);
            Assert.Equal(-0.5 * config.KickSideSpin, Ball(right).Spin.Z, 12);
        }

        [Fact]
        public void Back_aftertouch_lifts_and_forward_aftertouch_drives()
        {
            var config = Config();
            int ticks = config.KickWindUpTicks + config.AftertouchTicks + 1;
            double plain = Ball(KickAndShape(config, FullKick, Vec2.Zero, ticks)).Velocity.Z;
            double back = Ball(KickAndShape(config, FullKick, new Vec2(-1, 0), ticks)).Velocity.Z;
            double forward = Ball(KickAndShape(config, FullKick, new Vec2(1, 0), ticks)).Velocity.Z;

            Assert.Equal(plain + config.AftertouchLift, back, 12);
            Assert.Equal(plain - config.AftertouchLift, forward, 12);
        }

        [Fact]
        public void After_the_window_the_same_input_no_longer_changes_the_ball()
        {
            var config = Config();
            int window = config.KickWindUpTicks + config.AftertouchTicks;
            Simulator Run(Func<long, Vec2> aftertouch)
            {
                var sim = Simulator(config, Ready(config));
                Step(sim, Kick(0, FullKick));
                for (long t = 1; t < 240; t++)
                {
                    Vec2 a = aftertouch(t);
                    Step(sim, Aftertouch(t, a.X, a.Y));
                }

                return sim;
            }

            Simulator windowOnly = Run(t => t <= window ? new Vec2(-0.6, 0.8) : Vec2.Zero);
            Simulator heldOn = Run(t => new Vec2(-0.6, 0.8));
            Simulator none = Run(t => Vec2.Zero);
            Simulator lateOnly = Run(t => t > window ? new Vec2(-0.6, 0.8) : Vec2.Zero);

            Assert.Equal(windowOnly.StateHash(), heldOn.StateHash());
            Assert.Equal(none.StateHash(), lateOnly.StateHash());
            Assert.NotEqual(none.StateHash(), windowOnly.StateHash());
        }

        [Fact]
        public void Only_the_kicker_shapes_the_ball_and_a_new_contact_closes_the_window()
        {
            var config = Config();
            SimState state = Ready(config, ball: new Vec3(0.3, 0, config.BallRadius));
            state.Players = new[]
            {
                state.Players[0],
                new PlayerState { Id = 2, OwnerControllerId = 8, Capabilities = Exact, Position = new Vec2(0.6, 0) },
            };
            var sim = Simulator(config, state);
            var soft = KickIntent.AlongHeading(0.05, 0);
            sim.Step(new[] { Kick(0, soft) }, new List<SimEvent>());
            sim.Step(new[] { Kick(1, new KickIntent(new Vec2(0, 1), 0.5, 0), controller: 8) }, new List<SimEvent>());
            for (long t = 2; t <= config.KickWindUpTicks + 1; t++) sim.Step(Array.Empty<Command>(), new List<SimEvent>());

            PlayerState first = sim.PeekState().Players[0], second = sim.PeekState().Players[1];
            Assert.Equal(0, first.AftertouchTicks);
            Assert.Equal(config.AftertouchTicks, second.AftertouchTicks);

            SimState before = sim.CopyState();
            sim.Step(new[] { Aftertouch(sim.Tick, 0, 1) }, new List<SimEvent>());
            var unshaped = new Simulator(config, before);
            unshaped.Step(Array.Empty<Command>(), new List<SimEvent>());
            Assert.Equal(unshaped.PeekState().Ball.Velocity, sim.PeekState().Ball.Velocity);
        }

        // ---- Snapshots and validation -------------------------------------------------------------

        private static void Script(long tick, List<Command> into)
        {
            if (tick == 0) into.Add(Kick(0, new KickIntent(new Vec2(1, 0.2), 0.9, 0.4), move: new Vec2(0.3, 0)));
            else if (tick < 40) into.Add(Aftertouch(tick, -0.5, 0.7, move: new Vec2(0.5, 0.1)));
            else into.Add(Move(tick, -0.2, 0.4));
        }

        [Fact]
        public void Snapshot_and_restore_during_wind_up_and_aftertouch_reproduce_the_uninterrupted_run()
        {
            var config = Config();
            const int end = 120;
            var commands = new List<Command>();

            List<ulong> Continue(Simulator sim, List<SimEvent> events)
            {
                var hashes = new List<ulong> { sim.StateHash() };
                while (sim.Tick < end)
                {
                    commands.Clear();
                    Script(sim.Tick, commands);
                    sim.Step(commands, events);
                    hashes.Add(sim.StateHash());
                }

                return hashes;
            }

            var allEvents = new List<SimEvent>();
            List<ulong> uninterrupted = Continue(Simulator(config, Ready(config)), allEvents);
            Assert.Contains(allEvents, e => e.Kind == SimEventKind.Kicked);

            var phases = new HashSet<string>();
            for (int at = 0; at <= config.KickWindUpTicks + config.AftertouchTicks + 2; at++)
            {
                var original = Simulator(config, Ready(config));
                var ignored = new List<SimEvent>();
                while (original.Tick < at)
                {
                    commands.Clear();
                    Script(original.Tick, commands);
                    original.Step(commands, ignored);
                }

                PlayerState p = Player(original);
                if (p.WindUpTicks > 0) phases.Add("wind-up");
                if (p.AftertouchTicks > 0) phases.Add("aftertouch");

                var events = new List<SimEvent>();
                List<ulong> resumed = Continue(Snapshot.Restore(Snapshot.Capture(original), config), events);
                Assert.Equal(uninterrupted.Skip(at), resumed);
                Assert.Equal(allEvents.Where(e => e.Tick >= at), events);
            }

            Assert.Contains("wind-up", phases);
            Assert.Contains("aftertouch", phases);
        }

        [Fact]
        public void The_simulator_refuses_kick_state_the_config_could_not_produce()
        {
            var config = Config();
            void Refused(Action<PlayerState[]> change)
            {
                SimState state = Ready(config);
                change(state.Players);
                Assert.ThrowsAny<ArgumentException>(() => new Simulator(config, state));
            }

            Refused(p => p[0].WindUpTicks = config.KickWindUpTicks + 1);
            Refused(p => p[0].WindUpTicks = -1);
            Refused(p => p[0].AftertouchTicks = config.AftertouchTicks + 1);
            Refused(p => p[0].AftertouchTicks = -1);
            Refused(p => p[0].KickCooldown = config.KickCooldownTicks + 1);
            Refused(p => p[0].KickCooldown = -1);
            Refused(p => p[0].WindUpStrength = 1.5);
            Refused(p => p[0].WindUpLoft = -0.1);
            Refused(p => p[0].Capabilities.KickPower = 0);
            Refused(p => p[0].Capabilities.KickPower = 1.01);
            Refused(p => p[0].Capabilities.KickError = -0.01);

            SimState valid = Ready(config);
            valid.Players[0].WindUpTicks = config.KickWindUpTicks;
            valid.Players[0].AftertouchTicks = config.AftertouchTicks;
            valid.Players[0].KickCooldown = config.KickCooldownTicks;
            _ = new Simulator(config, valid);
        }
    }
}
