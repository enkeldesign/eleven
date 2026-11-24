using System;
using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Career
{
    /// <summary>
    /// A club's money's knobs (#198), all technical references, not tuned feel, in whole thousands
    /// of a fictional currency, "k". Deloitte's Annual Review of Football Finance puts wages at
    /// about 60–70 % of revenue in Europe's top divisions and above 100 % in some second tiers;
    /// UEFA's squad-cost rule caps it at 70 %. A top division earns mostly from broadcasting.
    /// </summary>
    public sealed class FinanceRules
    {
        /// <summary>A footballer's weekly wage at level 10 and his peak, in the second division.</summary>
        public double WageBase = 4;

        /// <summary>His wage's growth a level: exp(WageSlope × (level − 10)).</summary>
        public double WageSlope = 0.25;

        /// <summary>What a division's clubs pay for the same footballer: divisions 1, 2, 3.</summary>
        public double[] DivisionWage = { 1.6, 1.0, 0.6 };

        /// <summary>A young footballer's wage, a share of his peak's: under 21, then 21–23.</summary>
        public double YoungWage = 0.5, NearPeakWage = 0.8;

        /// <summary>Broadcasting a day, by division.</summary>
        public double[] BroadcastPerDay = { 9, 3, 1 };

        /// <summary>A home match's takings by division, then a share more a point of reputation above 10.</summary>
        public double[] MatchdayBase = { 150, 50, 18 };

        public double MatchdayPerReputation = 0.06;

        /// <summary>The champion's merit payment by division; each place below gets a twelfth less.</summary>
        public double[] MeritTop = { 1200, 300, 80 };

        /// <summary>The cup's payment a round reached, and the winner's on top.</summary>
        public double CupPerRound = 40, CupWinner = 200;

        /// <summary>A club's balance at the world's start, by division.</summary>
        public double[] StartBalance = { 2000, 600, 200 };

        /// <summary>The board's wage budget: this share of last season's revenue.</summary>
        public double WageShare = 0.65;

        public static FinanceRules Reference => new FinanceRules();
    }

    /// <summary>A ledger's lines (#198). Numeric values are stable.</summary>
    public enum LedgerLine : byte
    {
        Wages = 0,
        Broadcasting = 1,
        Matchday = 2,
        Merit = 3,
        Cup = 4,
    }

    /// <summary>A season's money in and out (#198), each k on one line: wages negative, revenue positive.</summary>
    public sealed class Ledger
    {
        public const int Lines = 5;

        public long[] Amounts = new long[Lines];

        public long this[LedgerLine line] => Amounts[(int)line];

        /// <summary>The season's net: every line's sum.</summary>
        public long Net
        {
            get
            {
                long sum = 0;
                foreach (long a in Amounts) sum += a;
                return sum;
            }
        }

        /// <summary>Revenue: every line but wages.</summary>
        public long Revenue => Net - this[LedgerLine.Wages];
    }

    /// <summary>
    /// A club's money (#198): its footballers' wages each day, its broadcasting each day, its home
    /// matches' takings, and at the season's end its merit and cup payments, every k booked to one
    /// ledger line so the money is conserved.
    /// </summary>
    public static class Finance
    {
        /// <summary>A footballer's weekly wage, by his level, age and his club's division.</summary>
        public static int Wage(Player p, int division, FinanceRules? rules = null) => 0;

        /// <summary>A club's balance at the world's start.</summary>
        public static long StartBalance(Club club, FinanceRules? rules = null) => 0;

        /// <summary>The <paramref name="days"/> between matches: each club pays its wages and earns its broadcasting.</summary>
        public static void Pass(World world, int days, FinanceRules? rules = null)
        {
        }

        /// <summary>A matchday's home takings, for each home club of <paramref name="played"/>.</summary>
        public static void Matchday(World world, IEnumerable<Fixture> played, FinanceRules? rules = null)
        {
        }

        /// <summary>The season's end: each club's merit by league place and its cup payment by the round reached; then its season's ledger becomes last season's.</summary>
        public static void SeasonEnd(World world, Season season, CompetitionRules competition, FinanceRules? rules = null)
        {
        }

        /// <summary>The board's weekly wage budget: its share of last season's revenue, or of a season's expected in the first.</summary>
        public static long WageBudget(World world, Club club, FinanceRules? rules = null) => 0;

        /// <summary>A club's weekly wage bill.</summary>
        public static long WageBill(World world, Club club)
        {
            long sum = 0;
            foreach (Player p in world.Players)
            {
                if (p.ClubId == club.Id) sum += p.Wage;
            }

            return sum;
        }
    }
}
