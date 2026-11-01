// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One inert practice receiver: where they start and the stick their controller holds throughout.</summary>
    public readonly struct PracticeReceiver
    {
        public PracticeReceiver(string name, Vec2 position, Vec2 stick)
        {
            Name = name;
            Position = position;
            Stick = stick;
        }

        public string Name { get; }
        public Vec2 Position { get; }

        /// <summary>Held from the first tick; the receiver starts already running at it, at the reference top speed.</summary>
        public Vec2 Stick { get; }
    }

    /// <summary>What one quick pass in a drill did.</summary>
    public sealed class PassOutcome
    {
        /// <summary>The receiver the footballer chose, or null for a free pass along the heading.</summary>
        public string? Target;

        public int TargetId;

        /// <summary>rad. The pass's intended line, as resolved.</summary>
        public double Line;

        /// <summary>
        /// m. The lead: how far the pass line runs from the receiver's position at the intent. 0 aims
        /// at where they are; more aims into their run.
        /// </summary>
        public double LeadMetres;

        public double LaunchSpeedMps;

        /// <summary>m. The closest the ball came to the receiver's centre, and when (s after the intent).</summary>
        public double ClosestApproachMetres;

        public double ClosestApproachSeconds;

        /// <summary>m/s. The ball's speed at the closest approach.</summary>
        public double ArrivalSpeedMps;
    }

    /// <summary>
    /// The quick-pass practice drill (#38): one controlled player and inert practice receivers on
    /// their team, no team AI. The receivers' own controllers hold fixed sticks, so a runner keeps
    /// running at a constant velocity. The passer stands at the origin facing +x with the ball at
    /// rest just ahead, and asks for a quick pass on tick 0; the simulation does the rest.
    ///
    /// The commands are a function of the tick alone, so every runtime replays the same input.
    /// </summary>
    public static class PassDrill
    {
        public const int PasserId = 1;
        public const int PasserController = 1;
        public const int BallId = 100;
        public const double MaxSeconds = 6;

        /// <summary>
        /// The standard drill: an obvious advancing receiver ahead, a closer one behind, and a near
        /// one wide, standing at about 63° off the passer's heading: just outside the reference
        /// profile's view, and the cheapest pass for anyone who sees them.
        /// </summary>
        public static readonly IReadOnlyList<PracticeReceiver> Standard = new[]
        {
            new PracticeReceiver("ahead-advancing", new Vec2(14, 3), new Vec2(5.0 / 7, 0)),
            new PracticeReceiver("behind-closer", new Vec2(-7, -1), Vec2.Zero),
            new PracticeReceiver("wide", new Vec2(3, 6), Vec2.Zero),
        };

        /// <summary>A single runner crossing the passer's line from left to right: the lead case.</summary>
        public static readonly IReadOnlyList<PracticeReceiver> CrossingRun = new[]
        {
            new PracticeReceiver("crossing-run", new Vec2(14, 8), new Vec2(0, -5.0 / 7)),
        };

        private static int Controller(int index) => 10 + index;

        public static SimState InitialState(SimConfig config, PlayerCapabilities passer, IReadOnlyList<PracticeReceiver> receivers)
        {
            var players = new PlayerState[1 + receivers.Count];
            players[0] = new PlayerState { Id = PasserId, OwnerControllerId = PasserController, Capabilities = passer };
            PlayerCapabilities reference = PlayerCapabilities.Reference;
            for (int i = 0; i < receivers.Count; i++)
            {
                PracticeReceiver r = receivers[i];
                Vec2 velocity = r.Stick * reference.MaxSpeed;
                players[i + 1] = new PlayerState
                {
                    Id = PasserId + 1 + i,
                    OwnerControllerId = Controller(i),
                    Capabilities = reference,
                    Position = r.Position,
                    Velocity = velocity,
                    Heading = velocity.LengthSquared > 0 ? SimMath.Atan2(velocity.Y, velocity.X) : SimMath.Atan2(-r.Position.Y, -r.Position.X),
                };
            }

            return new SimState
            {
                Seed = 0x0E1E7E2038UL,
                Ball = new BallState { Id = BallId, Position = new Vec3(0.5, 0, config.BallRadius) },
                Players = players,
            };
        }

        /// <summary>The commands for <paramref name="tick"/>: the quick pass on tick 0, and every receiver's held stick.</summary>
        public static void CommandsFor(long tick, IReadOnlyList<PracticeReceiver> receivers, List<Command> into)
        {
            into.Add(new Command(PasserController, tick, Vec2.Zero, quickPass: tick == 0));
            for (int i = 0; i < receivers.Count; i++) into.Add(new Command(Controller(i), tick, receivers[i].Stick));
        }

        /// <summary>Runs the drill and measures the pass against the receiver it was meant for.</summary>
        public static PassOutcome Run(SimConfig config, PlayerCapabilities passer, IReadOnlyList<PracticeReceiver> receivers, List<SimEvent>? events = null)
        {
            var sim = new Simulator(config, InitialState(config, passer, receivers));
            events ??= new List<SimEvent>();
            var commands = new List<Command>();
            var outcome = new PassOutcome { ClosestApproachMetres = double.PositiveInfinity };
            int ticks = (int)(MaxSeconds * config.TickRate);
            bool launched = false;
            for (int t = 0; t < ticks; t++)
            {
                commands.Clear();
                CommandsFor(sim.Tick, receivers, commands);
                int before = events.Count;
                sim.Step(commands, events);
                SimState state = sim.PeekState();

                if (t == 0)
                {
                    PlayerState p = state.Players[0];
                    outcome.TargetId = p.WindUpTarget;
                    outcome.Line = p.WindUpDirection;
                    int index = p.WindUpTarget - PasserId - 1;
                    outcome.Target = p.WindUpTarget == 0 ? null : receivers[index].Name;
                    if (p.WindUpTarget != 0)
                    {
                        // The lead: how far the aim point lies ahead of the receiver along the pass line.
                        Vec2 toReceiver = state.Players[index + 1].Position - state.Ball.Position.Horizontal;
                        double along = toReceiver.X * SimMath.Cos(p.WindUpDirection) + toReceiver.Y * SimMath.Sin(p.WindUpDirection);
                        outcome.LeadMetres = SimMath.Sqrt(Math.Max(0, toReceiver.LengthSquared - along * along));
                    }
                }

                for (int e = before; e < events.Count; e++)
                {
                    if (events[e].Kind == SimEventKind.Kicked && !launched)
                    {
                        launched = true;
                        outcome.LaunchSpeedMps = SimMath.Sqrt(state.Ball.Velocity.LengthSquared);
                    }
                }

                if (!launched || outcome.TargetId == 0) continue;
                Vec2 gap = state.Ball.Position.Horizontal - state.Players[outcome.TargetId - PasserId].Position;
                double distance = SimMath.Sqrt(gap.LengthSquared);
                if (distance < outcome.ClosestApproachMetres)
                {
                    outcome.ClosestApproachMetres = distance;
                    outcome.ClosestApproachSeconds = sim.Tick / (double)config.TickRate;
                    outcome.ArrivalSpeedMps = SimMath.Sqrt(state.Ball.Velocity.LengthSquared);
                }
            }

            if (outcome.TargetId == 0) outcome.ClosestApproachMetres = 0;
            return outcome;
        }
    }
}
