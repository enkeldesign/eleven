using System;
using System.Collections.Generic;
using System.Linq;

namespace Eleven.Simulation.Tests
{
    /// <summary>A shooter and a keeper in front of the goal (#69), for keeper rule tests.</summary>
    internal static class KeeperWorld
    {
        public const int Shooter = 1, ShooterController = 1;
        public const int Keeper = 2, KeeperController = 2;
        public const int BallId = 100;

        // The keeper's starting spot: 1.5 m off the line, centred.
        public static Vec2 Spot(SimConfig c) => new Vec2(c.GoalLineX - 1.5, 0);

        public static SimState State(SimConfig c, Vec3 ball, Vec3 ballVelocity = default, KeeperCapabilities? keeper = null, Vec2? keeperAt = null,
            Vec2? shooterAt = null, ulong seed = 7) => new SimState
        {
            Seed = seed,
            Ball = new BallState { Id = BallId, Position = ball, Velocity = ballVelocity },
            Players = new[]
            {
                new PlayerState
                {
                    Id = Shooter, OwnerControllerId = ShooterController, TeamId = 1,
                    Capabilities = PlayerCapabilities.Reference.With("kickError", 0),
                    Position = shooterAt ?? new Vec2(c.GoalLineX - 30, 20),
                },
                new PlayerState { Id = Keeper, OwnerControllerId = KeeperController, TeamId = 2, Capabilities = PlayerCapabilities.Reference, Position = keeperAt ?? Spot(c), Heading = Math.PI },
            },
            Keepers = new[] { new KeeperState { PlayerId = Keeper, Capabilities = keeper ?? KeeperCapabilities.Reference } },
        };

        /// <summary>A shooter 0.6 m behind a ball on the penalty spot.</summary>
        public static SimState Penalty(SimConfig c, KeeperCapabilities? keeper = null, ulong seed = 7) =>
            State(c, new Vec3(c.GoalLineX - 11, 0, c.BallRadius), keeper: keeper, shooterAt: new Vec2(c.GoalLineX - 11.6, 0), seed: seed);

        /// <summary>A kick from the shooter at <paramref name="at"/> on the goal line's plane: strength 0–1, loft 0–1.</summary>
        public static KickIntent Shot(SimConfig c, SimState s, double y, double strength, double loft = 0) =>
            new KickIntent(new Vec2(c.GoalLineX, y) - s.Ball.Position.Horizontal, strength, loft);

        public static KeeperState K(Simulator sim) => sim.PeekState().Keepers[0];

        public static PlayerState Body(Simulator sim) => sim.PeekState().Players[1];

        /// <summary>Steps <paramref name="sim"/> with the shooter's and the keeper's commands for each tick.</summary>
        public static List<SimEvent> Run(Simulator sim, int ticks, Func<long, Command?>? shooter = null, Func<long, Command?>? keeper = null, List<SimState>? states = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var commands = new List<Command>();
                Command? s = shooter?.Invoke(sim.Tick), k = keeper?.Invoke(sim.Tick);
                if (s.HasValue) commands.Add(s.Value);
                if (k.HasValue) commands.Add(k.Value);
                sim.Step(commands, events);
                states?.Add(sim.PeekState().Clone());
            }

            return events;
        }

        /// <summary>The same, with the keeper's brain deciding his commands.</summary>
        public static List<SimEvent> RunWithBrain(Simulator sim, int ticks, Func<long, Command?>? shooter = null, List<ulong>? hashes = null, List<Command[]>? recorded = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var commands = new List<Command>();
                Command? s = shooter?.Invoke(sim.Tick);
                if (s.HasValue) commands.Add(s.Value);
                KeeperControllers.Append(sim, commands);
                recorded?.Add(commands.ToArray());
                sim.Step(commands, events);
                hashes?.Add(sim.StateHash());
            }

            return events;
        }

        public static bool Saved(IEnumerable<SimEvent> events) => events.Any(e => e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.KeeperParried);
    }
}
