using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        /// <summary><paramref name="seasons"/> seasons from <paramref name="seed"/>: each division's, each season, every season turned so its merit is paid.</summary>
        public static List<DivisionSeason> Measure(ulong seed, int seasons)
        {
            World w = CareerSession.NewWorld(seed);
            CareerSession s = CareerSession.Start(w, seed, w.Clubs[0].Id, CareerRole.ManagerOnly, SpikeScenario.Config());
            var views = new List<DivisionSeason>();
            for (int i = 1; i <= seasons; i++)
            {
                // The season's start, by the divisions its clubs play in.
                var division = w.Clubs.ToDictionary(c => c.Id, c => c.Division);
                var start = new Dictionary<int, DivisionSeason>();
                foreach (IGrouping<int, Club> d in w.Clubs.GroupBy(c => c.Division).OrderBy(g => g.Key))
                {
                    List<double> balances = d.Select(c => (double)c.Balance).ToList();
                    double mean = balances.Average();
                    start[d.Key] = new DivisionSeason
                    {
                        Season = i,
                        Division = d.Key,
                        MeanBalance = mean,
                        BalanceSpread = Eleven.Simulation.Maths.SimMath.Sqrt(balances.Sum(b => (b - mean) * (b - mean)) / Math.Max(1, balances.Count - 1)),
                        InDeficit = d.Count(c => c.Balance < 0),
                    };
                }

                while (!s.SeasonOver) s.Simulate();
                s.NextSeason();

                // The season's ledger, by the division each club played it in.
                foreach (DivisionSeason v in start.Values)
                {
                    foreach (Club c in w.Clubs.Where(c => division[c.Id] == v.Division))
                    {
                        for (int l = 0; l < Ledger.Lines; l++) v.Lines[l] += c.LastSeason.Amounts[l];
                    }

                    long revenue = v.Lines.Sum() - v.Lines[(int)LedgerLine.Wages] - v.Lines[(int)LedgerLine.Transfers];
                    v.WagesOverRevenue = revenue > 0 ? -(double)v.Lines[(int)LedgerLine.Wages] / revenue : 0;
                    views.Add(v);
                }
            }

            return views;
        }

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons)
        {
            List<DivisionSeason> views = Measure(seed, seasons);
            var t = new StringBuilder();
            t.AppendLine(FormattableString.Invariant($"A headless career from seed {seed:x}, {seasons} season{(seasons == 1 ? "" : "s")}, each turned:"));
            t.AppendLine();
            t.AppendLine("| Season | Division | Balance at its start | In deficit | Wages over revenue | Broadcasting | Matchday | Merit | Cup |");
            t.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (DivisionSeason v in views)
            {
                double revenue = v.Lines.Sum() - v.Lines[(int)LedgerLine.Wages] - v.Lines[(int)LedgerLine.Transfers];
                string Share(LedgerLine line) => revenue > 0 ? FormattableString.Invariant($"{100 * v.Lines[(int)line] / revenue:F0} %") : "—";
                t.AppendLine(FormattableString.Invariant($"| {v.Season} | {v.Division} | {v.MeanBalance:N0}k ± {v.BalanceSpread:N0}k | {v.InDeficit} | {v.WagesOverRevenue:F2} | {Share(LedgerLine.Broadcasting)} | {Share(LedgerLine.Matchday)} | {Share(LedgerLine.Merit)} | {Share(LedgerLine.Cup)} |"));
            }

            return t.ToString().Replace("\r\n", "\n");
        }
    }
}
