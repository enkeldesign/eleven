using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The transfer market measured (#203): a career rolled forward headless, each window's
    /// transfers and fees by the divisions they went from and to, fees against values, wage bills
    /// over budget before and after, and the top division's footballers bought from below.
    /// </summary>
    public static class CareerTransfers
    {
        /// <summary>One window.</summary>
        public sealed class WindowView
        {
            public int Season;
            public TransferWindow Window;

            /// <summary>Transfers and their fees by the division from, then to.</summary>
            public int[,] Count = new int[3, 3];

            public long[,] Fees = new long[3, 3];

            /// <summary>The mean fee over the footballer's value.</summary>
            public double FeeOverValue;

            /// <summary>Each division's mean wage bill over budget, before the window and after.</summary>
            public double[] OverBefore = new double[3], OverAfter = new double[3];
        }

        /// <summary>The windows, and the share of the top division's footballers its clubs bought from below by the end.</summary>
        public sealed class Measurement
        {
            public List<WindowView> Windows = new List<WindowView>();

            public double TopBoughtFromBelow;

            /// <summary>Every fee paid, less every fee received, over every window: 0 when the money is conserved.</summary>
            public long FeesNet;
        }

        /// <summary><paramref name="seasons"/> seasons from <paramref name="seed"/>, each turned.</summary>
        public static Measurement Measure(ulong seed, int seasons)
        {
            World w = CareerSession.NewWorld(seed);
            CareerSession s = CareerSession.Start(w, seed, w.Clubs[0].Id, CareerRole.ManagerOnly, SpikeScenario.Config());
            var m = new Measurement();
            var fromBelow = new HashSet<int>();
            double[] Over()
            {
                var over = new double[3];
                for (int d = 1; d <= 3; d++)
                {
                    List<double> ratios = w.Clubs.Where(c => c.Division == d).Select(c => (double)Finance.WageBill(w, c) / Math.Max(1, Finance.WageBudget(w, c))).ToList();
                    over[d - 1] = ratios.Count > 0 ? ratios.Average() : 0;
                }

                return over;
            }

            void Record(int season, TransferWindow window, double[] before, Dictionary<int, int> division)
            {
                var v = new WindowView { Season = season, Window = window, OverBefore = before, OverAfter = Over() };
                double ratios = 0;
                foreach (Transfer t in w.LastTransfers.Where(t => t.Window == window))
                {
                    int from = Math.Clamp(division[t.From], 1, 3) - 1, to = Math.Clamp(division[t.To], 1, 3) - 1;
                    v.Count[from, to]++;
                    v.Fees[from, to] += t.Fee;
                    ratios += (double)t.Fee / Math.Max(1, t.Value);
                    if (to == 0 && from > 0) fromBelow.Add(t.PlayerId);
                }

                int n = v.Count.Cast<int>().Sum();
                v.FeeOverValue = n > 0 ? ratios / n : 0;
                m.Windows.Add(v);
            }

            for (int i = 1; i <= seasons; i++)
            {
                while (!s.SeasonOver)
                {
                    double[] before = Over();
                    s.Simulate();
                    if (Transfers.MidSeasonAfter(s.Season, s.Rules)) Record(i, TransferWindow.MidSeason, before, w.Clubs.ToDictionary(c => c.Id, c => c.Division));
                }

                double[] turn = Over();
                s.NextSeason();

                // The pre-season window ran at the turn, by each club's new division.
                Record(i + 1, TransferWindow.PreSeason, turn, w.Clubs.ToDictionary(c => c.Id, c => c.Division));
                m.FeesNet += w.Clubs.Sum(c => c.LastSeason[LedgerLine.Transfers]);
            }

            m.FeesNet += w.Clubs.Sum(c => c.Season[LedgerLine.Transfers]);
            List<Player> top = w.Players.Where(p => w.Clubs.Any(c => c.Id == p.ClubId && c.Division == 1)).ToList();
            m.TopBoughtFromBelow = top.Count > 0 ? (double)top.Count(p => fromBelow.Contains(p.Id)) / top.Count : 0;
            return m;
        }

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons)
        {
            Measurement m = Measure(seed, seasons);
            var t = new StringBuilder();
            string I(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            t.AppendLine(FormattableString.Invariant($"A headless career from seed {seed:x}, {seasons} season{(seasons == 1 ? "" : "s")}, each turned:"));
            t.AppendLine();
            t.AppendLine("Transfers each window, and their fees, by the divisions from and to (1→1 is within the top):");
            t.AppendLine();
            string[] pairs = { "1→1", "2→1", "3→1", "2→2", "3→2", "1→2", "3→3", "1→3", "2→3" };
            t.AppendLine("| Season | Window | " + string.Join(" | ", pairs) + " | Fees | Fee over value |");
            t.AppendLine("| --- | --- | " + string.Join(" | ", Enumerable.Repeat("---", pairs.Length)) + " | --- | --- |");
            foreach (WindowView v in m.Windows)
            {
                IEnumerable<string> cells = pairs.Select(p => I(v.Count[p[0] - '1', p[2] - '1']));
                long fees = v.Fees.Cast<long>().Sum();
                t.AppendLine(FormattableString.Invariant($"| {v.Season} | {(v.Window == TransferWindow.PreSeason ? "pre-season" : "mid-season")} | {string.Join(" | ", cells)} | {I(fees)}k | {v.FeeOverValue:F2} |"));
            }

            t.AppendLine();
            t.AppendLine("Each division's mean wage bill over budget, before the window's matchday or turn and after:");
            t.AppendLine();
            t.AppendLine("| Season | Window | Division 1 | Division 2 | Division 3 |");
            t.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (WindowView v in m.Windows)
            {
                string Cell(int d) => FormattableString.Invariant($"{v.OverBefore[d]:F2} → {v.OverAfter[d]:F2}");
                t.AppendLine(FormattableString.Invariant($"| {v.Season} | {(v.Window == TransferWindow.PreSeason ? "pre-season" : "mid-season")} | {Cell(0)} | {Cell(1)} | {Cell(2)} |"));
            }

            t.AppendLine();
            t.AppendLine(FormattableString.Invariant($"Transfers: every fee paid less every fee received, {m.FeesNet}k. The top division's footballers bought from below by the end: {100 * m.TopBoughtFromBelow:F0} %."));
            return t.ToString().Replace("\r\n", "\n");
        }
    }
}
