using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #177: condition. The calendar's days, a league round a week from the last and a cup round
    /// midweek; a match's cost by the share of it he played and his stamina; recovery by the days;
    /// a tired start from his condition; and the season's turn, everyone fresh.
    /// </summary>
    public class ConditionTests
    {
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;
        private static readonly SquadRules Squad = SquadRules.Reference;

        private static Player With(int stamina, int fitness = 100)
        {
            var p = new Player { Id = 1, Fitness = fitness };
            for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = 10;
            p.Attributes[PlayerAttributes.Index("stamina")] = stamina;
            return p;
        }

        [Fact]
        public void League_rounds_are_a_week_apart_and_a_cup_round_is_midweek()
        {
            List<Matchday> days = SeasonCalendar.Of(Rules);
            Assert.Equal(0, days[0].Day);
            int lastLeague = 0;
            for (int m = 1; m < days.Count; m++)
            {
                if (days[m].Kind == MatchdayKind.League)
                {
                    Assert.Equal(lastLeague + Rules.LeagueGapDays, days[m].Day);
                    lastLeague = days[m].Day;
                }
                else
                {
                    Assert.Equal(lastLeague + Rules.CupGapDays, days[m].Day);
                }

                Assert.True(days[m].Day > days[m - 1].Day);
                Assert.Equal(days[m].Day - days[m - 1].Day, Availability.GapAfter(Rules, m - 1));
            }

            Assert.Equal(Rules.LeagueGapDays, Availability.GapAfter(Rules, days.Count - 1));
            Assert.Contains(days, d => d.Kind == MatchdayKind.Cup);
        }

        [Fact]
        public void A_matchs_cost_goes_by_the_share_he_played_and_his_stamina()
        {
            double full = Availability.ConditionCost(With(10), 360, 360, Squad);
            Assert.InRange(full, Squad.ConditionCostPerMatch * 0.98, Squad.ConditionCostPerMatch * 1.04);
            Assert.Equal(full / 2, Availability.ConditionCost(With(10), 180, 360, Squad), 9);
            Assert.Equal(0, Availability.ConditionCost(With(10), 0, 360, Squad));
            Assert.Equal(Squad.ConditionCostPerMatch * (1 - Squad.StaminaWeight), Availability.ConditionCost(With(20), 360, 360, Squad), 9);
            Assert.Equal(Squad.ConditionCostPerMatch * (1 + Squad.StaminaWeight), Availability.ConditionCost(With(1), 360, 360, Squad), 9);
            Assert.True(Availability.ConditionCost(With(15), 360, 360, Squad) < full);

            // Extra time costs more than a whole match.
            Assert.True(Availability.ConditionCost(With(10), 480, 360, Squad) > full);
        }

        [Fact]
        public void A_fixtures_result_costs_its_footballers_their_minutes()
        {
            World w = WorldGenerator.Generate(0x177);
            Season s = SeasonEngine.Start(w, 1, 0x177, SeasonEngine.FirstRanking(w), Rules);
            Fixture f = SeasonEngine.Next(s, Rules)[0];
            Player whole = w.Players.First(p => p.ClubId == f.Home), half = w.Players.Last(p => p.ClubId == f.Away), none = w.Players.First(p => p.ClubId == f.Away);
            MatchResult r = new MatchResult(1, 2, 0, 0, 180, 360, Array.Empty<ResultGoal>(), 0)
                .WithPlayers(new[] { new ResultPlayer(1, 1, 360, 0, false, 0), new ResultPlayer(2, 2, 180, 0, false, 0) });
            Availability.Apply(w, s, f, r, new Dictionary<int, int> { [1] = whole.Id, [2] = half.Id }, Squad);
            Assert.True(whole.Fitness < half.Fitness && half.Fitness < 100, $"{whole.Fitness}, {half.Fitness}");
            Assert.Equal(100 - (int)Math.Round(Availability.ConditionCost(whole, 360, 360, Squad)), whole.Fitness);
            Assert.Equal(100 - (int)Math.Round(Availability.ConditionCost(half, 180, 360, Squad)), half.Fitness);
            Assert.Equal(100, none.Fitness);
        }

        [Fact]
        public void Condition_recovers_with_the_days_to_full_and_no_further()
        {
            World w = WorldGenerator.Generate(0x177);
            w.Players[0].Fitness = 70;
            w.Players[1].Fitness = 99;
            Availability.Pass(w, 2, Squad);
            double perDay = Squad.ConditionCostPerMatch / Squad.RecoveryDays;
            Assert.Equal(70 + (int)Math.Round(2 * perDay), w.Players[0].Fitness);
            Assert.Equal(100, w.Players[1].Fitness);

            // A whole match's cost is recovered in RecoveryDays.
            w.Players[2].Fitness = 100 - (int)Squad.ConditionCostPerMatch;
            Availability.Pass(w, (int)Math.Ceiling(Squad.RecoveryDays), Squad);
            Assert.Equal(100, w.Players[2].Fitness);
        }

        [Fact]
        public void A_tired_start_is_his_condition_below_full_and_none_when_full()
        {
            Assert.Equal(0, Availability.StartFatigue(With(10, 100), Squad));
            Assert.Equal(0.3 * Squad.ConditionFatigue, Availability.StartFatigue(With(10, 70), Squad), 9);
            Assert.InRange(Availability.StartFatigue(With(10, 0), Squad), 0, 1);
            Assert.True(Availability.StartFatigue(With(10, 60), Squad) > Availability.StartFatigue(With(10, 80), Squad));
        }

        [Fact]
        public void At_the_seasons_turn_everyone_is_fresh()
        {
            World w = WorldGenerator.Generate(0x177);
            w.Players[0].Fitness = 40;
            Availability.NewSeason(w, Squad);
            Assert.Equal(100, w.Players[0].Fitness);
        }
    }
}
