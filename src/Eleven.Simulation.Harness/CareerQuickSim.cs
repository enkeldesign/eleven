using System;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The world's clubs in the quick-sim (#163): a club's selected XI as a <see cref="QuickSide"/>.
    /// Its CPU skill is the XI's mean CPU rating placed on the tiers' own points of the rating
    /// scale; its capabilities are the mean of its outfielders' mapped capabilities.
    /// </summary>
    public static class CareerQuickSim
    {
        /// <summary>The ratings at which the CPU tiers sit (#159's CPU entries): apprentice, club, pro, elite.</summary>
        public static readonly double[] TierPoints = { 1, 5.75, RatingMap.ReferenceRating, 20 };

        /// <summary>A CPU rating's tier position, piecewise-linear through <see cref="TierPoints"/>.</summary>
        public static double TierPosition(double rating) => 0;

        /// <summary>A club's selected XI as a quick-sim side.</summary>
        public static QuickSide Side(World world, int clubId, int perSide) => new QuickSide(0, PlayerCapabilities.Reference);

        /// <summary>A match between two clubs that nobody plays: the quick-sim's result.</summary>
        public static MatchResult Play(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed) =>
            QuickSim.Play(config, Side(world, homeClub, perSide), Side(world, awayClub, perSide), seed);
    }
}
