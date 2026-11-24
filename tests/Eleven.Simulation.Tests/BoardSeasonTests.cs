using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #200: the board in a career. Confidence moves match by match and the turn judges the season
    /// and sets new objectives; the same career twice gives the same board; the hub says the
    /// board's objectives and confidence, and the review its verdict; and the measurement keeps
    /// the board neither easy nor impossible and the reputations steady.
    /// </summary>
    public class BoardSeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(ulong seed, int club = 0)
        {
            World w = CareerSession.NewWorld(seed);
            return CareerSession.Start(w, seed, w.Clubs[club].Id, CareerRole.ManagerOnly, Config);
        }

        [Fact]
        public void A_career_moves_confidence_match_by_match_and_its_turn_judges_the_season_and_sets_new_objectives()
        {
            CareerSession s = New(0x200);
            Assert.All(s.World.Clubs, c => Assert.InRange(c.Board.ExpectedPlace, 1, 12));
            s.Simulate();
            Assert.Contains(s.World.Clubs, c => c.Board.Confidence > BoardRules.FreshStart);
            Assert.Contains(s.World.Clubs, c => c.Board.Confidence < BoardRules.FreshStart);

            while (!s.SeasonOver) s.Simulate();
            s.NextSeason();
            // The new season's objectives, by the divisions its clubs now play in.
            foreach (IGrouping<int, Club> d in s.World.Clubs.GroupBy(c => c.Division))
            {
                Assert.Equal(Enumerable.Range(1, 12), d.Select(c => c.Board.ExpectedPlace).OrderBy(p => p));
            }

            Assert.All(s.World.Clubs, c => Assert.InRange(c.Board.LastPlace, 1, 12));
            Assert.All(s.World.Clubs, c => Assert.InRange(c.Board.Confidence, 0, 100));

            // The review: the human's club's verdict.
            Club mine = s.World.Clubs.Single(c => c.Id == s.Club);
            BoardState b = mine.Board;
            int change = mine.Reputation - b.LastReputation;
            string reputation = change > 0 ? $", up {change}" : change < 0 ? $", down {-change}" : "";
            Assert.Contains(FormattableString.Invariant($"{CareerHub.Ordinal(b.LastPlace)}, {CareerHub.Ordinal(b.LastExpected)} expected: objective {(b.LastPlace <= b.LastTarget ? "met" : "missed")}. Confidence {b.LastConfidence:F0}. Reputation {mine.Reputation}{reputation}"), CareerHub.ReviewLine(s));
        }

        [Fact]
        public void The_same_career_twice_gives_the_same_board()
        {
            string Run()
            {
                CareerSession s = New(0x2001);
                for (int i = 0; i < 12; i++) s.Simulate();
                return string.Join(";", s.World.Clubs.Select(c => FormattableString.Invariant($"{c.Board.ExpectedPlace}.{c.Board.LeagueTarget}.{c.Board.CupTarget}.{c.Board.Confidence:R}")));
            }

            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void The_hub_says_the_boards_objectives_and_its_confidence()
        {
            CareerSession s = New(0x2002, 4);
            Club mine = s.World.Clubs.Single(c => c.Id == s.Club);
            Assert.Equal(1, mine.Division);
            string league = Board.League(mine, 3, s.Rules);
            Assert.Equal(FormattableString.Invariant($"The board: {char.ToLowerInvariant(league[0])}{league.Substring(1)}, {Board.Cup(mine)} and end the season in the black. Confidence {mine.Board.Confidence:F0}: {Board.Words(mine.Board.Confidence)}."), CareerHub.BoardLine(s));

            mine.Board.LeagueTarget = 6;
            mine.Board.CupTarget = 3;
            mine.Board.Confidence = 62.4;
            Assert.Equal("The board: finish 6th or higher, reach the round of 16 and end the season in the black. Confidence 62: pleased.", CareerHub.BoardLine(s));
        }

        [Fact]
        public void The_measurement_keeps_the_board_neither_easy_nor_impossible_and_the_reputations_steady()
        {
            CareerBoard.Measurement m = CareerBoard.Measure(0x200, 3);
            Assert.Equal(3, m.Seasons.Count);
            foreach (CareerBoard.SeasonView v in m.Seasons)
            {
                Assert.Equal(36, v.Clubs);
                Assert.InRange((double)v.Met / v.Clubs, 0.5, 0.9);
                Assert.InRange(v.BelowTwenty, 0, 0.3);
                Assert.Equal(12, v.PointsByRank.Length);
                Assert.True(v.PointsByRank.Take(3).Average() > v.PointsByRank.Skip(9).Average(), "the best paid take more points");
            }

            for (int d = 1; d <= 3; d++) Assert.InRange(m.ClubDrift(d), -0.5, 0.5);
            Assert.Contains("Points a match by expected place", CareerBoard.Report(0x200, 1));
        }
    }
}
