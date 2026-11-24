using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Contracts measured (#201): a career rolled forward headless, each turn's renewals, releases
    /// and free signings by division and age band, the free agents left and retired, each
    /// division's wage bills against their budgets, and the squads' sizes.
    /// </summary>
    public static class CareerContracts
    {
        /// <summary>The age bands: under 24, 24–29, 30 and over.</summary>
        public static readonly string[] Bands = { "Under 24", "24–29", "30 and over" };

        /// <summary>One season's turn, and the next season's start.</summary>
        public sealed class TurnView
        {
            public int Season;

            /// <summary>By division (the club's next), then age band.</summary>
            public int[,] Renewed = new int[3, 3], Released = new int[3, 3], Signed = new int[3, 3];

            /// <summary>Free agents after the turn, and those unsigned who retired at it.</summary>
            public int FreeAgents, FreeRetired;

            /// <summary>Each division's wage bills over their budgets at the next season's start: mean and highest.</summary>
            public double[] BillOverBudget = new double[3], HighestOverBudget = new double[3];

            /// <summary>The smallest and largest squad at the next season's start.</summary>
            public int SmallestSquad, LargestSquad;
        }

        /// <summary><paramref name="seasons"/> seasons from <paramref name="seed"/>, each turned.</summary>
        public static List<TurnView> Measure(ulong seed, int seasons) => new List<TurnView>();

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons) => "";
    }
}
