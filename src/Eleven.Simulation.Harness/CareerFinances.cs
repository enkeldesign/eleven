using System.Collections.Generic;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A club's money measured (#198): a career rolled forward headless, the clubs' balances by
    /// division at each season's start, wages over revenue by division, each ledger line's share,
    /// and the clubs in deficit.
    /// </summary>
    public static class CareerFinances
    {
        /// <summary>One division's season: its clubs' balances at the season's start, and the season's ledger.</summary>
        public sealed class DivisionSeason
        {
            public int Season, Division, InDeficit;
            public double MeanBalance, BalanceSpread, WagesOverRevenue;
            public long[] Lines = new long[Ledger.Lines];
        }

        /// <summary><paramref name="seasons"/> seasons from <paramref name="seed"/>: each division's, each season.</summary>
        public static List<DivisionSeason> Measure(ulong seed, int seasons) => new List<DivisionSeason>();

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons) => "";
    }
}
