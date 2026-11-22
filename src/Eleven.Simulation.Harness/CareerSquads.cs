using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A match between two of the world's clubs (#159): each side's XI and bench chosen from its
    /// squad, every footballer, keeper and CPU footballer given his own values by
    /// <see cref="RatingMap"/>. The adapter outside the career that ADR 0005 calls for: the career
    /// holds the world, the simulation plays the match.
    /// </summary>
    public static class CareerSquads
    {
        /// <summary>Plumbing: the reference squads, whoever the clubs.</summary>
        public static SimState Match(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed, bool bothCpu = true) =>
            Squads.Match(config, seed, perSide, bothCpu: bothCpu);

        /// <summary>Plumbing: no one is chosen.</summary>
        public static SquadSelection Selection(World world, int clubId, int perSide) => new SquadSelection();

        /// <summary>
        /// A CPU-against-CPU match between two clubs, played to its result by the full simulation
        /// with every brain running: the keepers', the CPU's, the restarts' and the managers'.
        /// </summary>
        public static MatchResult Play(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed)
        {
            var sim = new Simulator(config, Match(config, world, homeClub, awayClub, perSide, seed));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            long limit = 8L * config.HalfTicks + 600L * config.TickRate;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                events.Clear();
                sim.Step(commands, events);
                recorder.Observe(config, sim.PeekState(), events);
            }

            return recorder.Result ?? throw new InvalidOperationException($"{homeClub} against {awayClub} (seed {seed}) didn't finish.");
        }
    }

    /// <summary>A side's footballers for a match: the starters in the side's slot order, the keeper last, then the bench.</summary>
    public sealed class SquadSelection
    {
        public List<Player> Starters = new List<Player>();
        public List<Player> Bench = new List<Player>();
    }
}
