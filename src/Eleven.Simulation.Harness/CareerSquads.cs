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
    }
}
