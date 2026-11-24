using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #201: contracts in a career. A season's turn renews, releases and signs and keeps every
    /// squad whole; the same career twice gives the same contracts; the hub says who's out of
    /// contract and the review who left and signed; and the measurement keeps squads whole and
    /// wage bills near their budgets.
    /// </summary>
    public class ContractSeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(ulong seed, int club = 0)
        {
            World w = CareerSession.NewWorld(seed);
            return CareerSession.Start(w, seed, w.Clubs[club].Id, CareerRole.ManagerOnly, Config);
        }

        private static string Short(Player p) => (p.FirstName.Length > 0 ? p.FirstName.Substring(0, 1) + ". " : "") + p.LastName;

        [Fact]
        public void A_careers_turn_renews_releases_and_signs_and_keeps_every_squad_whole_and_the_review_says_who_left_and_signed()
        {
            CareerSession s = New(0x201, 3);
            Club mine = s.World.Clubs.Single(c => c.Id == s.Club);

            // The hub: who's out of contract this season, by id.
            List<Player> ending = s.World.Players.Where(p => p.ClubId == mine.Id && p.ContractYears == 1).OrderBy(p => p.Id).ToList();
            Assert.NotEmpty(ending);
            Assert.Equal("Out of contract this season: " + string.Join(", ", ending.Select(Short)) + ".", CareerHub.ContractLine(s));

            // One of them poor, so he's let go.
            Player poor = ending[0];
            for (int i = 0; i < poor.Attributes.Length; i++) poor.Attributes[i] = 3;
            poor.Age = 29;
            while (!s.SeasonOver) s.Simulate();
            s.NextSeason();

            Assert.Equal(0, poor.ClubId);
            Assert.All(s.World.Clubs, c => Assert.Equal(WorldGenerator.SquadSize, s.World.Players.Count(p => p.ClubId == c.Id)));
            Assert.All(s.World.Players.Where(p => p.ClubId != 0), p => Assert.InRange(p.ContractYears, 1, 5));
            Assert.Contains(s.World.LastMoves, m => m.To == 0);
            Assert.Contains(s.World.LastMoves, m => m.From == 0);

            string review = CareerHub.ReviewLine(s);
            Assert.Contains("Released: ", review);
            Assert.Contains(Short(poor), review);
            foreach (ContractMove m in s.World.LastMoves.Where(m => m.To == mine.Id)) Assert.Contains($"{m.FirstName.Substring(0, 1)}. {m.LastName}", review);
        }

        [Fact]
        public void The_same_career_twice_gives_the_same_contracts()
        {
            string Run()
            {
                CareerSession s = New(0x2011);
                while (!s.SeasonOver) s.Simulate();
                s.NextSeason();
                return string.Join(";", s.World.Players.Select(p => $"{p.Id}.{p.ClubId}.{p.ContractYears}.{p.Wage}.{p.FreeSince}"));
            }

            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void The_hub_says_nothing_when_no_contract_ends()
        {
            CareerSession s = New(0x2012, 5);
            foreach (Player p in s.World.Players.Where(p => p.ClubId == s.Club)) p.ContractYears = 2;
            Assert.Equal("", CareerHub.ContractLine(s));
        }

        [Fact]
        public void The_measurement_keeps_squads_whole_and_wage_bills_near_their_budgets()
        {
            List<CareerContracts.TurnView> turns = CareerContracts.Measure(0x201, 3);
            Assert.Equal(3, turns.Count);
            foreach (CareerContracts.TurnView t in turns)
            {
                Assert.Equal((WorldGenerator.SquadSize, WorldGenerator.SquadSize), (t.SmallestSquad, t.LargestSquad));
                int renewed = t.Renewed.Cast<int>().Sum(), released = t.Released.Cast<int>().Sum();
                Assert.True(released > 0 && renewed > released, $"season {t.Season}: {renewed} renewed, {released} released");
                Assert.InRange(t.FreeAgents, 0, 150);
                for (int d = 0; d < 3; d++)
                {
                    Assert.InRange(t.BillOverBudget[d], 0.5, 1.15);
                    Assert.InRange(t.HighestOverBudget[d], 0.5, 1.6);
                }
            }

            Assert.Contains("Renewed", CareerContracts.Report(0x201, 1));
        }
    }
}
