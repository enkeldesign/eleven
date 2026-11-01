using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The scripted 60-second spike scenario: fixed seed, fixed initial state, fixed command
    /// sequence. The player runs at a resting ball and starts a kick too early, so the wind-up ends
    /// out of reach and misses. The next wind-up connects: a kick aimed along an explicit line
    /// towards the goal line, bent left by sideways aftertouch. The ball flies, curves on after the
    /// window closes, bounces, crosses the line and rolls to a stop. The player runs past it, turns
    /// and kicks it back along the heading, lifted by back aftertouch, so the ball recrosses the
    /// line in the other direction and stops.
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
        public const int Version = 3;
        public const ulong Seed = 0x0E1E7E2026UL;
        public const int ControllerId = 1;
        public const int PlayerId = 1;
        public const int BallId = 100;
        public const int DurationSeconds = 60;

        public static string Name => $"{Id}/v{Version}";

        public static SimConfig Config() => new SimConfig();

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
            Players = new[]
            {
                new PlayerState { Id = PlayerId, OwnerControllerId = ControllerId, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(10, 0) },
            },
        };

        // Both kicks rise at the spike's old elevation, 0.45 rad, as a fraction of the default maximum loft.
        private const double Loft = 0.45 / 1.05;

        // Towards the goal line along an explicit line, a little left of the run.
        private static readonly KickIntent Aimed = new KickIntent(new Vec2(1, 0.05), 1, Loft);

        // Back along the heading.
        private static readonly KickIntent Return = KickIntent.AlongHeading(1, Loft);

        // Each segment lasts until the next one starts.
        private static readonly Segment[] Script =
        {
            new Segment(0, 1.0, 0.1),                         // run at the ball
            new Segment(76, 1.0, 0.1, Aimed),                 // kick held: the first wind-up ends out of reach, the next connects
            new Segment(89, 1.0, 0.1, null, 0, 1),            // sideways aftertouch: bend it left
            new Segment(110, 0.0, 0.0),                       // stop and watch the ball fly
            new Segment(240, 1.0, 0.205),                     // run to a point past the ball's resting place
            new Segment(790, 0.0, 0.0),                       // stop
            new Segment(840, -1.0, 0.0, Return, 1, 0),        // run back at the ball, kick held, back aftertouch to lift it
            new Segment(960, 0.0, 0.0),                       // stand still to the end
        };

        /// <summary>The commands for <paramref name="tick"/>, appended to <paramref name="into"/>.</summary>
        public static void CommandsFor(long tick, List<Command> into)
        {
            Segment current = Script[0];
            foreach (Segment s in Script)
            {
                if (s.FirstTick <= tick) current = s;
            }

            into.Add(new Command(ControllerId, tick, current.Move, current.Kick, current.Aftertouch));
        }

        private readonly struct Segment
        {
            public readonly long FirstTick;
            public readonly Vec2 Move;
            public readonly KickIntent? Kick;
            public readonly Vec2 Aftertouch;

            public Segment(long firstTick, double moveX, double moveY, KickIntent? kick = null, double aftertouchX = 0, double aftertouchY = 0)
            {
                FirstTick = firstTick;
                Move = new Vec2(moveX, moveY);
                Kick = kick;
                Aftertouch = new Vec2(aftertouchX, aftertouchY);
            }
        }
    }
}
