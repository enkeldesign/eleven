using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#36: each player moves by their own capabilities, deterministically.</summary>
    public class MovementTests
    {
        private const int First = 1, Second = 2; // player ids, and the ids of the controllers that own them

        private static PlayerCapabilities Profile(double maxSpeed, double acceleration) =>
            new PlayerCapabilities { MaxSpeed = maxSpeed, Acceleration = acceleration, KickPower = 1, KickError = 0.05 };

        // Two players well apart and far from the ball, so nothing but movement happens.
        private static SimState TwoPlayers(SimConfig config, PlayerCapabilities first, PlayerCapabilities second,
            Vec2? firstVelocity = null, Vec2? secondVelocity = null) => new SimState
        {
            Seed = 5,
            Ball = new BallState { Id = 100, Position = new Vec3(0, 500, config.BallRadius) },
            Players = new[]
            {
                new PlayerState { Id = First, OwnerControllerId = First, Capabilities = first, Velocity = firstVelocity ?? Vec2.Zero },
                new PlayerState { Id = Second, OwnerControllerId = Second, Capabilities = second, Position = new Vec2(0, -20), Velocity = secondVelocity ?? Vec2.Zero },
            },
        };

        // Steps both players with the same stick for `ticks` ticks.
        private static Simulator Run(SimConfig config, SimState state, int ticks, Vec2 stick)
        {
            var sim = new Simulator(config, state);
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                sim.Step(new[] { new Command(First, sim.Tick, stick), new Command(Second, sim.Tick, stick) }, events);
            }

            return sim;
        }

        private static double Speed(PlayerState p) => Math.Sqrt(p.Velocity.LengthSquared);

        private static PlayerState Player(Simulator sim, int id) => sim.PeekState().Players.Single(p => p.Id == id);

        // ---- Capabilities shape movement --------------------------------------------------------

        [Fact]
        public void Top_speed_is_each_players_own()
        {
            var config = new SimConfig();
            Simulator sim = Run(config, TwoPlayers(config, Profile(7, 25), Profile(10, 25)), 120, new Vec2(1, 0));

            Assert.Equal(7, Speed(Player(sim, First)), 9);
            Assert.Equal(10, Speed(Player(sim, Second)), 9);
        }

        [Fact]
        public void Acceleration_is_each_players_own()
        {
            var config = new SimConfig();
            Simulator sim = Run(config, TwoPlayers(config, Profile(10, 12), Profile(10, 30)), 12, new Vec2(1, 0));

            // 0.2 s of full stick from rest: a·t, below either top speed.
            Assert.Equal(12 * 0.2, Speed(Player(sim, First)), 9);
            Assert.Equal(30 * 0.2, Speed(Player(sim, Second)), 9);
        }

        [Fact]
        public void Braking_and_reversing_use_the_players_acceleration()
        {
            var config = new SimConfig();
            SimState start = TwoPlayers(config, Profile(9, 12), Profile(9, 30), new Vec2(9, 0), new Vec2(9, 0));

            // Released stick: speed falls by a·t.
            Simulator braking = Run(config, start, 10, Vec2.Zero);
            Assert.Equal(9 - 12 / 6.0, Speed(Player(braking, First)), 9);
            Assert.Equal(9 - 30 / 6.0, Speed(Player(braking, Second)), 9);

            // Full stick the other way: through a stop to full speed back, in 2·v/a.
            foreach ((int id, double acceleration) in new[] { (First, 12.0), (Second, 30.0) })
            {
                int ticks = (int)Math.Ceiling(2 * 9 / acceleration * config.TickRate - 1e-9);
                Simulator before = Run(config, start, ticks - 1, new Vec2(-1, 0));
                Simulator after = Run(config, start, ticks, new Vec2(-1, 0));
                Assert.True(Player(before, id).Velocity.X > -9 + 1e-9, $"player {id} reversed early");
                Assert.Equal(-9, Player(after, id).Velocity.X, 9);
                Assert.Equal(Math.PI, Player(after, id).Heading, 12);
            }
        }

        [Fact]
        public void A_capability_change_makes_an_observable_deterministic_difference()
        {
            var config = new SimConfig();
            (List<ulong> Hashes, List<Vec2> Path) Run(PlayerCapabilities first)
            {
                var sim = new Simulator(config, TwoPlayers(config, first, PlayerCapabilities.Reference));
                var hashes = new List<ulong> { sim.StateHash() };
                var path = new List<Vec2>();
                var events = new List<SimEvent>();
                for (int i = 0; i < 240; i++)
                {
                    Vec2 stick = i < 120 ? new Vec2(1, 0.25) : new Vec2(-0.5, 1);
                    sim.Step(new[] { new Command(First, sim.Tick, stick), new Command(Second, sim.Tick, stick) }, events);
                    hashes.Add(sim.StateHash());
                    path.Add(Player(sim, First).Position);
                }

                return (hashes, path);
            }

            PlayerCapabilities quicker = PlayerCapabilities.Reference.With("acceleration", 26);
            var reference = Run(PlayerCapabilities.Reference);
            var changed = Run(quicker);

            // Deterministic: the same capabilities give the same run, bit for bit.
            Assert.Equal(reference.Hashes, Run(PlayerCapabilities.Reference).Hashes);
            Assert.Equal(changed.Hashes, Run(quicker).Hashes);

            // Observable: one m/s² more takes the player somewhere else, from the first tick on.
            Assert.NotEqual(reference.Path[0], changed.Path[0]);
            Assert.True(Math.Sqrt((reference.Path.Last() - changed.Path.Last()).LengthSquared) > 0.01);
        }

        // ---- The capability surface ---------------------------------------------------------------

        [Fact]
        public void Capabilities_outside_their_valid_range_are_refused()
        {
            var config = new SimConfig();
            foreach (TunableParameter p in PlayerCapabilities.Parameters)
            {
                foreach (double value in new[] { p.Min - 1e-9, p.Max + 1e-9 })
                {
                    SimState state = TwoPlayers(config, PlayerCapabilities.Reference.With(p.Name, value), PlayerCapabilities.Reference);
                    var error = Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(config, state));
                    Assert.Equal(p.Name, error.ParamName);
                }

                foreach (double value in new[] { p.Min, p.Max })
                {
                    _ = new Simulator(config, TwoPlayers(config, PlayerCapabilities.Reference.With(p.Name, value), PlayerCapabilities.Reference));
                }
            }

            // A player built without capabilities can't move, so it's refused rather than silently frozen.
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(config, TwoPlayers(config, default, PlayerCapabilities.Reference)));

            SimState nan = TwoPlayers(config, PlayerCapabilities.Reference.With("maxSpeed", double.NaN), PlayerCapabilities.Reference);
            Assert.Contains("Player.Capabilities.MaxSpeed", Assert.Throws<InvalidOperationException>(() => new Simulator(config, nan)).Message);
        }

        [Fact]
        public void Starting_ranges_lie_within_the_valid_ranges_and_hold_the_reference_profile()
        {
            Assert.Equal(new[] { "maxSpeed", "acceleration", "kickPower", "kickError", "passVision", "passAnticipation" }, PlayerCapabilities.Parameters.Select(p => p.Name));
            foreach (TunableParameter p in PlayerCapabilities.Parameters.Skip(2))
            {
                // #26 gives no research range for kick power, kick error (#37) or the pass's
                // perception (#38): technical references only.
                Assert.Null(p.StartMin);
                Assert.Null(p.StartMax);
                Assert.InRange(PlayerCapabilities.Reference.Get(p.Name), p.Min, p.Max);
            }

            foreach (TunableParameter p in PlayerCapabilities.Parameters.Take(2))
            {
                Assert.NotNull(p.StartMin);
                Assert.NotNull(p.StartMax);
                Assert.InRange(p.StartMin!.Value, p.Min, p.StartMax!.Value);
                Assert.InRange(p.StartMax.Value, p.StartMin.Value, p.Max);
                Assert.InRange(PlayerCapabilities.Reference.Get(p.Name), p.StartMin.Value, p.StartMax.Value);
            }

            // The research starting ranges on #26.
            Assert.Equal((7.0, 10.0), (PlayerCapabilities.Parameters[0].StartMin!.Value, PlayerCapabilities.Parameters[0].StartMax!.Value));
            Assert.Equal((12.0, 30.0), (PlayerCapabilities.Parameters[1].StartMin!.Value, PlayerCapabilities.Parameters[1].StartMax!.Value));
        }

        [Fact]
        public void Capabilities_are_read_and_changed_by_name()
        {
            PlayerCapabilities p = PlayerCapabilities.Reference.With("maxSpeed", 9).With("acceleration", 14);

            Assert.Equal(9, p.Get("maxSpeed"));
            Assert.Equal(14, p.Get("acceleration"));
            Assert.Equal(7, PlayerCapabilities.Reference.MaxSpeed);
            Assert.Throws<ArgumentException>(() => p.Get("pace"));
            Assert.Throws<ArgumentException>(() => p.With("pace", 1));
        }

        // ---- Snapshot and replay ------------------------------------------------------------------

        // Two contrasting players: accelerate, turn 90°, reverse, release. Tick-only, never state-dependent.
        private static void Script(long tick, List<Command> into)
        {
            Vec2 stick = tick < 60 ? new Vec2(1, 0)
                : tick < 150 ? new Vec2(0, 1)
                : tick < 240 ? new Vec2(0, -1)
                : Vec2.Zero;
            into.Add(new Command(First, tick, stick));
            into.Add(new Command(Second, tick, stick * 0.6));
        }

        private static SimState Contrasting(SimConfig config) => TwoPlayers(config, Profile(7, 12), Profile(10, 30));

        private static (List<ulong> Hashes, List<SimEvent> Events) Continue(Simulator sim, long endTick)
        {
            var hashes = new List<ulong> { sim.StateHash() };
            var events = new List<SimEvent>();
            var commands = new List<Command>();
            while (sim.Tick < endTick)
            {
                commands.Clear();
                Script(sim.Tick, commands);
                sim.Step(commands, events);
                hashes.Add(sim.StateHash());
            }

            return (hashes, events);
        }

        [Fact]
        public void Restoring_mid_movement_continues_the_uninterrupted_run()
        {
            var config = new SimConfig();
            var (uninterrupted, _) = Continue(new Simulator(config, Contrasting(config)), 300);

            // Accelerating, mid-turn, braking through the reversal, slowing to a stop.
            foreach (long at in new long[] { 5, 100, 165, 250 })
            {
                var original = new Simulator(config, Contrasting(config));
                Continue(original, at);
                Assert.True(Speed(Player(original, First)) > 0 && Speed(Player(original, Second)) > 0, $"tick {at}: not moving");

                var (resumed, _) = Continue(Snapshot.Restore(Snapshot.Capture(original), config), 300);
                Assert.Equal(uninterrupted.Skip((int)at), resumed);
            }
        }

        [Fact]
        public void A_recorded_session_replays_tick_for_tick()
        {
            var config = new SimConfig();
            var live = new Simulator(config, Contrasting(config));
            CommandRecording recording = CommandRecording.StartFrom(live);
            var liveHashes = new List<ulong> { live.StateHash() };
            var liveEvents = new List<SimEvent>();
            var commands = new List<Command>();
            for (int i = 0; i < 300; i++)
            {
                commands.Clear();
                Script(live.Tick, commands);
                if (i % 40 == 0) commands.Add(new Command(99, live.Tick, new Vec2(1, 0))); // unowned
                recording.Record(commands);
                live.Step(commands, liveEvents);
                liveHashes.Add(live.StateHash());
            }

            var replayHashes = new List<ulong>();
            var replayEvents = new List<SimEvent>();
            CommandRecording.Decode(recording.Encode()).Replay(config, replayEvents, replayHashes);

            Assert.Equal(liveHashes, replayHashes);
            Assert.Equal(liveEvents, replayEvents);
            Assert.Contains(liveEvents, e => e.Rejection == CommandRejection.Unowned);
        }

        [Fact]
        public void Capabilities_survive_the_state_encoding()
        {
            var config = new SimConfig();
            SimState state = Contrasting(config);
            var w = new CanonicalWriter();
            state.Encode(w);

            SimState decoded = SimState.Decode(new CanonicalReader(w.ToArray()));
            Assert.Equal(Profile(7, 12), decoded.Players[0].Capabilities);
            Assert.Equal(Profile(10, 30), decoded.Players[1].Capabilities);
        }
    }
}
