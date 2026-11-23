using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #164: the season engine. Tables order by points, goal difference, goals, head to head and a
    /// drawn lot; a season runs its 28 matchdays, the cup drawn as it comes up; results are refused
    /// out of turn; the season ends with two up and two down; and rollover starts the next.
    /// </summary>
    public class SeasonTests
    {
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;

        private static MatchResult Result(int home, int away, int homePens = 0, int awayPens = 0) =>
            new MatchResult(1, 2, home, away, 180, 360, Array.Empty<ResultGoal>(), 0,
                extraTime: homePens + awayPens > 0 ? 1 : 0, homeShootoutKicks: homePens + awayPens > 0 ? 5 : 0, awayShootoutKicks: homePens + awayPens > 0 ? 5 : 0,
                homeShootoutGoals: homePens, awayShootoutGoals: awayPens);

        // A one-division season of the given clubs with these results already in.
        private static Season Played(int[] clubs, params (int Home, int Away, int HomeGoals, int AwayGoals)[] matches)
        {
            var s = new Season { Number = 1, Seed = 0x164 };
            s.Divisions.Add(clubs.ToList());
            for (int i = 0; i < matches.Length; i++)
            {
                var f = new Fixture { Id = 1000 + i, Season = 1, Competition = CompetitionId.Division1, Round = i + 1, Home = matches[i].Home, Away = matches[i].Away };
                s.Fixtures.Add(f);
                s.Results[f.Id] = Result(matches[i].HomeGoals, matches[i].AwayGoals);
            }

            return s;
        }

        private static int[] Order(Season s) => SeasonEngine.Table(s, CompetitionId.Division1, Rules).Select(r => r.Club).ToArray();

        [Fact]
        public void A_table_orders_by_points_then_goal_difference_then_goals()
        {
            // 1: W 3–0, D 0–0: 4 points, +3. 2: W 1–0, D 0–0: 4 points, +1. 3: L 0–3, W 4–2, D 0–0:
            // 4 points, −1. 4: L 0–1, L 2–4, D 0–0: 1 point.
            Season s = Played(new[] { 1, 2, 3, 4 }, (1, 3, 3, 0), (2, 4, 1, 0), (1, 2, 0, 0), (3, 4, 4, 2), (4, 3, 0, 0));
            List<TableRow> t = SeasonEngine.Table(s, CompetitionId.Division1, Rules);
            Assert.Equal(4, t.Count);
            TableRow one = t.Single(r => r.Club == 1), three = t.Single(r => r.Club == 3);
            Assert.Equal((2, 1, 1, 0, 3, 0, 4), (one.Played, one.Won, one.Drawn, one.Lost, one.For, one.Against, one.Points));
            Assert.Equal((3, 1, 1, 1, 4, 5, 4), (three.Played, three.Won, three.Drawn, three.Lost, three.For, three.Against, three.Points));
            // 1 (4 pts, +3), 2 (4 pts, +1), 3 (4 pts, −1), 4 (1 pt).
            Assert.Equal(new[] { 1, 2, 3, 4 }, Order(s));

            // Level on points and goal difference: more goals first.
            Season g = Played(new[] { 5, 6, 7 }, (5, 7, 3, 2), (6, 7, 1, 0));
            Assert.Equal(new[] { 5, 6, 7 }, Order(g));
        }

        [Fact]
        public void Clubs_level_on_points_difference_and_goals_are_split_head_to_head()
        {
            // 8 wins twice and loses to 9. 9 and 11 each beat one club 2–1 and lose 1–2 to another:
            // level on points, difference and goals, but 11 beat 9.
            Season s = Played(new[] { 8, 9, 10, 11 }, (8, 10, 2, 1), (9, 8, 2, 1), (11, 9, 2, 1), (8, 11, 2, 1));
            int[] order = Order(s);
            Assert.Equal(4, order.Length);
            TableRow r8 = SeasonEngine.Table(s, CompetitionId.Division1, Rules).Single(r => r.Club == 8);
            TableRow r9 = SeasonEngine.Table(s, CompetitionId.Division1, Rules).Single(r => r.Club == 9);
            Assert.Equal((6, 1), (r8.Points, r8.GoalDifference));
            Assert.Equal(3, r9.Points);
            Assert.Equal(new[] { 8, 11, 9, 10 }, order);
        }

        [Fact]
        public void Clubs_level_on_everything_are_split_by_a_lot_drawn_from_the_seed()
        {
            int[] clubs = { 21, 22 };
            var orders = new HashSet<string>();
            for (ulong seed = 0; seed < 20; seed++)
            {
                Season s = Played(clubs, (21, 22, 1, 1), (22, 21, 0, 0));
                s.Seed = seed;
                int[] order = Order(s);
                Assert.Equal(order, Order(s));
                orders.Add(string.Join(",", order));
            }

            Assert.Equal(2, orders.Count);
        }

        private static World NewWorld() => WorldGenerator.Generate(0x157);

        // A result for every fixture: the lower id wins 1–0 at home, and away a cup tie on penalties.
        private static MatchResult Scripted(Fixture f) =>
            f.Competition == CompetitionId.Cup
                ? (f.Home < f.Away ? Result(1, 0) : Result(0, 0, 3, 4))
                : (f.Home < f.Away ? Result(1, 0) : Result(0, 0));

        private static Season PlayScripted(Season s)
        {
            int matchdays = 0;
            while (!SeasonEngine.Finished(s, Rules))
            {
                List<Fixture> day = SeasonEngine.Next(s, Rules);
                Assert.NotEmpty(day);
                Assert.Equal(day.Select(f => f.Id), SeasonEngine.Next(s, Rules).Select(f => f.Id));
                foreach (Fixture f in day) SeasonEngine.Record(s, Rules, f.Id, Scripted(f));
                SeasonEngine.Advance(s, Rules);
                matchdays++;
                Assert.True(matchdays <= 28, "more than 28 matchdays");
            }

            Assert.Equal(28, matchdays);
            return s;
        }

        [Fact]
        public void A_season_runs_its_28_matchdays_with_the_cup_drawn_as_it_comes_up()
        {
            World w = NewWorld();
            Season s = SeasonEngine.Start(w, 1, 0xCA4EE5, SeasonEngine.FirstRanking(w), Rules);
            Assert.Equal(3, s.Divisions.Count);
            Assert.All(s.Divisions, d => Assert.Equal(12, d.Count));
            Assert.Equal(396, s.Fixtures.Count);
            PlayScripted(s);

            List<Fixture> league = s.Fixtures.Where(f => f.Competition != CompetitionId.Cup).ToList(), cup = s.Fixtures.Where(f => f.Competition == CompetitionId.Cup).ToList();
            Assert.Equal(396, league.Count);
            Assert.Equal(35, cup.Count);
            Assert.Equal(s.Fixtures.Count, s.Results.Count);
            foreach (Club c in w.Clubs) Assert.Equal(22, league.Count(f => f.Home == c.Id || f.Away == c.Id));
            Assert.Equal(new[] { 4, 16, 8, 4, 2, 1 }, Enumerable.Range(1, 6).Select(r => cup.Count(f => f.Round == r)));
            Assert.True(cup.Single(f => f.Round == 6).Neutral);
            Assert.Contains(s.CupWinner, w.Clubs.Select(c => c.Id));
            Fixture final = cup.Single(f => f.Round == 6);
            Assert.Equal(Cup.Winner(final, s.Results[final.Id]), s.CupWinner);

            // The tables add up: every point a result's.
            foreach (CompetitionId d in new[] { CompetitionId.Division1, CompetitionId.Division2, CompetitionId.Division3 })
            {
                List<TableRow> t = SeasonEngine.Table(s, d, Rules);
                Assert.Equal(12, t.Count);
                Assert.All(t, r => Assert.Equal(22, r.Played));
                Assert.All(t, r => Assert.Equal(r.Played, r.Won + r.Drawn + r.Lost));
                Assert.All(t, r => Assert.Equal(3 * r.Won + r.Drawn, r.Points));
                Assert.Equal(t.Sum(r => r.For), t.Sum(r => r.Against));
            }
        }

        [Fact]
        public void Results_out_of_turn_and_level_cup_ties_are_refused()
        {
            World w = NewWorld();
            Season s = SeasonEngine.Start(w, 1, 0xCA4EE5, SeasonEngine.FirstRanking(w), Rules);
            List<Fixture> day = SeasonEngine.Next(s, Rules);
            Assert.NotEmpty(day);
            Fixture later = s.Fixtures.First(f => f.Round == 2);
            Assert.Throws<InvalidOperationException>(() => SeasonEngine.Record(s, Rules, later.Id, Result(1, 0)));
            Assert.Throws<InvalidOperationException>(() => SeasonEngine.Advance(s, Rules));
            foreach (Fixture f in day) SeasonEngine.Record(s, Rules, f.Id, Result(1, 1));
            SeasonEngine.Advance(s, Rules);

            // On to the first cup round: a level tie is refused.
            while (SeasonEngine.Next(s, Rules)[0].Competition != CompetitionId.Cup)
            {
                foreach (Fixture f in SeasonEngine.Next(s, Rules)) SeasonEngine.Record(s, Rules, f.Id, Result(1, 1));
                SeasonEngine.Advance(s, Rules);
            }

            Fixture tie = SeasonEngine.Next(s, Rules)[0];
            Assert.Throws<InvalidOperationException>(() => SeasonEngine.Record(s, Rules, tie.Id, Result(2, 2)));
        }

        [Fact]
        public void The_season_ends_with_two_up_and_two_down_and_rolls_over()
        {
            World w = NewWorld();
            Season s = PlayScripted(SeasonEngine.Start(w, 1, 0xCA4EE5, SeasonEngine.FirstRanking(w), Rules));
            List<int>[] tables = new[] { CompetitionId.Division1, CompetitionId.Division2, CompetitionId.Division3 }
                .Select(d => SeasonEngine.Table(s, d, Rules).Select(r => r.Club).ToList()).ToArray();
            (List<int> up, List<int> down) = SeasonEngine.Movement(s, Rules);
            Assert.Equal(tables[1].Take(2).Concat(tables[2].Take(2)).OrderBy(i => i), up.OrderBy(i => i));
            Assert.Equal(tables[0].Skip(10).Concat(tables[1].Skip(10)).OrderBy(i => i), down.OrderBy(i => i));

            Season next = SeasonEngine.Rollover(w, s, 0xCA4EE5, Rules);
            Assert.Equal(2, next.Number);
            Assert.NotEqual(s.Seed, next.Seed);
            Assert.Equal(tables[0].Concat(tables[1]).Concat(tables[2]), next.CupRanking);
            for (int d = 1; d <= 3; d++) Assert.Equal(12, w.Clubs.Count(c => c.Division == d));
            Assert.All(tables[1].Take(2), id => Assert.Equal(1, w.Clubs.Single(c => c.Id == id).Division));
            Assert.All(tables[0].Skip(10), id => Assert.Equal(2, w.Clubs.Single(c => c.Id == id).Division));
            Assert.All(tables[2].Take(2), id => Assert.Equal(2, w.Clubs.Single(c => c.Id == id).Division));
            Assert.All(tables[1].Skip(10), id => Assert.Equal(3, w.Clubs.Single(c => c.Id == id).Division));
            Assert.Equal(w.Clubs.Where(c => c.Division == 1).Select(c => c.Id).OrderBy(i => i), next.Divisions[0].OrderBy(i => i));
            Assert.Equal(396, next.Fixtures.Count);
            Assert.Empty(next.Results);
        }

        [Fact]
        public void A_season_is_the_same_season_for_the_same_career_seed()
        {
            World a = NewWorld(), b = NewWorld();
            Season x = SeasonEngine.Start(a, 1, 7, SeasonEngine.FirstRanking(a), Rules), y = SeasonEngine.Start(b, 1, 7, SeasonEngine.FirstRanking(b), Rules);
            Assert.NotEmpty(x.Fixtures);
            Assert.Equal(x.Seed, y.Seed);
            Assert.Equal(x.Fixtures.Select(f => (f.Id, f.Home, f.Away)), y.Fixtures.Select(f => (f.Id, f.Home, f.Away)));
            Assert.NotEqual(x.Seed, SeasonEngine.Start(a, 1, 8, SeasonEngine.FirstRanking(a), Rules).Seed);
            List<int> ranking = SeasonEngine.FirstRanking(a);
            Assert.Equal(36, ranking.Count);
            Assert.Equal(a.Clubs.OrderBy(c => c.Division).ThenByDescending(c => c.Reputation).ThenBy(c => c.Id).Select(c => c.Id), ranking);
        }
    }
}
