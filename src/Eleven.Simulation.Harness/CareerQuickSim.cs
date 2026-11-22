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
        public static double TierPosition(double rating)
        {
            if (!(rating > TierPoints[0])) return 0;
            for (int i = 0; i < TierPoints.Length - 1; i++)
            {
                if (rating <= TierPoints[i + 1]) return i + (rating - TierPoints[i]) / (TierPoints[i + 1] - TierPoints[i]);
            }

            return TierPoints.Length - 1;
        }

        /// <summary>
        /// A club's selected XI (<see cref="CareerSquads.Selection"/>) as a quick-sim side: its
        /// tier is the outfielders' mean CPU rating (the mean of each one's CPU entries' ratings)
        /// at <see cref="TierPosition"/>; its capabilities the mean of their mapped capabilities.
        /// The quick-sim's sides have no keeper, as its calibration league's sides had none apart.
        /// </summary>
        public static QuickSide Side(World world, int clubId, int perSide)
        {
            SquadSelection xi = CareerSquads.Selection(world, clubId, perSide);
            var mapped = new PlayerCapabilities[perSide];
            double cpu = 0;
            for (int i = 0; i < perSide; i++)
            {
                Player p = xi.Starters[i];
                mapped[i] = RatingMap.Capabilities(p);
                double own = 0;
                foreach (RatingEntry e in RatingMap.Cpu) own += e.Rating(p);
                cpu += own / RatingMap.Cpu.Count;
            }

            PlayerCapabilities mean = PlayerCapabilities.Reference;
            foreach (TunableParameter t in PlayerCapabilities.Parameters)
            {
                double sum = 0;
                foreach (PlayerCapabilities c in mapped) sum += c.Get(t.Name);
                mean = mean.With(t.Name, sum / perSide);
            }

            return new QuickSide(TierPosition(cpu / perSide), mean);
        }

        /// <summary>A match between two clubs that nobody plays: the quick-sim's result.</summary>
        public static MatchResult Play(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed) =>
            QuickSim.Play(config, Side(world, homeClub, perSide), Side(world, awayClub, perSide), seed);
    }
}
