using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #200: the board. Objectives from a club's wage rank; confidence moved by each league
    /// match's points against the expected, by the cup's rounds and at the season's end, and
    /// carried; reputations moving slowly toward what a club's division and place would give it;
    /// and the world's bytes keeping it, a format-8 world read by the rules.
    /// </summary>
    public class BoardTests
    {
        private static readonly BoardRules Rules = BoardRules.Reference;
        private static readonly CompetitionRules Competition = CompetitionRules.Reference;

        private static (World World, Season Season) New(ulong seed = 0x200)
        {
            World w = WorldGenerator.Generate(seed);
            Season s = SeasonEngine.Start(w, 1, seed, SeasonEngine.FirstRanking(w), Competition);
            return (w, s);
        }

        private static MatchResult Score(int home, int away) => new MatchResult(1, 2, home, away, 180, 360, Array.Empty<ResultGoal>(), 0);

        private static double Clamp(double c) => Math.Clamp(c, 0, 100);

        [Fact]
        public void The_expected_place_is_the_clubs_wage_rank_in_its_division_and_its_targets_follow()
        {
            (World w, _) = New();
            for (int d = 1; d <= 3; d++)
            {
                List<Club> ranked = w.Clubs.Where(c => c.Division == d)
                    .OrderByDescending(c => Finance.WageBill(w, c)).ThenByDescending(c => c.Reputation).ThenBy(c => c.Id).ToList();
                for (int i = 0; i < ranked.Count; i++)
                {
                    BoardState b = ranked[i].Board;
                    Assert.Equal(i + 1, b.ExpectedPlace);
                    Assert.Equal(Math.Min(i + 1 + Rules.Leeway, Competition.LeagueClubs - Competition.Relegated), b.LeagueTarget);
                    Assert.Equal(Rules.CupTarget[d - 1], b.CupTarget);
                    Assert.Equal(Rules.StartConfidence, b.Confidence);
                }
            }

            // The poorest club of the top division paid the most is expected to win it.
            Club poorest = w.Clubs.Where(c => c.Division == 1).OrderBy(c => c.Board.ExpectedPlace).Last();
            Assert.Equal(12, poorest.Board.ExpectedPlace);
            foreach (Player p in w.Players.Where(p => p.ClubId == poorest.Id)) p.Wage *= 10;
            Board.Objectives(w, Competition);
            Assert.Equal(1, poorest.Board.ExpectedPlace);
            Assert.Equal(1 + Rules.Leeway, poorest.Board.LeagueTarget);
        }

        [Fact]
        public void Each_objective_and_confidence_has_its_words()
        {
            var club = new Club { Division = 1 };
            string League(int division, int target)
            {
                club.Division = division;
                club.Board.LeagueTarget = target;
                return Board.League(club, 3, Competition);
            }

            Assert.Equal("Win the league", League(1, 1));
            Assert.Equal("Win the league", League(2, 1));
            Assert.Equal("Win promotion", League(2, 2));
            Assert.Equal("Win promotion", League(3, 2));
            Assert.Equal("Finish 2nd or higher", League(1, 2));
            Assert.Equal("Finish 6th or higher", League(1, 6));
            Assert.Equal("Finish 3rd or higher", League(3, 3));
            Assert.Equal("Avoid relegation", League(1, 10));
            Assert.Equal("Avoid relegation", League(2, 10));
            Assert.Equal("Finish 10th or higher", League(3, 10));

            string Cup(int target)
            {
                club.Board.CupTarget = target;
                return Board.Cup(club);
            }

            Assert.Equal("reach the round of 32", Cup(2));
            Assert.Equal("reach the round of 16", Cup(3));
            Assert.Equal("reach the quarter-finals", Cup(4));
            Assert.Equal("reach the final", Cup(6));
            Assert.Equal("win the cup", Cup(7));

            Assert.Equal("delighted", Board.Words(80));
            Assert.Equal("pleased", Board.Words(79.9));
            Assert.Equal("pleased", Board.Words(60));
            Assert.Equal("satisfied", Board.Words(59.6));
            Assert.Equal("satisfied", Board.Words(40));
            Assert.Equal("concerned", Board.Words(20));
            Assert.Equal("losing patience", Board.Words(19.9));
            Assert.Equal("losing patience", Board.Words(0));
        }

        [Fact]
        public void Each_league_match_moves_confidence_by_its_points_against_the_expected_and_each_cup_tie_by_the_round_reached()
        {
            Assert.Equal(Rules.TopPoints, Board.ExpectedPoints(1, 12));
            Assert.Equal(Rules.BottomPoints, Board.ExpectedPoints(12, 12));
            Assert.Equal(Rules.TopPoints - (Rules.TopPoints - Rules.BottomPoints) * 5 / 11, Board.ExpectedPoints(6, 12), 12);
            Assert.True(Board.ExpectedPoints(3, 12) > Board.ExpectedPoints(4, 12));

            (World w, Season s) = New();
            Club ClubOf(int id) => w.Clubs.Single(c => c.Id == id);
            List<Fixture> league = s.Fixtures.Where(f => f.Competition != CompetitionId.Cup).Take(2).ToList();

            // A home win, then a draw.
            Club home = ClubOf(league[0].Home), away = ClubOf(league[0].Away);
            Board.Matchday(w, new[] { (league[0], Score(2, 0)) });
            Assert.Equal(Clamp(60 + Rules.PerPoint * (3 - Board.ExpectedPoints(home.Board.ExpectedPlace, 12))), home.Board.Confidence, 12);
            Assert.Equal(Clamp(60 + Rules.PerPoint * (0 - Board.ExpectedPoints(away.Board.ExpectedPlace, 12))), away.Board.Confidence, 12);
            Club drawn = ClubOf(league[1].Home);
            Board.Matchday(w, new[] { (league[1], Score(1, 1)) });
            Assert.Equal(Clamp(60 + Rules.PerPoint * (1 - Board.ExpectedPoints(drawn.Board.ExpectedPlace, 12))), drawn.Board.Confidence, 12);

            // Bounded: 0–100.
            home.Board.Confidence = 99.5;
            away.Board.Confidence = 0.5;
            Board.Matchday(w, new[] { (league[0], Score(3, 0)) });
            Assert.Equal(100, home.Board.Confidence);
            Assert.Equal(0, away.Board.Confidence);

            // A cup tie of round 3: the top division's club (to reach round 4) out before its
            // target; the third division's (to reach round 2) through to round 4, beyond it.
            Club top = w.Clubs.First(c => c.Division == 1), low = w.Clubs.First(c => c.Division == 3);
            top.Board.Confidence = low.Board.Confidence = 50;
            var tie = new Fixture { Id = Fixture.MakeId(1, CompetitionId.Cup, 3, 0), Season = 1, Competition = CompetitionId.Cup, Round = 3, Home = top.Id, Away = low.Id };
            Board.Matchday(w, new[] { (tie, Score(0, 1)) });
            Assert.Equal(50 - Rules.CupMissed, top.Board.Confidence);
            Assert.Equal(50 + Rules.CupBeyond, low.Board.Confidence);

            // The top club through to its target round, the low one out after reaching its own: neither moves.
            top.Board.Confidence = low.Board.Confidence = 50;
            Board.Matchday(w, new[] { (tie, Score(2, 0)) });
            Assert.Equal(50, top.Board.Confidence);
            Assert.Equal(50, low.Board.Confidence);
        }

        // A season played by the clubs' ids: a league match by them, a cup tie never level, the lower id through.
        private static void Play(Season s)
        {
            while (!SeasonEngine.Finished(s, Competition))
            {
                foreach (Fixture f in SeasonEngine.Next(s, Competition))
                {
                    (int home, int away) = f.Competition == CompetitionId.Cup ? (f.Home < f.Away ? (1, 0) : (0, 1)) : (f.Home % 3, f.Away % 2);
                    SeasonEngine.Record(s, Competition, f.Id, Score(home, away));
                }

                SeasonEngine.Advance(s, Competition);
            }
        }

        [Fact]
        public void The_seasons_end_judges_the_league_and_the_money_moves_reputations_and_carries_confidence()
        {
            (World w, Season s) = New();
            Play(s);
            Finance.SeasonEnd(w, s, Competition);
            Club broke = w.Clubs[5];
            broke.Balance = -10;
            foreach (Club c in w.Clubs) c.Board.Confidence = 50 + c.Id % 7;
            Dictionary<int, (double Confidence, int Reputation, int Expected, int Target)> before = w.Clubs.ToDictionary(c => c.Id, c => (c.Board.Confidence, c.Reputation, c.Board.ExpectedPlace, c.Board.LeagueTarget));
            Dictionary<int, int> managers = w.Managers.ToDictionary(m => m.Id, m => m.Reputation);

            Board.SeasonEnd(w, s, Competition);
            foreach (Club c in w.Clubs)
            {
                List<TableRow> table = SeasonEngine.Table(s, (CompetitionId)c.Division, Competition);
                int place = table.FindIndex(r => r.Club == c.Id) + 1;
                (double confidence, int reputation, int expected, int target) = before[c.Id];
                double judged = confidence + (place <= target ? Rules.LeagueMet : -Rules.LeagueMissed) + Rules.PerPlace * (target - place) - (c.Balance < 0 ? Rules.Deficit : 0);
                BoardState b = c.Board;
                Assert.Equal((place, expected, target), (b.LastPlace, b.LastExpected, b.LastTarget));
                Assert.Equal(Clamp(judged), b.LastConfidence, 12);
                Assert.Equal(Rules.StartConfidence + Rules.Carry * (b.LastConfidence - Rules.StartConfidence), b.Confidence, 12);

                double clubTarget = Board.ReputationTarget(c.Division, place, 12, s.CupWinner == c.Id);
                Assert.Equal(reputation, b.LastReputation);
                Assert.Equal(Board.Toward(reputation, clubTarget), c.Reputation);
                Manager m = w.Managers.Single(x => x.Id == c.ManagerId);
                Assert.Equal(Board.Toward(managers[m.Id], clubTarget + Rules.ManagerPerPlace * (expected - place)), m.Reputation);
            }

            Assert.Contains(w.Clubs, c => c.Board.LastPlace <= c.Board.LastTarget);
            Assert.Contains(w.Clubs, c => c.Board.LastPlace > c.Board.LastTarget);
            Assert.Contains(w.Clubs, c => c.Reputation != c.Board.LastReputation);
            Assert.True(broke.Balance < 0, "the deficit's club is judged by it, above");
        }

        [Fact]
        public void A_reputation_moves_a_share_of_the_way_to_its_target_a_season_at_most_a_step_within_1_to_20()
        {
            double spread = WorldGenerator.ClubSpread;
            Assert.Equal(WorldGenerator.DivisionLevel[0] + spread, Board.ReputationTarget(1, 1, 12, false), 12);
            Assert.Equal(WorldGenerator.DivisionLevel[0] - spread, Board.ReputationTarget(1, 12, 12, false), 12);
            Assert.Equal(WorldGenerator.DivisionLevel[2] + spread * 3 / 11 + Rules.CupWinnerReputation, Board.ReputationTarget(3, 5, 12, true), 12);
            Assert.True(Board.ReputationTarget(2, 1, 12, false) > Board.ReputationTarget(1, 12, 12, false));

            Assert.Equal(12, Board.Toward(10, 14.5));  // 0.35 × 4.5 = 1.6: two points
            Assert.Equal(11, Board.Toward(12, 10.5));  // −0.5: one down
            Assert.Equal(10, Board.Toward(10, 11.2));  // 0.4: none
            Assert.Equal(12, Board.Toward(10, 30));    // at most a step
            Assert.Equal(8, Board.Toward(10, -10));
            Assert.Equal(20, Board.Toward(20, 26));
            Assert.Equal(1, Board.Toward(1, -3));
        }

        [Fact]
        public void The_worlds_bytes_keep_each_clubs_board_and_a_format_8_world_reads_it_by_the_rules()
        {
            (World w, _) = New();
            Assert.Equal(11, WorldCodec.FormatVersion);
            byte[] plain = WorldCodec.Encode(w);

            // Format 8 is format 10 less format 9's section, each club's objectives, confidence and last season's verdict, and format 10's, the contracts (#201).
            var eight = new CanonicalWriter(plain.Length);
            eight.WriteBytes(plain[..4]);
            eight.WriteInt32(8);
            eight.WriteBytes(plain[8..^Formats.From9(w)]);
            World old = WorldCodec.Decode(eight.ToArray());
            Assert.Equal(w.Clubs.Select(c => (c.Board.ExpectedPlace, c.Board.LeagueTarget, c.Board.CupTarget)), old.Clubs.Select(c => (c.Board.ExpectedPlace, c.Board.LeagueTarget, c.Board.CupTarget)));
            Assert.All(old.Clubs, c => Assert.Equal((Rules.StartConfidence, 0), (c.Board.Confidence, c.Board.LastPlace)));
            Assert.Equal(plain, WorldCodec.Encode(old));

            Club k = w.Clubs[7];
            k.Board.Confidence = 33.25;
            k.Board.LastPlace = 4;
            k.Board.LastExpected = 6;
            k.Board.LastTarget = 8;
            k.Board.LastConfidence = 71.125;
            k.Board.LastReputation = 12;
            k.Board.LeagueTarget = 5;
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            string Of(Club c) => FormattableString.Invariant($"{c.Board.ExpectedPlace}.{c.Board.LeagueTarget}.{c.Board.CupTarget}.{c.Board.Confidence:R}.{c.Board.LastPlace}.{c.Board.LastExpected}.{c.Board.LastTarget}.{c.Board.LastConfidence:R}.{c.Board.LastReputation}");
            Assert.Equal(w.Clubs.Select(Of), back.Clubs.Select(Of));
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));
        }
    }
}
