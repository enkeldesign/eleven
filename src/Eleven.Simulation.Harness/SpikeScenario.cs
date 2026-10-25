using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The scripted 60-second spike scenario: fixed seed, fixed initial state, fixed command
    /// sequence. The dot runs onto a resting ball and kicks it towards the goal line; the ball
    /// flies, curves, bounces, crosses the line and rolls to a stop. The dot runs past it, turns
    /// and kicks it back, so the ball recrosses the line in the other direction and stops.
    ///
    /// The script was written against these placeholder physics values; if they change, the
    /// scenario's behavioural assertions say whether it still exercises everything.
    ///
    /// The commands are a function of the tick alone, never of state, so every runtime replays
    /// exactly the same input. Bump <see cref="Version"/> whenever the script, initial state or
    /// seed changes.
    /// </summary>
    public static class SpikeScenario
    {
        public const string Id = "spike-1";
        public const int Version = 1;
        public const ulong Seed = 0x0E1E7E2026UL;
        public const int ControllerId = 1;
        public const int DotId = 1;
        public const int BallId = 100;
        public const int DurationSeconds = 60;

        public static string Name => $"{Id}/v{Version}";

        public static SimConfig Config(MathBackend backend) => new SimConfig(mathBackend: backend);

        public static int DurationTicks(SimConfig config) => DurationSeconds * config.TickRate;

        public static SimState InitialState(SimConfig config) => new SimState
        {
            Tick = 0,
            Seed = Seed,
            Ball = new BallState
            {
                Id = BallId,
                Position = new Vec3(20, 1, config.BallRadius),
            },
            Dots = new[]
            {
                new DotState { Id = DotId, OwnerControllerId = ControllerId, Position = new Vec2(10, 0) },
            },
        };

        // (first tick, move x, move y, kick) — each segment lasts until the next one starts.
        private static readonly Segment[] Script =
        {
            new Segment(0, 1.0, 0.1, false),      // run at the ball
            new Segment(84, 1.0, 0.1, true),      // kick held: connects when in reach
            new Segment(110, 0.0, 0.0, false),    // stop and watch the ball fly
            new Segment(240, 1.0, 0.1664, false), // run to a point past the ball's resting place
            new Segment(790, 0.0, 0.0, false),    // stop
            new Segment(840, -1.0, 0.0, true),    // run back at the ball, kick held
            new Segment(960, 0.0, 0.0, false),    // stand still to the end
        };

        /// <summary>The commands for <paramref name="tick"/>, appended to <paramref name="into"/>.</summary>
        public static void CommandsFor(long tick, List<Command> into)
        {
            Segment current = Script[0];
            foreach (Segment s in Script)
            {
                if (s.FirstTick <= tick) current = s;
            }

            into.Add(new Command(ControllerId, tick, new Vec2(current.MoveX, current.MoveY), current.Kick));
        }

        private readonly struct Segment
        {
            public readonly long FirstTick;
            public readonly double MoveX;
            public readonly double MoveY;
            public readonly bool Kick;

            public Segment(long firstTick, double moveX, double moveY, bool kick)
            {
                FirstTick = firstTick;
                MoveX = moveX;
                MoveY = moveY;
                Kick = kick;
            }
        }
    }
}
