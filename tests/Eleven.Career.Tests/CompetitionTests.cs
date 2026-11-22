using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #162: the competitions as data. A league season is a double round robin of 22 rounds with
    /// balanced, mirrored grounds; the cup takes 36 clubs through a preliminary round and five more
    /// to a neutral final, each round drawn from its own seed; a tie's winner comes from the
    /// match-result contract; and the calendar puts the cup's rounds between the league's.
    /// </summary>
    public class CompetitionTests
    {
        private static readonly int[] Twelve = Enumerable.Range(101, 12).ToArray();

        private static List<Fixture> League(ulong seed = 0x162, int season = 1, CompetitionId division = CompetitionId.Division1) =>
            LeagueFixtures.Generate(Twelve, seed, season, division);

        private static string Describe(IEnumerable<Fixture> fixtures) =>
            string.Join(";", fixtures.Select(f => $"{f.Id}:{f.Round}:{f.Index}:{f.Home}-{f.Away}{(f.Neutral ? "n" : "")}"));

        [Fact]
        public void The_rules_are_the_roadmaps()
        {
            CompetitionRules r = CompetitionRules.Reference;
            Assert.Equal(12, r.LeagueClubs);
            Assert.Equal(22, r.LeagueRounds);
            Assert.Equal((3, 1, 0), (r.PointsForWin, r.PointsForDraw, r.PointsForLoss));
            Assert.Equal((2, 2), (r.Promoted, r.Relegated));
            Assert.Equal(36, r.CupEntrants);
            Assert.True(r.CupExtraTimeAndPenalties);
            Assert.True(r.CupNeutralFinal);
            Assert.Equal(6, CompetitionRules.CupRounds.Count);
        }

        [Fact]
        public void A_league_season_is_22_rounds_of_6_with_every_club_playing_once_a_round()
        {
            List<Fixture> f = League();
            Assert.Equal(132, f.Count);
            for (int r = 1; r <= 22; r++)
            {
                List<Fixture> round = f.Where(x => x.Round == r).ToList();
                Assert.Equal(6, round.Count);
                Assert.Equal(12, round.SelectMany(x => new[] { x.Home, x.Away }).Distinct().Count());
                Assert.Equal(Enumerable.Range(0, 6), round.Select(x => x.Index).OrderBy(i => i));
                Assert.All(round, x => Assert.Equal(CompetitionId.Division1, x.Competition));
                Assert.All(round, x => Assert.False(x.Neutral));
            }
        }

        [Fact]
        public void Every_club_meets_every_other_once_at_home_and_once_away()
        {
            List<Fixture> f = League();
            Assert.NotEmpty(f);
            var pairs = new HashSet<(int, int)>(f.Select(x => (x.Home, x.Away)));
            Assert.Equal(132, pairs.Count);
            foreach (int a in Twelve)
            {
                foreach (int b in Twelve)
                {
                    if (a != b) Assert.Contains((a, b), pairs);
                }

                Assert.Equal(11, f.Count(x => x.Home == a));
            }
        }

        [Fact]
        public void No_club_plays_three_in_a_row_at_home_or_away()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 0x162 })
            {
                List<Fixture> f = League(seed);
                Assert.NotEmpty(f);
                foreach (int club in Twelve)
                {
                    string grounds = string.Concat(f.Where(x => x.Home == club || x.Away == club).OrderBy(x => x.Round).Select(x => x.Home == club ? 'H' : 'A'));
                    Assert.Equal(22, grounds.Length);
                    Assert.DoesNotContain("HHH", grounds);
                    Assert.DoesNotContain("AAA", grounds);
                }
            }
        }

        [Fact]
        public void The_second_half_mirrors_the_first_with_the_grounds_swapped()
        {
            List<Fixture> f = League();
            Assert.NotEmpty(f);
            for (int r = 1; r <= 11; r++)
            {
                var first = new HashSet<(int, int)>(f.Where(x => x.Round == r).Select(x => (x.Away, x.Home)));
                var second = new HashSet<(int, int)>(f.Where(x => x.Round == r + 11).Select(x => (x.Home, x.Away)));
                Assert.Equal(6, first.Count);
                Assert.True(first.SetEquals(second), $"round {r + 11} isn't round {r} reversed");
            }
        }

        [Fact]
        public void A_seed_and_a_season_shuffle_the_schedule_and_the_same_inputs_give_the_same_list()
        {
            Assert.NotEmpty(League());
            Assert.Equal(Describe(League(1, 1)), Describe(League(1, 1)));
            Assert.NotEqual(Describe(League(1, 1)), Describe(League(2, 1)));
            Assert.NotEqual(Describe(League(1, 1)), Describe(League(1, 2)));
        }

        [Fact]
        public void Fixture_ids_are_stable_and_unique()
        {
            var all = new List<Fixture>();
            all.AddRange(League(division: CompetitionId.Division1));
            all.AddRange(League(division: CompetitionId.Division2));
            all.AddRange(League(division: CompetitionId.Division3));
            all.AddRange(League(season: 2));
            all.AddRange(Cup.Draw(Enumerable.Range(1, 32).ToList(), 0x162, 1, 2));
            Assert.Equal(132 * 4 + 16, all.Count);
            Assert.Equal(all.Count, all.Select(x => x.Id).Distinct().Count());
            Assert.All(all, x => Assert.Equal(Fixture.MakeId(x.Season, x.Competition, x.Round, x.Index), x.Id));
            Assert.NotEqual(Fixture.MakeId(1, CompetitionId.Cup, 1, 0), Fixture.MakeId(1, CompetitionId.Cup, 1, 1));
        }

        private static readonly int[] ThirtySix = Enumerable.Range(1, 36).ToArray();

        [Fact]
        public void The_eight_lowest_ranked_entrants_play_the_preliminary_round_and_the_rest_have_byes()
        {
            (List<int> preliminary, List<int> byes) = Cup.FirstRound(ThirtySix);
            Assert.Equal(Enumerable.Range(29, 8), preliminary.OrderBy(i => i));
            Assert.Equal(Enumerable.Range(1, 28), byes.OrderBy(i => i));
        }

        [Fact]
        public void A_rounds_draw_pairs_every_club_once_from_its_own_seed()
        {
            List<int> eight = Enumerable.Range(29, 8).ToList();
            List<Fixture> d = Cup.Draw(eight, 0x162, 1, 1);
            Assert.Equal(4, d.Count);
            Assert.Equal(eight, d.SelectMany(x => new[] { x.Home, x.Away }).OrderBy(i => i));
            Assert.All(d, x => Assert.Equal(CompetitionId.Cup, x.Competition));
            Assert.All(d, x => Assert.Equal(1, x.Round));
            Assert.All(d, x => Assert.False(x.Neutral));
            Assert.Equal(Enumerable.Range(0, 4), d.Select(x => x.Index));
            Assert.Equal(Describe(d), Describe(Cup.Draw(eight, 0x162, 1, 1)));

            // Each round, season and seed draws for itself: some of them pair the clubs otherwise.
            string Pairs(List<Fixture> x) => string.Join(";", x.Select(t => $"{Math.Min(t.Home, t.Away)}-{Math.Max(t.Home, t.Away)}").OrderBy(s => s));
            var draws = new HashSet<string> { Pairs(d), Pairs(Cup.Draw(eight, 0x163, 1, 1)), Pairs(Cup.Draw(eight, 0x162, 2, 1)), Pairs(Cup.Draw(eight, 0x162, 1, 2)) };
            Assert.True(draws.Count > 1, "every draw paired the clubs the same way");
        }

        private static MatchResult Result(int home, int away, int homePens = 0, int awayPens = 0) =>
            new MatchResult(1, 2, home, away, 180, 360, Array.Empty<ResultGoal>(), 0,
                extraTime: home == away ? 1 : 0, homeShootoutKicks: homePens + awayPens > 0 ? 5 : 0, awayShootoutKicks: homePens + awayPens > 0 ? 5 : 0,
                homeShootoutGoals: homePens, awayShootoutGoals: awayPens);

        [Fact]
        public void The_cup_takes_36_clubs_through_six_rounds_to_a_neutral_final()
        {
            (List<int> preliminary, List<int> byes) = Cup.FirstRound(ThirtySix);
            Assert.NotEmpty(preliminary);
            List<int> entrants = preliminary;
            int ties = 0;
            int round = 1;
            Fixture final = null;
            for (; entrants.Count > 1; round++)
            {
                List<Fixture> draw = Cup.Draw(entrants, 0x162, 1, round);
                Assert.Equal(entrants.Count / 2, draw.Count);
                ties += draw.Count;
                // The home side always wins here, so the draw decides who goes through.
                List<int> winners = draw.Select(t => Cup.Winner(t, Result(1, 0))).ToList();
                entrants = round == 1 ? byes.Concat(winners).ToList() : winners;
                if (entrants.Count == 1) final = draw.Single();
                else Assert.All(draw, t => Assert.False(t.Neutral));
            }

            Assert.Equal(7, round);
            Assert.Equal(35, ties);
            Assert.NotNull(final);
            Assert.True(final.Neutral);
        }

        [Fact]
        public void A_ties_winner_is_read_from_the_result_and_its_shootout()
        {
            var tie = new Fixture { Home = 5, Away = 9 };
            Assert.Equal(5, Cup.Winner(tie, Result(2, 1)));
            Assert.Equal(9, Cup.Winner(tie, Result(0, 1)));
            Assert.Equal(9, Cup.Winner(tie, Result(1, 1, 4, 5)));
            Assert.Equal(5, Cup.Winner(tie, Result(2, 2, 3, 1)));
            Assert.Throws<InvalidOperationException>(() => Cup.Winner(tie, Result(1, 1)));
        }

        [Fact]
        public void The_calendar_puts_the_six_cup_rounds_between_the_22_league_rounds_and_the_final_last()
        {
            List<Matchday> days = SeasonCalendar.Of(CompetitionRules.Reference);
            Assert.Equal(28, days.Count);
            Assert.Equal(Enumerable.Range(1, 22), days.Where(d => d.Kind == MatchdayKind.League).Select(d => d.Round));
            Assert.Equal(Enumerable.Range(1, 6), days.Where(d => d.Kind == MatchdayKind.Cup).Select(d => d.Round));
            Assert.Equal(MatchdayKind.League, days[0].Kind);
            Assert.Equal(MatchdayKind.Cup, days[days.Count - 1].Kind);
            Assert.Equal(MatchdayKind.League, days[days.Count - 2].Kind);
            for (int i = 1; i < days.Count; i++)
            {
                Assert.False(days[i].Kind == MatchdayKind.Cup && days[i - 1].Kind == MatchdayKind.Cup, $"cup rounds back to back at matchday {i + 1}");
            }
        }
    }
}
