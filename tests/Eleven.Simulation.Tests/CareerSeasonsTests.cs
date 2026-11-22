using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #164: headless seasons. A season plays to its end by the quick-sim, league matches level or
    /// not and cup ties always won; three seasons roll forward the same for a seed, with the world
    /// conserved; and a hook plays a chosen club's fixtures its own way.
    /// </summary>
    public class CareerSeasonsTests
    {
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 180);

        [Fact]
        public void A_season_plays_headless_to_its_end()
        {
            World w = WorldGenerator.Generate(0x157);
            Season s = SeasonEngine.Start(w, 1, 0x164, SeasonEngine.FirstRanking(w), Rules);
            CareerSeasons.PlaySeason(Config, w, s, Rules, 10);
            Assert.True(SeasonEngine.Finished(s, Rules));
            Assert.Equal(396 + 35, s.Results.Count);
            Assert.All(s.Fixtures, f => Assert.True(s.Results.ContainsKey(f.Id)));
            List<MatchResult> cup = s.Fixtures.Where(f => f.Competition == CompetitionId.Cup).Select(f => s.Results[f.Id]).ToList();
            Assert.All(cup, r => Assert.NotEqual(0, r.Winner));
            Assert.All(s.Fixtures.Where(f => f.Competition != CompetitionId.Cup), f => Assert.Equal(0, s.Results[f.Id].ExtraTime));
            Assert.Contains(s.Fixtures.Where(f => f.Competition != CompetitionId.Cup), f => s.Results[f.Id].Winner == 0);
            Assert.NotEqual(0, s.CupWinner);

            // The quick-sim's own seed for each fixture: replaying one gives its result.
            Fixture any = s.Fixtures[17];
            Assert.Equal(s.Results[any.Id].ToText(), CareerSeasons.Quick(Config, w, s, any, 10).ToText());
        }

        private static string Story(List<Season> seasons, World w) =>
            string.Join("|", seasons.Select(s => $"{s.Number}:{s.Seed:x}:{s.CupWinner}:" + string.Join(",", s.Fixtures.Select(f => $"{f.Id}={s.Results[f.Id].HomeGoals}-{s.Results[f.Id].AwayGoals}"))))
            + "|" + Convert.ToBase64String(WorldCodec.Encode(w));

        [Fact]
        public void Three_seasons_roll_forward_the_same_for_a_seed_with_the_world_conserved()
        {
            World a = WorldGenerator.Generate(0x157), b = WorldGenerator.Generate(0x157);
            int players = a.Players.Count;
            List<Season> x = CareerSeasons.RollForward(Config, a, 0x164, 3, Rules, 10);
            List<Season> y = CareerSeasons.RollForward(Config, b, 0x164, 3, Rules, 10);
            Assert.Equal(3, x.Count);
            Assert.Equal(Story(x, a), Story(y, b));
            Assert.Equal(new[] { 1, 2, 3 }, x.Select(s => s.Number));
            Assert.Equal(3, x.Select(s => s.Seed).Distinct().Count());

            // Every season whole: all its results, a cup winner, twelve clubs a division.
            foreach (Season s in x)
            {
                Assert.True(SeasonEngine.Finished(s, Rules));
                Assert.Equal(396 + 35, s.Results.Count);
                Assert.NotEqual(0, s.CupWinner);
                Assert.All(s.Divisions, d => Assert.Equal(12, d.Count));
                Assert.Equal(36, s.Divisions.SelectMany(d => d).Distinct().Count());
            }

            // Two up and two down between seasons, and nothing else moves.
            for (int i = 1; i < x.Count; i++)
            {
                int moved = 0;
                for (int d = 0; d < 3; d++) moved += x[i].Divisions[d].Except(x[i - 1].Divisions[d]).Count();
                Assert.Equal(8, moved);
            }

            // The world conserved: its clubs, squads, players and managers.
            Assert.Equal(36, a.Clubs.Count);
            Assert.Equal(players, a.Players.Count);
            Assert.All(a.Clubs, c => Assert.Equal(WorldGenerator.SquadSize, c.Squad.Count));
            for (int d = 1; d <= 3; d++) Assert.Equal(12, a.Clubs.Count(c => c.Division == d));
            Assert.Equal(x[2].Divisions[0].OrderBy(i => i), a.Clubs.Where(c => c.Division == 1).Select(c => c.Id).OrderBy(i => i));
            Assert.Equal(WorldCodec.Encode(a), WorldCodec.Encode(WorldCodec.Decode(WorldCodec.Encode(a))));
        }

        [Fact]
        public void A_hook_plays_a_chosen_clubs_fixtures_its_own_way()
        {
            World w = WorldGenerator.Generate(0x157);
            Season s = SeasonEngine.Start(w, 1, 0x164, SeasonEngine.FirstRanking(w), Rules);
            int mine = s.Divisions[0][0];
            var played = new List<int>();
            MatchResult? Mine(Fixture f)
            {
                if (f.Home != mine && f.Away != mine) return null;
                played.Add(f.Id);
                return new MatchResult(1, 2, f.Home == mine ? 4 : 0, f.Away == mine ? 4 : 0, 180, 360, Array.Empty<ResultGoal>(), 0);
            }

            List<Fixture> day = CareerSeasons.PlayMatchday(Config, w, s, Rules, 10, Mine);
            Assert.Equal(18, day.Count);
            Assert.Single(played);
            Fixture f = day.Single(x => x.Home == mine || x.Away == mine);
            Assert.Equal(4, f.Home == mine ? s.Results[f.Id].HomeGoals : s.Results[f.Id].AwayGoals);
            Assert.Equal(1, s.Matchday);
        }
    }
}
