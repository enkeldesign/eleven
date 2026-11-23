using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #168: what the career hub says. The season and matchday; the human's division and place;
    /// his next fixture or why there is none; his division's table with his row marked; his last
    /// results; the season's end; and the one forward action.
    /// </summary>
    public class CareerHubTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(CareerRole role = CareerRole.PlayerManager)
        {
            World w = CareerSession.NewWorld(0x168);
            Assert.NotEmpty(w.Clubs);
            return CareerSession.Start(w, 0x168, QuickMatch.Clubs(w)[0].Id, role, Config);
        }

        private static string Short(CareerSession s, int id) => s.World.Clubs.Single(c => c.Id == id).ShortName;

        [Theory]
        [InlineData(1, "1st")]
        [InlineData(2, "2nd")]
        [InlineData(3, "3rd")]
        [InlineData(4, "4th")]
        [InlineData(11, "11th")]
        [InlineData(12, "12th")]
        [InlineData(13, "13th")]
        [InlineData(21, "21st")]
        [InlineData(22, "22nd")]
        public void Places_read_as_ordinals(int n, string text) => Assert.Equal(text, CareerHub.Ordinal(n));

        [Fact]
        public void The_hub_opens_on_the_first_matchday_with_the_humans_first_fixture()
        {
            CareerSession s = New();
            Assert.Equal("Season 1 · Matchday 1 of 28", CareerHub.Header(s));
            Assert.Equal("Division 1 · 1st · 0 points", CareerHub.Standing(s).Replace(CareerHub.Ordinal(s.Position), "1st"));
            Fixture? f = s.Next();
            Assert.NotNull(f);
            string ground = f!.Home == s.Club ? "at home" : "away";
            Assert.Equal($"League round 1: {Short(s, f.Home)} v {Short(s, f.Away)}, {ground}", CareerHub.NextLine(s));
            Assert.Equal(HubAction.Play, CareerHub.Action(s));
            Assert.Equal(HubAction.Watch, CareerHub.Action(New(CareerRole.ManagerOnly)));
            Assert.Empty(CareerHub.ResultLines(s, 3));

            List<HubRow> rows = CareerHub.TableRows(s);
            Assert.Equal(12, rows.Count);
            Assert.Single(rows, r => r.Mine);
            Assert.Contains(Short(s, s.Club), rows.Single(r => r.Mine).Text);
        }

        [Fact]
        public void After_a_matchday_the_table_and_the_results_show_it()
        {
            CareerSession s = New();
            Fixture f = s.Next()!;
            Assert.NotNull(f);
            s.Simulate();
            Assert.Equal("Season 1 · Matchday 2 of 28", CareerHub.Header(s));
            MatchResult r = s.Season.Results[f.Id];
            int mine = f.Home == s.Club ? r.HomeGoals : r.AwayGoals, theirs = f.Home == s.Club ? r.AwayGoals : r.HomeGoals;
            string verdict = mine > theirs ? "W" : mine < theirs ? "L" : "D";
            int other = f.Home == s.Club ? f.Away : f.Home;
            Assert.Equal(new[] { $"{verdict} {mine}–{theirs} v {Short(s, other)} ({(f.Home == s.Club ? "home" : "away")})" }, CareerHub.ResultLines(s, 3));

            // A row: place, club, played, goal difference and points.
            TableRow row = s.Table().Single(x => x.Club == s.Club);
            string expected = $"{s.Position,2}  {Short(s, s.Club),-4} {row.Played,2}  {(row.GoalDifference > 0 ? "+" : "")}{row.GoalDifference,0}  {row.Points,2}";
            Assert.Equal(expected, CareerHub.TableRows(s).Single(x => x.Mine).Text);
            Assert.Equal($"Division 1 · {CareerHub.Ordinal(s.Position)} · {row.Points} point{(row.Points == 1 ? "" : "s")}", CareerHub.Standing(s));
        }

        [Fact]
        public void A_bye_says_so_and_offers_the_next_matchday()
        {
            CareerSession s = New();
            for (int i = 0; i < 3; i++) s.Simulate();
            Assert.Null(s.Next());
            Assert.Equal("No match for you: a bye into the round of 32.", CareerHub.NextLine(s));
            Assert.Equal(HubAction.NextMatchday, CareerHub.Action(s));
        }

        [Fact]
        public void A_cup_tie_says_its_round_and_the_season_ends_with_its_story()
        {
            CareerSession s = New();
            while (!s.SeasonOver)
            {
                Fixture? f = s.Next();
                if (f != null && f.Competition == CompetitionId.Cup)
                {
                    string round = CompetitionRules.CupRounds[f.Round - 1];
                    Assert.StartsWith($"Cup, {round}: ", CareerHub.NextLine(s));
                }

                bool inCup = s.Season.CupAlive.Contains(s.Club) || s.Season.Matchday < 4;
                if (f == null && !inCup && SeasonCalendar.Of(s.Rules)[s.Season.Matchday].Kind == MatchdayKind.Cup)
                    Assert.Equal("No match for you: out of the cup.", CareerHub.NextLine(s));
                s.Simulate();
            }

            Assert.Equal("Season 1 · over", CareerHub.Header(s));
            Assert.Equal(HubAction.NextSeason, CareerHub.Action(s));
            Assert.Equal("The season is over.", CareerHub.NextLine(s));
            string summary = CareerHub.Summary(s);
            Assert.StartsWith($"You finished {CareerHub.Ordinal(s.Position)} in division 1.", summary);
            (List<int> up, List<int> down) = SeasonEngine.Movement(s.Season, s.Rules);
            Assert.Contains("Up: " + string.Join(", ", up.Select(id => Short(s, id))) + ".", summary);
            Assert.Contains("Down: " + string.Join(", ", down.Select(id => Short(s, id))) + ".", summary);
            Assert.Contains($"Cup won by {s.World.Clubs.Single(c => c.Id == s.Season.CupWinner).Name}.", summary);
            Assert.Equal("", CareerHub.Summary(New()));
        }
    }
}
