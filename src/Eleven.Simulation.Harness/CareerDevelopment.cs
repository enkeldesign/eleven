using System.Collections.Generic;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Development and ageing measured (#194): a career rolled forward headless, the world's
    /// footballers by age band at each season's start, its mean level's drift, retirements and
    /// youth, squad sizes, and a few footballers followed through.
    /// </summary>
    public static class CareerDevelopment
    {
        /// <summary>One season's start: the world's mean level, its footballers by age band, and the season's turn's changes.</summary>
        public sealed class SeasonView
        {
            public int Season;
            public double MeanLevel;
            public List<(string Band, int Count, double Mean, double Spread)> Bands = new List<(string, int, double, double)>();
            public int Retired, Intake, SmallestSquad, LargestSquad;
        }

        /// <summary><paramref name="seasons"/> seasons of a new career, headless: each season's start.</summary>
        public static List<SeasonView> Measure(ulong seed, int seasons) => new List<SeasonView>();

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons) => "";
    }
}
