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
            public double ClubDrift(int division) => Seasons.Count == 0 ? 0 : ClubReputationAfter[division - 1] - Seasons[0].ClubReputation[division - 1];

            public double ManagerDrift(int division) => Seasons.Count == 0 ? 0 : ManagerReputationAfter[division - 1] - Seasons[0].ManagerReputation[division - 1];
        }

        /// <summary><paramref name="seasons"/> seasons from <paramref name="seed"/>, each turned.</summary>
        public static Measurement Measure(ulong seed, int seasons)
        {
            World w = CareerSession.NewWorld(seed);
            CareerSession s = CareerSession.Start(w, seed, w.Clubs[0].Id, CareerRole.ManagerOnly, SpikeScenario.Config());
            int divisions = w.Clubs.Max(c => c.Division), places = s.Rules.LeagueClubs;
            var m = new Measurement();
            for (int i = 1; i <= seasons; i++)
            {
                var view = new SeasonView { Season = i, ClubReputation = ClubMeans(w, divisions), ManagerReputation = ManagerMeans(w, divisions) };
                Dictionary<int, int> expected = w.Clubs.ToDictionary(c => c.Id, c => c.Board.ExpectedPlace);
                Dictionary<int, int> division = w.Clubs.ToDictionary(c => c.Id, c => c.Division);
                while (!s.SeasonOver) s.Simulate();

                // Points a league match by the expected place of the club that took them.
                var points = new double[places];
                var games = new int[places];
                foreach (Fixture f in s.Season.Fixtures)
                {
                    if (f.Competition == CompetitionId.Cup || !s.Season.Results.TryGetValue(f.Id, out MatchResult? r)) continue;
                    int home = r.HomeGoals > r.AwayGoals ? 3 : r.HomeGoals == r.AwayGoals ? 1 : 0, away = r.AwayGoals > r.HomeGoals ? 3 : r.HomeGoals == r.AwayGoals ? 1 : 0;
                    points[expected[f.Home] - 1] += home;
                    games[expected[f.Home] - 1]++;
                    points[expected[f.Away] - 1] += away;
                    games[expected[f.Away] - 1]++;
                }

                view.PointsByRank = points.Select((p, k) => games[k] > 0 ? p / games[k] : 0).ToArray();
                s.NextSeason();

                // The verdict, by the division each club played the season in.
                view.Clubs = w.Clubs.Count;
                view.MetByDivision = new int[divisions];
                foreach (Club c in w.Clubs.Where(c => c.Board.LastPlace > 0 && c.Board.LastPlace <= c.Board.LastTarget))
                {
                    view.Met++;
                    view.MetByDivision[division[c.Id] - 1]++;
                }

                List<double> confidence = w.Clubs.Select(c => c.Board.LastConfidence).ToList();
                view.MeanConfidence = confidence.Average();
                view.ConfidenceSpread = Eleven.Simulation.Maths.SimMath.Sqrt(confidence.Sum(x => (x - view.MeanConfidence) * (x - view.MeanConfidence)) / Math.Max(1, confidence.Count - 1));
                view.BelowTwenty = (double)confidence.Count(x => x < 20) / confidence.Count;
                m.Seasons.Add(view);
            }

            m.ClubReputationAfter = ClubMeans(w, divisions);
            m.ManagerReputationAfter = ManagerMeans(w, divisions);
            return m;
        }

        private static double[] ClubMeans(World w, int divisions) =>
            Enumerable.Range(1, divisions).Select(d => w.Clubs.Where(c => c.Division == d).Select(c => (double)c.Reputation).DefaultIfEmpty(0).Average()).ToArray();

        private static double[] ManagerMeans(World w, int divisions) =>
            Enumerable.Range(1, divisions).Select(d => w.Managers.Where(m => w.Clubs.Any(c => c.Id == m.ClubId && c.Division == d)).Select(m => (double)m.Reputation).DefaultIfEmpty(0).Average()).ToArray();

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons)
        {
            Measurement m = Measure(seed, seasons);
            int places = m.Seasons.Count > 0 ? m.Seasons[0].PointsByRank.Length : 0, divisions = m.ClubReputationAfter.Length;
            var t = new StringBuilder();
            t.AppendLine(FormattableString.Invariant($"A headless career from seed {seed:x}, {seasons} season{(seasons == 1 ? "" : "s")}, each turned:"));
            t.AppendLine();
            t.AppendLine("Points a match by expected place (the wage rank), every division's clubs together:");
            t.AppendLine();
            t.AppendLine("| Season | " + string.Join(" | ", Enumerable.Range(1, places)) + " |");
            t.AppendLine("| --- | " + string.Join(" | ", Enumerable.Repeat("---", places)) + " |");
            foreach (SeasonView v in m.Seasons) t.AppendLine(FormattableString.Invariant($"| {v.Season} | {string.Join(" | ", v.PointsByRank.Select(p => p.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))} |"));
            if (m.Seasons.Count > 0)
            {
                IEnumerable<double> mean = Enumerable.Range(0, places).Select(k => m.Seasons.Average(v => v.PointsByRank[k]));
                t.AppendLine(FormattableString.Invariant($"| All | {string.Join(" | ", mean.Select(p => p.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))} |"));
            }

            t.AppendLine();
            t.AppendLine("The board's verdicts, and confidence at each season's end:");
            t.AppendLine();
            t.AppendLine("| Season | Met their league objective | " + string.Join(" | ", Enumerable.Range(1, divisions).Select(d => $"Division {d}")) + " | Confidence | Below 20 |");
            t.AppendLine("| --- | --- | " + string.Join(" | ", Enumerable.Repeat("---", divisions)) + " | --- | --- |");
            foreach (SeasonView v in m.Seasons)
            {
                t.AppendLine(FormattableString.Invariant($"| {v.Season} | {v.Met} of {v.Clubs} | {string.Join(" | ", v.MetByDivision.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)))} | {v.MeanConfidence:F0} ± {v.ConfidenceSpread:F0} | {100 * v.BelowTwenty:F0} % |"));
            }

            t.AppendLine();
            t.AppendLine("Mean reputation by division at each season's start, clubs then managers:");
            t.AppendLine();
            t.AppendLine("| Season | " + string.Join(" | ", Enumerable.Range(1, divisions).Select(d => $"Clubs {d}")) + " | " + string.Join(" | ", Enumerable.Range(1, divisions).Select(d => $"Managers {d}")) + " |");
            t.AppendLine("| --- | " + string.Join(" | ", Enumerable.Repeat("---", 2 * divisions)) + " |");
            string Row(string label, double[] clubs, double[] managers) =>
                FormattableString.Invariant($"| {label} | {string.Join(" | ", clubs.Concat(managers).Select(x => x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))} |");
            foreach (SeasonView v in m.Seasons) t.AppendLine(Row(v.Season.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ClubReputation, v.ManagerReputation));
            t.AppendLine(Row(FormattableString.Invariant($"After {seasons}"), m.ClubReputationAfter, m.ManagerReputationAfter));
            t.AppendLine(Row("Drift", Enumerable.Range(1, divisions).Select(m.ClubDrift).ToArray(), Enumerable.Range(1, divisions).Select(m.ManagerDrift).ToArray()));
            return t.ToString().Replace("\r\n", "\n");
        }
    }
}
