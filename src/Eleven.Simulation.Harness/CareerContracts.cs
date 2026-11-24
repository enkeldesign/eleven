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
        public static List<TurnView> Measure(ulong seed, int seasons)
        {
            World w = CareerSession.NewWorld(seed);
            CareerSession s = CareerSession.Start(w, seed, w.Clubs[0].Id, CareerRole.ManagerOnly, SpikeScenario.Config());
            var turns = new List<TurnView>();
            int Band(int age) => age < 24 ? 0 : age < 30 ? 1 : 2;
            for (int i = 1; i <= seasons; i++)
            {
                while (!s.SeasonOver) s.Simulate();

                // Before the turn: each footballer's club, contract and age.
                Dictionary<int, (int Club, int Years, int Age)> before = w.Players.ToDictionary(p => p.Id, p => (p.ClubId, p.ContractYears, p.Age));
                s.NextSeason();
                var view = new TurnView { Season = i };
                Dictionary<int, int> division = w.Clubs.ToDictionary(c => c.Id, c => c.Division);
                int Division(int club) => Math.Clamp(division.TryGetValue(club, out int d) ? d : 1, 1, 3) - 1;
                foreach (Player p in w.Players)
                {
                    if (!before.TryGetValue(p.Id, out var b) || b.Club == 0 || b.Years != 1 || p.ClubId != b.Club) continue;
                    view.Renewed[Division(p.ClubId), Band(b.Age)]++;
                }

                foreach (ContractMove m in w.LastMoves)
                {
                    int age = before.TryGetValue(m.PlayerId, out var b) ? b.Age : 0;
                    if (m.To == 0) view.Released[Division(m.From), Band(age)]++;
                    else view.Signed[Division(m.To), Band(age)]++;
                }

                view.FreeAgents = w.Players.Count(p => p.ClubId == 0);
                view.FreeRetired = w.LastRetired.Count(r => r.ClubId == 0);
                for (int d = 1; d <= 3; d++)
                {
                    List<double> over = w.Clubs.Where(c => c.Division == d).Select(c => (double)Finance.WageBill(w, c) / Math.Max(1, Finance.WageBudget(w, c))).ToList();
                    view.BillOverBudget[d - 1] = over.Count > 0 ? over.Average() : 0;
                    view.HighestOverBudget[d - 1] = over.Count > 0 ? over.Max() : 0;
                }

                List<int> sizes = w.Clubs.Select(c => w.Players.Count(p => p.ClubId == c.Id)).ToList();
                view.SmallestSquad = sizes.Min();
                view.LargestSquad = sizes.Max();
                turns.Add(view);
            }

            return turns;
        }

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons)
        {
            List<TurnView> turns = Measure(seed, seasons);
            var t = new StringBuilder();
            string I(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            t.AppendLine(FormattableString.Invariant($"A headless career from seed {seed:x}, {seasons} season{(seasons == 1 ? "" : "s")}, each turned:"));
            t.AppendLine();
            t.AppendLine("Each turn's contracts ending, by the club's next division and the footballer's age:");
            t.AppendLine();
            t.AppendLine("| Turn | Division | " + string.Join(" | ", Bands.Select(b => "Renewed " + b.ToLowerInvariant())) + " | " + string.Join(" | ", Bands.Select(b => "Released " + b.ToLowerInvariant())) + " | Signed free |");
            t.AppendLine("| --- | --- | " + string.Join(" | ", Enumerable.Repeat("---", 7)) + " |");
            foreach (TurnView v in turns)
            {
                for (int d = 0; d < 3; d++)
                {
                    int signed = 0;
                    for (int b = 0; b < 3; b++) signed += v.Signed[d, b];
                    t.AppendLine($"| {I(v.Season)} | {I(d + 1)} | {string.Join(" | ", Enumerable.Range(0, 3).Select(b => I(v.Renewed[d, b])))} | {string.Join(" | ", Enumerable.Range(0, 3).Select(b => I(v.Released[d, b])))} | {I(signed)} |");
                }
            }

            t.AppendLine();
            t.AppendLine("After each turn: the free agents, those unsigned who retired, the wage bills over their budgets by division, mean and highest, and the squads:");
            t.AppendLine();
            t.AppendLine("| Turn | Free agents | Retired unsigned | Division 1 | Division 2 | Division 3 | Squads |");
            t.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (TurnView v in turns)
            {
                string Over(int d) => FormattableString.Invariant($"{v.BillOverBudget[d]:F2}, {v.HighestOverBudget[d]:F2}");
                t.AppendLine($"| {I(v.Season)} | {I(v.FreeAgents)} | {I(v.FreeRetired)} | {Over(0)} | {Over(1)} | {Over(2)} | {I(v.SmallestSquad)}–{I(v.LargestSquad)} |");
            }

            return t.ToString().Replace("\r\n", "\n");
        }
    }
}
