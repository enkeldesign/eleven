using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #194: development and ageing in a career. A matchday counts each XI's starts and grows the
    /// young; three seasons rolled forward keep the world's level and every squad whole; the hub's
    /// squad rows show each footballer's age, and the season's review names your club's risers,
    /// its retirements and its youth until your first match of the new season.
    /// </summary>
    public class DevelopmentSeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        [Fact]
        public void A_career_matchday_counts_each_xis_starts_and_grows_the_young()
        {
            World w = CareerSession.NewWorld(0x194);
            CareerSession s = CareerSession.Start(w, 0x194, w.Clubs[0].Id, CareerRole.ManagerOnly, Config);
            List<Fixture> day = SeasonEngine.Next(s.Season, s.Rules);
            var xi = new HashSet<int>();
            foreach (Fixture f in day)
            {
                foreach (KeyValuePair<int, int> kv in CareerSquads.Lineup(w, f.Home, f.Away, CareerSession.PerSide, null, null, s.Squad))
                {
                    if (kv.Key <= Squads.AwayKeeper(CareerSession.PerSide)) xi.Add(kv.Value);
                }
            }

            Dictionary<int, int[]> before = w.Players.ToDictionary(p => p.Id, p => p.Attributes.ToArray());
            s.Simulate();
            Assert.All(w.Players, p => Assert.Equal(xi.Contains(p.Id) ? 1 : 0, p.Starts));
            Assert.Equal(2 * day.Count * (CareerSession.PerSide + 1), xi.Count);
            List<Player> young = w.Players.Where(p => p.Age < 20 && CareerSquads.Level(p) + 1 < p.Potential).ToList();
            Assert.NotEmpty(young);
            Assert.All(young, p => Assert.True(p.Progress > 0 || p.SeasonPoints > 0, $"{p.Id} didn't grow"));
            Assert.All(w.Players.Where(p => p.Age >= 24), p => Assert.Equal(before[p.Id], p.Attributes));
        }

        [Fact]
        public void Three_seasons_rolled_forward_keep_the_worlds_level_and_every_squad_whole()
        {
            List<CareerDevelopment.SeasonView> seasons = CareerDevelopment.Measure(0x194, 3);
            Assert.Equal(3, seasons.Count);
            double start = seasons[0].MeanLevel;
            Assert.All(seasons, v => Assert.InRange(v.MeanLevel, start - 0.3, start + 0.3));
            // The youth replace those the clubs lost: retired, and since #201 released less those signed free.
            Assert.All(seasons.Skip(1), v => Assert.True(v.Retired > 0 && v.Intake == v.Retired + v.Released - v.Signed, $"season {v.Season}: {v.Retired} retired, {v.Released} released, {v.Signed} signed, {v.Intake} joined"));
            Assert.All(seasons, v => Assert.Equal((WorldGenerator.SquadSize, WorldGenerator.SquadSize), (v.SmallestSquad, v.LargestSquad)));
            Assert.Contains(seasons[0].Bands, b => b.Band.StartsWith("16", StringComparison.Ordinal) || b.Band.StartsWith("17", StringComparison.Ordinal));
            Assert.Contains(seasons[2].Bands, b => b.Count > 0);
            Assert.Contains("drift", CareerDevelopment.Report(0x194, 2));
        }

        [Fact]
        public void The_hubs_review_names_your_risers_retirements_and_youth_until_your_first_match()
        {
            World w = CareerSession.NewWorld(0x1942);
            CareerSession s = CareerSession.Start(w, 0x1942, w.Clubs[3].Id, CareerRole.ManagerOnly, Config);
            Assert.Equal("", CareerHub.ReviewLine(s));
            Player veteran = w.Players.First(p => p.ClubId == s.Club && p.Positions[0] == Position.CentreBack);
            veteran.Age = 44;
            while (!s.SeasonOver) s.Simulate();
            s.NextSeason();

            string review = CareerHub.ReviewLine(s);
            Assert.StartsWith("Last season: ", review);
            Assert.Contains($"Retired: {veteran.FirstName[0]}. {veteran.LastName}, 45", review);
            Player youth = w.Players.First(p => p.ClubId == s.Club && p.Joined == 2);
            Assert.Contains($"{youth.FirstName[0]}. {youth.LastName}, {youth.Age}", review);
            Player riser = w.Players.Where(p => p.ClubId == s.Club && p.Joined < 2).OrderByDescending(p => p.LastSeasonPoints).ThenBy(p => p.Id).First();
            Assert.True(riser.LastSeasonPoints > 0);
            Assert.Contains($"{riser.FirstName[0]}. {riser.LastName}", review);

            // Gone once your club has played in the new season.
            while (!s.Season.Fixtures.Any(f => (f.Home == s.Club || f.Away == s.Club) && s.Season.Results.ContainsKey(f.Id))) s.Simulate();
            Assert.Equal("", CareerHub.ReviewLine(s));
        }

        [Fact]
        public void A_squad_row_shows_his_age()
        {
            World w = CareerSession.NewWorld(0x1943);
            CareerSession s = CareerSession.Start(w, 0x1943, w.Clubs[5].Id, CareerRole.PlayerManager, Config);
            SquadRow row = CareerHub.SquadRows(s)[0];
            Player p = w.Players.Single(x => x.Id == row.Player);
            Assert.StartsWith($"{p.FirstName[0]}. {p.LastName}, {p.Age} · ", row.Text);
        }
    }
}
