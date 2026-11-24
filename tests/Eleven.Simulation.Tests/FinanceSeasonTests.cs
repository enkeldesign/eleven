using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #198: a club's money in a career. A season and its turn conserve every k; the same career
    /// twice gives the same money; the hub says your balance, wages and budget; and the
    /// measurement keeps wages over revenue near the research's by division.
    /// </summary>
    public class FinanceSeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(ulong seed, int club = 0)
        {
            World w = CareerSession.NewWorld(seed);
            return CareerSession.Start(w, seed, w.Clubs[club].Id, CareerRole.ManagerOnly, Config);
        }

        [Fact]
        public void A_season_and_its_turn_conserve_every_k_each_on_its_line()
        {
            CareerSession s = New(0x198);
            Dictionary<int, long> start = s.World.Clubs.ToDictionary(c => c.Id, c => c.Balance);
            while (!s.SeasonOver) s.Simulate();
            s.NextSeason();
            foreach (Club c in s.World.Clubs)
            {
                Assert.Equal(start[c.Id] + c.LastSeason.Net + c.Season.Net, c.Balance);
                Assert.True(c.LastSeason[LedgerLine.Wages] < 0 && c.LastSeason[LedgerLine.Broadcasting] > 0 && c.LastSeason[LedgerLine.Matchday] > 0 && c.LastSeason[LedgerLine.Merit] > 0, c.ShortName);
                Assert.True(c.Season[LedgerLine.Wages] < 0, "the off-season's wages are the new season's");
            }
        }

        [Fact]
        public void The_same_career_twice_gives_the_same_money()
        {
            string Run()
            {
                CareerSession s = New(0x1981);
                for (int i = 0; i < 12; i++) s.Simulate();
                return string.Join(";", s.World.Clubs.Select(c => $"{c.Balance}:{string.Join(".", c.Season.Amounts)}"));
            }

            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void The_hub_says_your_balance_wages_and_budget()
        {
            CareerSession s = New(0x1982, 4);
            Club mine = s.World.Clubs.Single(c => c.Id == s.Club);
            Assert.Equal(FormattableString.Invariant($"Balance {mine.Balance:N0}k. Wages {Finance.WageBill(s.World, mine):N0}k a week of a {Finance.WageBudget(s.World, mine):N0}k budget."), CareerHub.FinanceLine(s));
            mine.Balance = 1240;
            Assert.StartsWith("Balance 1,240k. Wages ", CareerHub.FinanceLine(s));
        }

        [Fact]
        public void The_measurement_keeps_wages_over_revenue_near_the_researchs_and_most_clubs_solvent()
        {
            List<CareerFinances.DivisionSeason> views = CareerFinances.Measure(0x198, 2);
            Assert.Equal(2 * 3, views.Count);
            foreach (CareerFinances.DivisionSeason v in views)
            {
                (double low, double high) = v.Division == 1 ? (0.5, 0.8) : v.Division == 2 ? (0.6, 1.0) : (0.7, 1.2);
                Assert.InRange(v.WagesOverRevenue, low, high);
                Assert.InRange(v.InDeficit, 0, 3);
            }

            Assert.True(views.Where(v => v.Division == 1).All(v => v.MeanBalance > views.Single(x => x.Season == v.Season && x.Division == 3).MeanBalance));
            Assert.Contains("Wages over revenue", CareerFinances.Report(0x198, 1));
        }
    }
}
