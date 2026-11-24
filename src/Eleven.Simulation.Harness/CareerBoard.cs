using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The board measured (#200): a career rolled forward headless, points a match by wage rank,
    /// the clubs meeting their league objectives, confidence at each season's end, and the
    /// reputations' drift by division.
    /// </summary>
    public static class CareerBoard
    {
        /// <summary>One season, every division's clubs.</summary>
        public sealed class SeasonView
        {
            public int Season;

            /// <summary>Points a league match by expected place, 1 first, every division's together.</summary>
            public double[] PointsByRank = Array.Empty<double>();

            /// <summary>The clubs that met their league objective, of all of them; by division too.</summary>
            public int Met, Clubs;

            public int[] MetByDivision = Array.Empty<int>();

            /// <summary>Confidence at the season's end, before the new season's carry.</summary>
            public double MeanConfidence, ConfidenceSpread, BelowTwenty;

            /// <summary>Each division's mean club and manager reputation at the season's start.</summary>
            public double[] ClubReputation = Array.Empty<double>(), ManagerReputation = Array.Empty<double>();
        }

        /// <summary>The seasons, and each division's mean club and manager reputation after the last one's turn.</summary>
        public sealed class Measurement
        {
            public List<SeasonView> Seasons = new List<SeasonView>();

            public double[] ClubReputationAfter = Array.Empty<double>(), ManagerReputationAfter = Array.Empty<double>();

            /// <summary>A division's mean club reputation after the last turn, less at the first season's start.</summary>
            public double ClubDrift(int division) => 0;

            public double ManagerDrift(int division) => 0;
        }

        /// <summary><paramref name="seasons"/> seasons from <paramref name="seed"/>, each turned.</summary>
        public static Measurement Measure(ulong seed, int seasons) => new Measurement();

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons) => "";
    }
}
