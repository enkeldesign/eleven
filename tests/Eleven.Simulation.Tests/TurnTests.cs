using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The turn with the ball (#66, docs/simulation/turn.md): on a sharp change of direction the
    /// footballer traps the ball beside where they plant, then plays it the new way as they come
    /// round. Shared by FREE and ATTACHED. These pin the contract and relationships, not untuned
    /// feel values.
    /// </summary>
    public class TurnTests
    {
        private const int A = TestWorld.Controller;
        private const double Good = 0.9;
        private const double Poor = 0.3;

        private static SimConfig Under(ControlModel model, double? turnStrength = null, double? touchError = null)
        {
            SimConfig config = new SimConfig(controlModel: model);
            if (turnStrength.HasValue) config = config.With("turnStrength", turnStrength.Value);
            if (touchError.HasValue) config = config.With("touchError", touchError.Value);
            return config;
        }

        private static SimState AtFeet(SimConfig config, double ballControl)
        {
            SimState state = TestWorld.State(config, new Vec3(0.6, 0, config.BallRadius));
            state.Players[0].Capabilities = PlayerCapabilities.Reference.With("ballControl", ballControl);
            return state;
        }

        /// <summary>A run along +x, then the stick turned by some angle, kept for the rest of the run.</summary>
        private sealed class Run
        {
            public SimConfig Config = null!;
            public Simulator Sim = null!;

            /// <summary>The state before each step: Before[t] is state t.</summary>
            public readonly List<SimState> Before = new List<SimState>();

            /// <summary>The events each step emitted: Events[t] is step t's.</summary>
            public readonly List<List<SimEvent>> Events = new List<List<SimEvent>>();

            public readonly List<ulong> Hashes = new List<ulong>();

            /// <summary>The step on which the stick turns.</summary>
            public int TurnAt;

            /// <summary>Where the stick asks to go after the turn, unit length.</summary>
            public Vec2 Direction;

            /// <summary>The state after step t.</summary>
            public SimState After(int t) => t + 1 < Before.Count ? Before[t + 1] : Sim.PeekState();

            public IEnumerable<int> Turned(int other) =>
                Enumerable.Range(0, Events.Count).Where(t => Events[t].Any(e => e.Kind == SimEventKind.Turned && e.Subject == TestWorld.PlayerId && e.Other == other));

            /// <summary>The first step after which the player moves the stick's way, within 90° of it.</summary>
            public int Reversed()
            {
                for (int t = TurnAt; t < Events.Count; t++)
                {
                    Vec2 v = After(t).Players[0].Velocity;
                    if (v.X * Direction.X + v.Y * Direction.Y > 0) return t;
                }

                return -1;
            }

            /// <summary>Whether the player kept control from the turn on: within reach under FREE, attached under ATTACHED.</summary>
            public bool Kept() => Enumerable.Range(TurnAt, Events.Count - TurnAt).All(t =>
                Config.ControlModel == ControlModel.Attached ? After(t).Ball.AttachedTo == TestWorld.PlayerId : Separation(After(t)) <= Config.ControlReach);

            /// <summary>The ball's offset from the player along the new direction: + ahead, − behind.</summary>
            public double Ahead(SimState s)
            {
                Vec2 offset = s.Ball.Position.Horizontal - s.Players[0].Position;
                return offset.X * Direction.X + offset.Y * Direction.Y;
            }

            /// <summary>Across the new direction.</summary>
            public double Beside(SimState s)
            {
                Vec2 offset = s.Ball.Position.Horizontal - s.Players[0].Position;
                return Math.Abs(offset.Y * Direction.X - offset.X * Direction.Y);
            }

            /// <summary>
            /// From when the player moves the new way until the ball is 0.3 m ahead: the furthest it
            /// is behind them, and for how long it is behind.
            /// </summary>
            public (double metres, double seconds) Trailing()
            {
                double metres = 0, seconds = 0;
                for (int t = Reversed(); t >= 0 && t < Events.Count; t++)
                {
                    double ahead = Ahead(After(t));
                    if (ahead >= 0.3) break;
                    metres = Math.Max(metres, -ahead);
                    if (ahead < 0) seconds += Config.TickSeconds;
                }

                return (metres, seconds);
            }
        }

        private static double Separation(SimState s) => Math.Sqrt((s.Ball.Position.Horizontal - s.Players[0].Position).LengthSquared);

        private static Run Turn(SimConfig config, double ballControl, double degrees, double stick = 1, int phase = 0, double seconds = 3,
            Func<int, KickIntent?>? kick = null)
        {
            double r = degrees * Math.PI / 180;
            var run = new Run
            {
                Config = config,
                Sim = new Simulator(config, AtFeet(config, ballControl)),
                TurnAt = 2 * config.TickRate + phase,
                Direction = new Vec2(Math.Cos(r), Math.Sin(r)),
            };

            int ticks = run.TurnAt + (int)(seconds * config.TickRate);
            for (int t = 0; t < ticks; t++)
            {
                run.Before.Add(run.Sim.PeekState().Clone());
                Vec2 move = (t < run.TurnAt ? new Vec2(1, 0) : run.Direction) * stick;
                var events = new List<SimEvent>();
                run.Sim.Step(new[] { new Command(A, t, move, kick?.Invoke(t)) }, events);
                run.Events.Add(events);
                run.Hashes.Add(run.Sim.StateHash());
            }

            return run;
        }

        public static readonly TheoryData<ControlModel> Models = new TheoryData<ControlModel> { ControlModel.Free, ControlModel.Attached };

        [Fact]
        public void A_reversal_at_a_sprint_traps_the_ball_as_the_turn_starts_at_every_touch_phase()
        {
            SimConfig config = Under(ControlModel.Free);
            for (int phase = 0; phase < config.TouchIntervalTicks; phase++)
            {
                Run run = Turn(config, Good, 180, phase: phase, seconds: 0.5);
                Assert.True(run.Turned(1).FirstOrDefault(-1) == run.TurnAt, $"phase {phase}: the trap came on step {run.Turned(1).FirstOrDefault(-1)}, the turn on {run.TurnAt}");
            }
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void The_touch_out_comes_as_the_player_comes_round(ControlModel model)
        {
            foreach (double degrees in new[] { 135.0, 180.0 })
            {
                Run run = Turn(Under(model), Good, degrees, phase: 3, seconds: 1);
                Assert.Equal(run.TurnAt, run.Turned(1).FirstOrDefault(-1));
                Assert.Equal(run.Reversed(), run.Turned(2).FirstOrDefault(-1));
                Assert.Single(run.Turned(2));
                Assert.False(run.After(run.Reversed()).Players[0].Turning, $"{degrees}°: still turning after the touch out");
                Assert.All(Enumerable.Range(run.TurnAt, run.Reversed() - run.TurnAt), t => Assert.True(run.After(t).Players[0].Turning, $"{degrees}°, step {t}: not turning before the touch out"));
            }
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void With_the_turn_the_ball_trails_less_and_for_less_time(ControlModel model)
        {
            foreach (double stick in new[] { 0.5, 1.0 })
            {
                // On a touch step, so FREE keeps the ball without the turn too.
                Run off = Turn(Under(model, turnStrength: 0), Good, 180, stick);
                Run on = Turn(Under(model), Good, 180, stick);
                Assert.True(off.Kept() && on.Kept(), $"stick {stick}: kept without the turn {off.Kept()}, with it {on.Kept()}");

                (double offMetres, double offSeconds) = off.Trailing();
                (double onMetres, double onSeconds) = on.Trailing();
                Assert.True(onMetres <= offMetres / 2, $"stick {stick}: trails {onMetres:F2} m with the turn, {offMetres:F2} m without");
                Assert.True(onSeconds <= offSeconds / 2, $"stick {stick}: behind {onSeconds:F2} s with the turn, {offSeconds:F2} s without");
            }
        }

        [Fact]
        public void Good_control_keeps_a_sprint_reversal_from_every_touch_phase_and_poor_control_from_fewer()
        {
            SimConfig config = Under(ControlModel.Free);
            int Kept(double control) => Enumerable.Range(0, config.TouchIntervalTicks).Count(phase => Turn(config, control, 180, phase: phase).Kept());

            int good = Kept(Good), poor = Kept(Poor);
            Assert.True(good == config.TouchIntervalTicks, $"control {Good} kept {good} of {config.TouchIntervalTicks}");
            Assert.True(poor <= config.TouchIntervalTicks / 2, $"control {Poor} kept {poor} of {config.TouchIntervalTicks}");
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void Under_both_models_better_control_trails_less_on_a_reversal(ControlModel model)
        {
            double Trails(double control) => Turn(Under(model), control, 180, 0.5).Trailing().metres;
            double good = Trails(Good), poor = Trails(Poor);
            Assert.True(poor > good, $"control {Poor} trails {poor:F2} m, control {Good} {good:F2} m");
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void Each_turn_touch_is_bounded_and_the_ball_moves_only_by_its_velocity(ControlModel model)
        {
            // No execution error, so the bound is the turn's strength alone.
            SimConfig config = Under(model, touchError: 0);
            double rolling = config.RollingFriction * config.Gravity * config.TickSeconds;
            foreach (double control in new[] { Good, 0.7, Poor })
            foreach (double degrees in new[] { 135.0, 180.0 })
            foreach (int phase in new[] { 0, 5, 10 })
            {
                Run run = Turn(config, control, degrees, phase: phase, seconds: 1.5);
                Assert.True(run.Turned(1).Any(), $"control {control}, {degrees}°, phase {phase}: no trap");
                double cap = config.TurnStrength * (0.5 + 0.5 * control) + rolling + 1e-9;
                for (int t = 0; t < run.Events.Count; t++)
                {
                    SimState before = run.Before[t], after = run.After(t);
                    Vec3 moved = after.Ball.Position - before.Ball.Position;
                    double stepped = Math.Sqrt(after.Ball.Velocity.LengthSquared) * config.TickSeconds;
                    Assert.True(Math.Sqrt(moved.LengthSquared) <= stepped + 1e-9, $"step {t}: the ball moved {Math.Sqrt(moved.LengthSquared):F4} m on a {stepped:F4} m step");

                    if (!run.Events[t].Any(e => e.Kind == SimEventKind.Turned)) continue;
                    double change = Math.Sqrt((after.Ball.Velocity.Horizontal - before.Ball.Velocity.Horizontal).LengthSquared);
                    Assert.True(change <= cap, $"control {control}, {degrees}°, phase {phase}, step {t}: a turn touch changed the ball's velocity by {change:F3} m/s, more than {cap:F3}");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void A_good_players_turn_takes_the_ball_round_the_planted_foot(ControlModel model)
        {
            SimConfig config = Under(model);
            foreach (double stick in new[] { 0.5, 1.0 })
            for (int phase = 0; phase < config.TouchIntervalTicks; phase++)
            {
                Run run = Turn(config, Good, 180, stick, phase, 1.5);
                Assert.True(run.Turned(1).Any(), $"stick {stick}, phase {phase}: no trap");
                for (int t = run.TurnAt; t + 1 < run.Events.Count; t++)
                {
                    // Where the ball passes the player, from behind to ahead or back.
                    double was = run.Ahead(run.After(t)), now = run.Ahead(run.After(t + 1));
                    if (Math.Sign(was) == Math.Sign(now)) continue;
                    double beside = run.Beside(run.After(t + 1));
                    Assert.True(beside >= 0.12, $"stick {stick}, phase {phase}, step {t + 1}: the ball passed {beside:F2} m from the player's centre");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void A_change_of_direction_short_of_the_turn_angle_is_ordinary(ControlModel model)
        {
            foreach (double degrees in new[] { 45.0, 90.0 })
            foreach (int phase in new[] { 0, 7 })
            {
                Run on = Turn(Under(model), Good, degrees, phase: phase, seconds: 2);
                Run off = Turn(Under(model, turnStrength: 0), Good, degrees, phase: phase, seconds: 2);
                Assert.Empty(on.Turned(1));
                Assert.Equal(off.Hashes, on.Hashes);
            }
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void Turn_strength_zero_makes_no_turn_touch(ControlModel model)
        {
            Run run = Turn(Under(model, turnStrength: 0), Good, 180, phase: 4);
            Assert.DoesNotContain(run.Events.SelectMany(e => e), e => e.Kind == SimEventKind.Turned);
            Assert.All(run.Before, s => Assert.False(s.Players[0].Turning));
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void No_turn_touch_while_winding_up_and_a_wind_up_ends_the_turn(ControlModel model)
        {
            SimConfig config = Under(model);
            KickIntent flick = KickIntent.AlongHeading(0.6, 0);

            // The same reversal traps the ball without a kick.
            Run plain = Turn(config, Good, 180, phase: 2, seconds: 1);
            Assert.Equal(plain.TurnAt, plain.Turned(1).FirstOrDefault(-1));

            // A kick asked as the stick turns: no turn touch through its wind-up and cooldown.
            Run kicked = Turn(config, Good, 180, phase: 2, seconds: 1, kick: t => t == 2 * config.TickRate + 2 ? flick : (KickIntent?)null);
            int resolved = Enumerable.Range(0, kicked.Events.Count).First(t => kicked.Events[t].Any(e => e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KickMissed));
            int quiet = resolved + config.KickCooldownTicks;
            Assert.DoesNotContain(kicked.Turned(1).Concat(kicked.Turned(2)), t => t >= kicked.TurnAt && t <= quiet);

            // A kick asked mid-turn ends the turn as its wind-up starts.
            int asked = plain.TurnAt + 3;
            Run late = Turn(config, Good, 180, phase: 2, seconds: 1, kick: t => t == asked ? flick : (KickIntent?)null);
            Assert.True(late.After(asked - 1).Players[0].Turning, "not turning before the kick");
            Assert.False(late.After(asked).Players[0].Turning, "still turning once the wind-up started");
            int connected = Enumerable.Range(0, late.Events.Count).First(t => late.Events[t].Any(e => e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KickMissed));
            Assert.DoesNotContain(late.Turned(1).Concat(late.Turned(2)), t => t >= asked && t <= connected + config.KickCooldownTicks);
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void A_turn_is_deterministic_and_survives_snapshot_and_restore(ControlModel model)
        {
            SimConfig config = Under(model);
            Run first = Turn(config, 0.7, 180, phase: 6, seconds: 2);
            Run second = Turn(config, 0.7, 180, phase: 6, seconds: 2);
            Assert.Equal(first.Hashes, second.Hashes);

            // Mid-turn: after the trap, before the touch out.
            int at = first.TurnAt + 3;
            Assert.True(first.Before[at].Players[0].Turning, "not turning at the snapshot");
            var sim = new Simulator(config, AtFeet(config, 0.7));
            for (int t = 0; t < at; t++) sim.Step(new[] { new Command(A, t, t < first.TurnAt ? new Vec2(1, 0) : first.Direction) }, new List<SimEvent>());
            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), config);
            for (int t = at; t < first.Events.Count; t++)
            {
                restored.Step(new[] { new Command(A, t, first.Direction) }, new List<SimEvent>());
                Assert.Equal(first.Hashes[t], restored.StateHash());
            }
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void A_turn_ends_when_the_player_loses_the_ball(ControlModel model)
        {
            // Codex on #73. Lost mid-turn: beyond reach under FREE, released under ATTACHED.
            SimConfig config = Under(model);
            SimState lost = TestWorld.State(config, new Vec3(2, 0, config.BallRadius));
            lost.Players[0].Turning = true;
            if (model == ControlModel.Attached) lost.Ball.AttachedTo = TestWorld.PlayerId;
            var sim = new Simulator(config, lost);
            sim.Step(new[] { new Command(A, 0, Vec2.Zero) }, new List<SimEvent>());
            Assert.False(sim.PeekState().Players[0].Turning, "still turning after losing the ball");

            // So the next possession is an ordinary one: a ball rolled at a standing player left
            // mid-turn is taken exactly as if they had never turned.
            SimState Rolled(bool turning)
            {
                SimState s = TestWorld.State(config, new Vec3(3, 0, config.BallRadius), new Vec3(-3, 0, 0));
                s.Players[0].Turning = turning;
                return s;
            }

            var stale = new Simulator(config, Rolled(true));
            var fresh = new Simulator(config, Rolled(false));
            int controls = 0;
            for (int t = 0; t < 2 * config.TickRate; t++)
            {
                var events = new List<SimEvent>();
                stale.Step(new[] { new Command(A, t, Vec2.Zero) }, events);
                fresh.Step(new[] { new Command(A, t, Vec2.Zero) }, new List<SimEvent>());
                controls += events.Count(e => e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Attached);
                BallState a = stale.PeekState().Ball, b = fresh.PeekState().Ball;
                Assert.True(a.Position.X == b.Position.X && a.Position.Y == b.Position.Y && a.Velocity.X == b.Velocity.X && a.Velocity.Y == b.Velocity.Y,
                    $"step {t}: left mid-turn the ball is at ({a.Position.X:F3}, {a.Position.Y:F3}), never turning ({b.Position.X:F3}, {b.Position.Y:F3})");
            }

            Assert.True(controls > 0, "the rolled ball was never controlled");
        }

        [Fact]
        public void The_turn_flag_round_trips_through_the_encoding_and_a_bad_one_is_refused()
        {
            SimConfig config = Under(ControlModel.Free);
            SimState state = AtFeet(config, Good);
            state.Players[0].Turning = true;
            Simulator restored = Snapshot.Restore(Snapshot.Capture(new Simulator(config, state)), config);
            Assert.True(restored.PeekState().Players[0].Turning);

            var w = new Encoding.CanonicalWriter();
            state.Encode(w);
            byte[] bytes = w.ToArray();

            // The flag is the last byte of the last player, just before the keeper count.
            int flag = bytes.Length - 4 - 1;
            Assert.Equal(1, bytes[flag]);
            bytes[flag] = 2;
            Assert.Throws<FormatException>(() => SimState.Decode(new Encoding.CanonicalReader(bytes)));
        }
    }
}
