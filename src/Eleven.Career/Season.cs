#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>A club's line in a league table.</summary>
    public sealed class TableRow
    {
        public int Club;
        public int Played, Won, Drawn, Lost, For, Against, Points;

        public int GoalDifference => For - Against;
    }

    /// <summary>
    /// A season as plain data (#164): its number and seed, each division's clubs, the league's
    /// fixtures and the cup's ties drawn so far, every result by fixture id, the next matchday,
    /// and the clubs still in the cup.
    /// </summary>
    public sealed class Season
    {
        public int Number;
        public ulong Seed;

        /// <summary>Each division's clubs: division 1 first.</summary>
        public List<List<int>> Divisions = new List<List<int>>();

        /// <summary>The entrants to the cup, strongest first: last season's final tables.</summary>
        public List<int> CupRanking = new List<int>();

        /// <summary>The league's fixtures, then the cup's ties as each round is drawn.</summary>
        public List<Fixture> Fixtures = new List<Fixture>();

        public Dictionary<int, MatchResult> Results = new Dictionary<int, MatchResult>();

        /// <summary>The next matchday: its place in the calendar.</summary>
        public int Matchday;

        /// <summary>The clubs with a bye, waiting for the round of 32, and then the cup's survivors.</summary>
        public List<int> CupAlive = new List<int>();

        /// <summary>The cup's winner once its final is played, else 0.</summary>
        public int CupWinner;
    }

    /// <summary>
    /// The season engine (#164): the season's matchdays in turn, each league round in every
    /// division or a cup round drawn as it comes up; the results recorded; the tables; the end
    /// with its promotion and relegation; and the rollover into the next season.
    /// </summary>
    public static class SeasonEngine
    {
        /// <summary>
        /// A new season for the world's clubs in their divisions: each division's double round
        /// robin, and the cup to come, ranked by <paramref name="cupRanking"/>. Its seed comes from
        /// the career's and the season's number, so the same career has the same seasons.
        /// </summary>
        public static Season Start(World world, int number, ulong careerSeed, IReadOnlyList<int> cupRanking, CompetitionRules rules)
        {
            var season = new Season { Number = number, Seed = CounterRng.Hash(careerSeed, number, RandomStream.Competition, SeasonEntity, 0) };
            int divisions = 0;
            foreach (Club c in world.Clubs) divisions = Math.Max(divisions, c.Division);
            for (int d = 1; d <= divisions; d++)
            {
                var clubs = new List<int>();
                foreach (Club c in world.Clubs)
                {
                    if (c.Division == d) clubs.Add(c.Id);
                }

                clubs.Sort();
                if (clubs.Count != rules.LeagueClubs) throw new InvalidOperationException($"Division {d} has {clubs.Count} clubs; the league has {rules.LeagueClubs}.");
                season.Divisions.Add(clubs);
                season.Fixtures.AddRange(LeagueFixtures.Generate(clubs, season.Seed, number, (CompetitionId)d));
            }

            season.CupRanking.AddRange(cupRanking);
            season.CupAlive.AddRange(Cup.FirstRound(cupRanking).Byes);

            // The board's objectives (#200), from each club's wage rank in its division.
            Board.Objectives(world, rules);
            return season;
        }

        // The competition stream's entities beyond the competitions' own: a season's seed, and a table's lots.
        private const int SeasonEntity = 99, LotEntity = 20;

        /// <summary>The first season's cup ranking: by division, then reputation, highest first, then id.</summary>
        public static List<int> FirstRanking(World world)
        {
            var clubs = new List<Club>(world.Clubs);
            clubs.Sort((a, b) => a.Division != b.Division ? a.Division.CompareTo(b.Division)
                : a.Reputation != b.Reputation ? b.Reputation.CompareTo(a.Reputation)
                : a.Id.CompareTo(b.Id));
            return clubs.ConvertAll(c => c.Id);
        }

        public static bool Finished(Season season, CompetitionRules rules) => season.Matchday >= SeasonCalendar.Of(rules).Count;

        /// <summary>
        /// The next matchday's fixtures: a league round in every division, or a cup round, drawn
        /// the first time it is asked for. The preliminary round's clubs are the ranking's lowest;
        /// later rounds draw the clubs still in.
        /// </summary>
        public static List<Fixture> Next(Season season, CompetitionRules rules)
        {
            List<Matchday> calendar = SeasonCalendar.Of(rules);
            if (season.Matchday >= calendar.Count) throw new InvalidOperationException($"Season {season.Number} is over.");
            Matchday day = calendar[season.Matchday];
            var fixtures = new List<Fixture>();
            foreach (Fixture f in season.Fixtures)
            {
                if ((day.Kind == MatchdayKind.Cup) == (f.Competition == CompetitionId.Cup) && f.Round == day.Round) fixtures.Add(f);
            }

            if (day.Kind == MatchdayKind.Cup && fixtures.Count == 0)
            {
                IReadOnlyList<int> entrants = day.Round == 1 ? Cup.FirstRound(season.CupRanking).Preliminary : season.CupAlive;
                fixtures = Cup.Draw(entrants, season.Seed, season.Number, day.Round);
                season.Fixtures.AddRange(fixtures);
            }

            return fixtures;
        }

        /// <summary>A result for one of the next matchday's fixtures; anything else is refused, and so is a level cup tie.</summary>
        public static void Record(Season season, CompetitionRules rules, int fixtureId, MatchResult result)
        {
            Fixture? fixture = null;
            foreach (Fixture f in Next(season, rules))
            {
                if (f.Id == fixtureId) fixture = f;
            }

            if (fixture == null) throw new InvalidOperationException($"Fixture {fixtureId} isn't on matchday {season.Matchday + 1}.");
            if (fixture.Competition == CompetitionId.Cup && result.Winner == 0) throw new InvalidOperationException($"Cup tie {fixtureId} ended level: a cup tie goes to extra time and penalties.");
            season.Results[fixtureId] = result;
        }

        /// <summary>On to the next matchday, once every fixture of this one has its result: a cup round's winners go through.</summary>
        public static void Advance(Season season, CompetitionRules rules)
        {
            List<Fixture> day = Next(season, rules);
            foreach (Fixture f in day)
            {
                if (!season.Results.ContainsKey(f.Id)) throw new InvalidOperationException($"Fixture {f.Id} has no result yet.");
            }

            if (day.Count > 0 && day[0].Competition == CompetitionId.Cup)
            {
                var through = new List<int>();
                if (day[0].Round == 1) through.AddRange(season.CupAlive);
                foreach (Fixture f in day) through.Add(Cup.Winner(f, season.Results[f.Id]));
                season.CupAlive = through;
                if (through.Count == 1) season.CupWinner = through[0];
            }

            season.Matchday++;
        }

        /// <summary>
        /// A division's table: points, then goal difference, then goals scored; clubs level on all
        /// three by head to head among them (points, goal difference, goals); and clubs level on
        /// that too by a lot drawn from the season's seed.
        /// </summary>
        public static List<TableRow> Table(Season season, CompetitionId division, CompetitionRules rules)
        {
            List<int> clubs = season.Divisions[(int)division - 1];
            Dictionary<int, TableRow> rows = Tally(season, division, rules, clubs);
            var table = new List<TableRow>(rows.Values);
            table.Sort((a, b) => Compare(a, b));
            for (int i = 0; i < table.Count;)
            {
                int j = i + 1;
                while (j < table.Count && Compare(table[i], table[j]) == 0) j++;
                if (j - i > 1) Split(season, division, rules, table, i, j - i);
                i = j;
            }

            return table;
        }

        private static int Compare(TableRow a, TableRow b) =>
            a.Points != b.Points ? b.Points.CompareTo(a.Points)
            : a.GoalDifference != b.GoalDifference ? b.GoalDifference.CompareTo(a.GoalDifference)
            : b.For.CompareTo(a.For);

        // Every result of the division among `clubs`, tallied.
        private static Dictionary<int, TableRow> Tally(Season season, CompetitionId division, CompetitionRules rules, IReadOnlyCollection<int> clubs)
        {
            var rows = new Dictionary<int, TableRow>();
            foreach (int c in clubs) rows[c] = new TableRow { Club = c };
            foreach (Fixture f in season.Fixtures)
            {
                if (f.Competition != division || !rows.ContainsKey(f.Home) || !rows.ContainsKey(f.Away) || !season.Results.TryGetValue(f.Id, out MatchResult? r)) continue;
                Count(rows[f.Home], r.HomeGoals, r.AwayGoals, rules);
                Count(rows[f.Away], r.AwayGoals, r.HomeGoals, rules);
            }

            return rows;
        }

        private static void Count(TableRow row, int scored, int conceded, CompetitionRules rules)
        {
            row.Played++;
            row.For += scored;
            row.Against += conceded;
            if (scored > conceded)
            {
                row.Won++;
                row.Points += rules.PointsForWin;
            }
            else if (scored == conceded)
            {
                row.Drawn++;
                row.Points += rules.PointsForDraw;
            }
            else
            {
                row.Lost++;
                row.Points += rules.PointsForLoss;
            }
        }

        // Clubs level on points, difference and goals, at table[start..start+count): by their
        // matches against each other, then by the lot.
        private static void Split(Season season, CompetitionId division, CompetitionRules rules, List<TableRow> table, int start, int count)
        {
            var tied = new List<int>(count);
            for (int i = start; i < start + count; i++) tied.Add(table[i].Club);
            Dictionary<int, TableRow> h2h = Tally(season, division, rules, tied);
            var lot = new Dictionary<int, double>();
            foreach (int c in tied) lot[c] = CounterRng.Uniform(season.Seed, (long)season.Number * 64 + 63, RandomStream.Competition, LotEntity + (int)division, c);
            List<TableRow> group = table.GetRange(start, count);
            group.Sort((a, b) =>
            {
                int byHead = Compare(h2h[a.Club], h2h[b.Club]);
                return byHead != 0 ? byHead : lot[a.Club] != lot[b.Club] ? lot[a.Club].CompareTo(lot[b.Club]) : a.Club.CompareTo(b.Club);
            });
            for (int i = 0; i < count; i++) table[start + i] = group[i];
        }

        /// <summary>Who goes up and who goes down: the top of each division but the first, the bottom of each but the last.</summary>
        public static (List<int> Promoted, List<int> Relegated) Movement(Season season, CompetitionRules rules)
        {
            var up = new List<int>();
            var down = new List<int>();
            for (int d = 1; d <= season.Divisions.Count; d++)
            {
                List<TableRow> table = Table(season, (CompetitionId)d, rules);
                if (d > 1) for (int i = 0; i < rules.Promoted; i++) up.Add(table[i].Club);
                if (d < season.Divisions.Count) for (int i = table.Count - rules.Relegated; i < table.Count; i++) down.Add(table[i].Club);
            }

            return (up, down);
        }

        /// <summary>
        /// The season's end: its money (#198), the board's verdict (#200) and the contracts ending (#201), the promoted clubs up
        /// a division and the relegated down one, in the world itself, every footballer a year
        /// older (#194), and the next season, its cup ranked by this season's final tables.
        /// </summary>
        public static Season Rollover(World world, Season season, ulong careerSeed, CompetitionRules rules, SquadRules? squad = null, DevelopmentRules? development = null, ICollection<int>? excluded = null)
        {
            if (!Finished(season, rules)) throw new InvalidOperationException($"Season {season.Number} isn't over.");
            var ranking = new List<int>();
            for (int d = 1; d <= season.Divisions.Count; d++)
            {
                foreach (TableRow row in Table(season, (CompetitionId)d, rules)) ranking.Add(row.Club);
            }

            (List<int> up, List<int> down) = Movement(season, rules);
            Finance.SeasonEnd(world, season, rules);
            Board.SeasonEnd(world, season, rules);

            // Contracts (#201): those ending renewed or released, the free agents signed, by each club's next division.
            var next = new Dictionary<int, int>();
            foreach (Club c in world.Clubs) next[c.Id] = up.Contains(c.Id) ? c.Division - 1 : down.Contains(c.Id) ? c.Division + 1 : c.Division;
            // The pre-season window (#203) between the releases and the free signings, the human's club out of it until M6.12c.
            Contracts.Turn(world, season.Number + 1, next, between: () => Transfers.Window(world, season.Number + 1, TransferWindow.PreSeason, next, excluded));
            Availability.NewSeason(world, squad ?? SquadRules.Reference);
            Finance.Pass(world, (squad ?? SquadRules.Reference).OffSeasonDays);
            Development.Rollover(world, season.Number + 1, development);
            foreach (Club c in world.Clubs)
            {
                if (up.Contains(c.Id)) c.Division--;
                else if (down.Contains(c.Id)) c.Division++;
            }

            return Start(world, season.Number + 1, careerSeed, ranking, rules);
        }
    }
}
