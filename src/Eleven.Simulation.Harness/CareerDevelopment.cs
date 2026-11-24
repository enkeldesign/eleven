using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

            /// <summary>The turn's releases and free signings (#201): with the clubs' retirements, what the youth replace.</summary>
            public int Released, Signed;

            /// <summary>The squads' places short of 23 at the last season's end (#203): mid-season sales the youth replace too.</summary>
            public int ShortAtEnd;

            /// <summary>The followed footballers' levels this season, by id; absent once retired.</summary>
            public Dictionary<int, (int Age, double Level)> Followed = new Dictionary<int, (int, double)>();
        }

        private static readonly (string Name, int From, int To)[] AgeBands =
        {
            ("16–18", 16, 18), ("19–21", 19, 21), ("22–24", 22, 24), ("25–27", 25, 27), ("28–30", 28, 30), ("31–33", 31, 33), ("34+", 34, 99),
        };

        /// <summary><paramref name="seasons"/> seasons of a new career, headless, the human at the first club: each season's start.</summary>
        public static List<SeasonView> Measure(ulong seed, int seasons) => Measure(seed, seasons, out _);

        private static List<SeasonView> Measure(ulong seed, int seasons, out List<(int Id, string Who)> followed)
        {
            World w = CareerSession.NewWorld(seed);
            CareerSession s = CareerSession.Start(w, seed, w.Clubs[0].Id, CareerRole.ManagerOnly, SpikeScenario.Config());
            followed = Follow(w);
            var views = new List<SeasonView>(seasons);
            int shortAtEnd = 0;
            for (int i = 1; i <= seasons; i++)
            {
                SeasonView view = View(w, s.Season.Number, followed);
                view.ShortAtEnd = shortAtEnd;
                views.Add(view);
                while (!s.SeasonOver) s.Simulate();
                shortAtEnd = w.Clubs.Sum(c => WorldGenerator.SquadSize - w.Players.Count(p => p.ClubId == c.Id));
                if (i < seasons) s.NextSeason();
            }

            return views;
        }

        // Five footballers to follow: outfielders of 17, 23, 29 and 33, and a keeper of 34 or more, the first of each.
        private static List<(int Id, string Who)> Follow(World w)
        {
            var picks = new List<(int, string)>();
            foreach (int age in new[] { 17, 23, 29, 33 })
            {
                Player? p = w.Players.Find(x => x.Age == age && x.Positions[0] != Position.Goalkeeper);
                if (p != null) picks.Add((p.Id, $"{Name(p)}, {p.Positions[0]}"));
            }

            Player? keeper = w.Players.Where(x => x.Positions[0] == Position.Goalkeeper && x.Age >= 34).OrderBy(x => x.Age).ThenBy(x => x.Id).FirstOrDefault();
            if (keeper != null) picks.Add((keeper.Id, $"{Name(keeper)}, keeper"));
            return picks;
        }

        private static string Name(Player p) => (p.FirstName.Length > 0 ? p.FirstName.Substring(0, 1) + ". " : "") + p.LastName;

        private static SeasonView View(World w, int season, List<(int Id, string Who)> followed)
        {
            // The clubs' footballers: the free agents (#201) are no club's.
            List<Player> at = w.Players.Where(p => p.ClubId != 0).ToList();
            var v = new SeasonView { Season = season, MeanLevel = at.Average(Development.Level) };
            foreach ((string name, int from, int to) in AgeBands)
            {
                List<double> levels = at.Where(p => p.Age >= from && p.Age <= to).Select(Development.Level).ToList();
                double mean = levels.Count > 0 ? levels.Average() : 0;
                double spread = levels.Count > 1 ? Eleven.Simulation.Maths.SimMath.Sqrt(levels.Sum(x => (x - mean) * (x - mean)) / (levels.Count - 1)) : 0;
                v.Bands.Add((name, levels.Count, mean, spread));
            }

            v.Retired = w.LastRetired.Count(r => r.ClubId != 0);
            v.Intake = w.Players.Count(p => p.Joined == season);
            v.Released = w.LastMoves.Count(m => m.To == 0);
            v.Signed = w.LastMoves.Count(m => m.From == 0);
            List<int> sizes = w.Clubs.Select(c => w.Players.Count(p => p.ClubId == c.Id)).ToList();
            v.SmallestSquad = sizes.Min();
            v.LargestSquad = sizes.Max();
            foreach ((int id, _) in followed)
            {
                Player? p = w.Players.Find(x => x.Id == id);
                if (p != null) v.Followed[id] = (p.Age, Development.Level(p));
            }

            return v;
        }

        /// <summary>The measurement as text: each season's start, its age bands, and the footballers followed.</summary>
        public static string Report(ulong seed, int seasons)
        {
            List<SeasonView> views = Measure(seed, seasons, out List<(int Id, string Who)> followed);
            var t = new StringBuilder();
            double start = views[0].MeanLevel;
            t.AppendLine("| Season | Mean level | Its drift from the start | Retired at its turn | Youth | Squads |");
            t.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (SeasonView v in views)
            {
                t.AppendLine(FormattableString.Invariant($"| {v.Season} | {v.MeanLevel:F2} | {v.MeanLevel - start:+0.00;−0.00;0.00} | {v.Retired} | {v.Intake} | {v.SmallestSquad}–{v.LargestSquad} |"));
            }

            t.AppendLine();
            t.Append("| Age |");
            foreach (SeasonView v in views) t.Append(FormattableString.Invariant($" Season {v.Season} |"));
            t.AppendLine();
            t.Append("| --- |");
            foreach (SeasonView _ in views) t.Append(" --- |");
            t.AppendLine();
            for (int b = 0; b < AgeBands.Length; b++)
            {
                t.Append($"| {AgeBands[b].Name} |");
                foreach (SeasonView v in views)
                {
                    (_, int count, double mean, double spread) = v.Bands[b];
                    t.Append(count > 0 ? FormattableString.Invariant($" {count}: {mean:F1} ± {spread:F1} |") : " — |");
                }

                t.AppendLine();
            }

            t.AppendLine();
            t.Append("| Followed |");
            foreach (SeasonView v in views) t.Append(FormattableString.Invariant($" Season {v.Season} |"));
            t.AppendLine();
            t.Append("| --- |");
            foreach (SeasonView _ in views) t.Append(" --- |");
            t.AppendLine();
            foreach ((int id, string who) in followed)
            {
                t.Append($"| {who} |");
                foreach (SeasonView v in views)
                {
                    t.Append(v.Followed.TryGetValue(id, out (int Age, double Level) f) ? FormattableString.Invariant($" {f.Age}: {f.Level:F2} |") : " retired |");
                }

                t.AppendLine();
            }

            return t.ToString().Replace("\r\n", "\n");
        }
    }
}
