using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Tests
{
    /// <summary>Small hand-built worlds for rule tests.</summary>
    internal static class TestWorld
    {
        public const int Controller = 7;
        public const int DotId = 1;
        public const int BallId = 100;

        public static SimConfig Config(MathBackend backend = MathBackend.Portable) => new SimConfig(mathBackend: backend);

        public static SimState State(SimConfig config, Vec3? ballPosition = null, Vec3? ballVelocity = null, Vec3? ballSpin = null,
            Vec2? dotPosition = null) => new SimState
        {
            Seed = 99,
            Ball = new BallState
            {
                Id = BallId,
                Position = ballPosition ?? new Vec3(0, 20, config.BallRadius),
                Velocity = ballVelocity ?? Vec3.Zero,
                Spin = ballSpin ?? Vec3.Zero,
            },
            Dots = new[] { new DotState { Id = DotId, OwnerControllerId = Controller, Position = dotPosition ?? Vec2.Zero } },
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

        public static Command Move(long tick, double x, double y, bool kick = false, int controller = Controller) =>
            new Command(controller, tick, new Vec2(x, y), kick);
    }
}
