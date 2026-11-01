using System.Collections.Generic;

namespace Eleven.Simulation.Tests
{
    /// <summary>Small hand-built worlds for rule tests.</summary>
    internal static class TestWorld
    {
        public const int Controller = 7;
        public const int PlayerId = 1;
        public const int BallId = 100;

        public static SimConfig Config() => new SimConfig();

        public static SimState State(SimConfig config, Vec3? ballPosition = null, Vec3? ballVelocity = null, Vec3? ballSpin = null,
            Vec2? playerPosition = null) => new SimState
        {
            Seed = 99,
            Ball = new BallState
            {
                Id = BallId,
                Position = ballPosition ?? new Vec3(0, 20, config.BallRadius),
                Velocity = ballVelocity ?? Vec3.Zero,
                Spin = ballSpin ?? Vec3.Zero,
            },
            Players = new[] { new PlayerState { Id = PlayerId, OwnerControllerId = Controller, Capabilities = PlayerCapabilities.Reference, Position = playerPosition ?? Vec2.Zero } },
        };

        public static Simulator Simulator(SimConfig? config = null, SimState? state = null)
        {
            config ??= Config();
            return new Simulator(config, state ?? State(config));
        }

        public static List<SimEvent> Step(Simulator sim, params Command[] commands)
        {
            var events = new List<SimEvent>();
            sim.Step(commands, events);
            return events;
        }

        /// <summary>A full-strength kick along the heading, at the spike's old elevation (0.45 rad of the default 1.05).</summary>
        public static readonly KickIntent FullKick = KickIntent.AlongHeading(1, 0.45 / 1.05);

        public static Command Move(long tick, double x, double y, bool kick = false, int controller = Controller) =>
            new Command(controller, tick, new Vec2(x, y), kick ? FullKick : (KickIntent?)null);

        public static Command Kick(long tick, KickIntent intent, Vec2 move = default, Vec2 aftertouch = default, int controller = Controller) =>
            new Command(controller, tick, move, intent, aftertouch);

        public static Command Aftertouch(long tick, double x, double y, Vec2 move = default, int controller = Controller) =>
            new Command(controller, tick, move, null, new Vec2(x, y));
    }
}
