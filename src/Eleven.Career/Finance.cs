using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Maths;

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
        public double[] BroadcastPerDay = { 48, 10, 3 };

        /// <summary>A home match's takings by division, then a share more a point of reputation above 10.</summary>
        public double[] MatchdayBase = { 250, 130, 50 };

        public double MatchdayPerReputation = 0.06;

        /// <summary>The champion's merit payment by division; each place below gets a twelfth less.</summary>
        public double[] MeritTop = { 3000, 900, 300 };

        /// <summary>The cup's payment a round reached, and the winner's on top.</summary>
        public double CupPerRound = 40, CupWinner = 200;

        /// <summary>A club's balance at the world's start, by division.</summary>
        public double[] StartBalance = { 5000, 1500, 400 };

        /// <summary>
        /// The board's wage budget: this share of last season's revenue, by division (#201). The
        /// top's is UEFA's squad-cost cap; lower divisions run higher wage ratios (Deloitte), and
        /// their boards allow for it.
        /// </summary>
        public double[] WageShare = { 0.70, 0.85, 0.95 };

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

        /// <summary>Fees paid out and received (#203): not revenue.</summary>
        Transfers = 5,
    }

    /// <summary>A season's money in and out (#198), each k on one line: wages negative, revenue positive.</summary>
    public sealed class Ledger
    {
        public const int Lines = 6;

        /// <summary>The lines format 8 saves (#198); format 11 saves the transfers line (#203).</summary>
        public const int FirstLines = 5;

        public long[] Amounts = new long[Lines];

        /// <summary>
        /// The days its wages and broadcasting were paid for (#201): a season's calendar and the
        /// off-season before it, a first season's only its calendar.
        /// </summary>
        public int Days;

        /// <summary>A ledger from a world before #201 with money already booked: its days aren't known (Codex on #204).</summary>
        public const int UnknownDays = -1;

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

        /// <summary>Revenue: every line but wages and transfers (#203), which football's accounts keep apart.</summary>
        public long Revenue => Net - this[LedgerLine.Wages] - this[LedgerLine.Transfers];
    }

    /// <summary>
    /// A club's money (#198): its footballers' wages each day, its broadcasting each day, its home
    /// matches' takings, and at the season's end its merit and cup payments, every k booked to one
    /// ledger line so the money is conserved.
    /// </summary>
    public static class Finance
    {
        /// <summary>A footballer's weekly wage, by his level, age and his club's division.</summary>
        public static int Wage(Player p, int division, FinanceRules? rules = null)
        {
            rules ??= FinanceRules.Reference;
            double share = p.Age < 21 ? rules.YoungWage : p.Age < 24 ? rules.NearPeakWage : 1;
            double wage = rules.WageBase * SimMath.Exp(rules.WageSlope * (Development.Level(p) - 10)) * Division(rules.DivisionWage, division) * share;
            return Math.Max(1, (int)Math.Round(wage));
        }

        /// <summary>A club's balance at the world's start.</summary>
        public static long StartBalance(Club club, FinanceRules? rules = null) => (long)Math.Round(Division((rules ?? FinanceRules.Reference).StartBalance, club.Division));

        /// <summary>The <paramref name="days"/> between matches: each club pays its wages and earns its broadcasting.</summary>
        public static void Pass(World world, int days, FinanceRules? rules = null)
        {
            rules ??= FinanceRules.Reference;
            var bills = new Dictionary<int, long>();
            foreach (Player p in world.Players) bills[p.ClubId] = (bills.TryGetValue(p.ClubId, out long b) ? b : 0) + p.Wage;
            foreach (Club c in world.Clubs)
            {
                if (c.Season.Days != Ledger.UnknownDays) c.Season.Days += days;
                Book(c, LedgerLine.Wages, -(long)Math.Round((bills.TryGetValue(c.Id, out long bill) ? bill : 0) * days / 7.0));
                Book(c, LedgerLine.Broadcasting, (long)Math.Round(Division(rules.BroadcastPerDay, c.Division) * days));
            }
        }

        /// <summary>
        /// A matchday's home takings, for each home club of <paramref name="played"/>. A match at a
        /// neutral ground, the cup final, has no home side and brings none: its money is the cup's.
        /// </summary>
        public static void Matchday(World world, IEnumerable<Fixture> played, FinanceRules? rules = null)
        {
            rules ??= FinanceRules.Reference;
            foreach (Fixture f in played)
            {
                if (f.Neutral) continue;
                Club? home = world.Clubs.Find(c => c.Id == f.Home);
                if (home != null) Book(home, LedgerLine.Matchday, Takings(home, rules));
            }
        }

        // A home match's takings: the division's, a share more a point of reputation above 10.
        private static long Takings(Club home, FinanceRules rules) =>
            Math.Max(0, (long)Math.Round(Division(rules.MatchdayBase, home.Division) * (1 + rules.MatchdayPerReputation * (home.Reputation - 10))));

        /// <summary>The season's end: each club's merit by league place and its cup payment by the round reached; then its season's ledger becomes last season's.</summary>
        public static void SeasonEnd(World world, Season season, CompetitionRules competition, FinanceRules? rules = null)
        {
            rules ??= FinanceRules.Reference;
            foreach (Club c in world.Clubs)
            {
                List<TableRow> table = SeasonEngine.Table(season, (CompetitionId)c.Division, competition);
                int place = table.FindIndex(r => r.Club == c.Id) + 1;
                if (place > 0) Book(c, LedgerLine.Merit, (long)Math.Round(Division(rules.MeritTop, c.Division) * (table.Count + 1 - place) / table.Count));
                int reached = 0;
                foreach (Fixture f in season.Fixtures)
                {
                    if (f.Competition == CompetitionId.Cup && (f.Home == c.Id || f.Away == c.Id)) reached = Math.Max(reached, f.Round);
                }

                long cup = (long)Math.Round(rules.CupPerRound * reached + (season.CupWinner == c.Id ? rules.CupWinner : 0));
                if (cup > 0) Book(c, LedgerLine.Cup, cup);
                c.LastSeason = c.Season;
                c.Season = new Ledger();
            }
        }

        /// <summary>
        /// The board's weekly wage budget: its share of last season's revenue over the weeks it was
        /// earned in, or of a season's expected over the season's weeks in the first. A first
        /// season's ledger has no off-season before it, so its weeks are fewer (#201). At the
        /// season's turn, a promoted or relegated club's share is its next <paramref name="division"/>'s
        /// (Codex on #204); 0 is its own.
        /// </summary>
        public static long WageBudget(World world, Club club, FinanceRules? rules = null, CompetitionRules? competition = null, SquadRules? squad = null, int division = 0)
        {
            rules ??= FinanceRules.Reference;
            int shareOf = division > 0 ? division : club.Division;
            int days = SeasonDays(competition ?? CompetitionRules.Reference, squad ?? SquadRules.Reference);
            bool earned = club.LastSeason.Revenue > 0;
            double revenue = earned ? club.LastSeason.Revenue : Expected(club, rules, days);
            int weeksOf = earned && club.LastSeason.Days > 0 ? club.LastSeason.Days : days;
            return (long)Math.Round(Division(rules.WageShare, shareOf) * revenue / (weeksOf / 7.0));
        }

        /// <summary>The days a season pays wages and broadcasting for: its calendar's, from its first matchday past its last, and the off-season's.</summary>
        public static int SeasonDays(CompetitionRules competition, SquadRules squad)
        {
            List<Matchday> calendar = SeasonCalendar.Of(competition);
            return calendar[calendar.Count - 1].Day - calendar[0].Day + Availability.GapAfter(competition, calendar.Count - 1) + squad.OffSeasonDays;
        }

        // A first season's expected revenue: its days' broadcasting, eleven home matches, a mid-table merit and two rounds of the cup.
        private static double Expected(Club club, FinanceRules rules, int days) =>
            Division(rules.BroadcastPerDay, club.Division) * days + 11 * Takings(club, rules) + Division(rules.MeritTop, club.Division) * 6.5 / 12 + 2 * rules.CupPerRound;

        // Money in or out, onto the club's balance and its season's line.
        internal static void Book(Club c, LedgerLine line, long amount)
        {
            c.Balance += amount;
            c.Season.Amounts[(int)line] += amount;
        }

        private static double Division(double[] row, int division) => row[Math.Clamp(division, 1, row.Length) - 1];

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
