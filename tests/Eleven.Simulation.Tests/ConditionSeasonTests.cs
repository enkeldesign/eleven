using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #177 in the harness: a career match's tired start from each footballer's condition, a
    /// fresh side unchanged; rotation's rule; the quick-sim's side tired by its XI's condition
    /// and its rating lowered by the fitted weight; a headless matchday's cost and recovery; the
    /// human's match starting tired; the condition sample read back and the model equal to its
    /// fit; and the hub's line.
    /// </summary>
    public class ConditionSeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;
        private static readonly SquadRules Squad = SquadRules.Reference;

        [Fact]
        public void A_career_match_starts_each_footballer_at_his_own_fatigue_and_a_fresh_side_as_before()
        {
            World w = WorldGenerator.Generate(0x177);
            int home = w.Clubs[2].Id, away = w.Clubs[20].Id;
            Dictionary<int, int> lineup = CareerSquads.Lineup(w, home, away, 10);
            SimState fresh = CareerSquads.Match(Config, w, home, away, 10, 1);
            CareerSquads.TiredStart(fresh, w, lineup, Squad);
            Assert.All(fresh.Players, p => Assert.Equal(0, p.Fatigue));

            Player tired = w.Players.Single(p => p.Id == lineup[Squads.HomeOutfielder(3)]);
            Player spent = w.Players.Single(p => p.Id == lineup[Squads.AwayOutfielder(10, 5)]);
            tired.Fitness = 70;
            spent.Fitness = 40;
            SimState s = CareerSquads.Match(Config, w, home, away, 10, 1);
            CareerSquads.TiredStart(s, w, lineup, Squad);
            foreach (PlayerState p in s.Players)
            {
                double expected = p.Id == Squads.HomeOutfielder(3) ? Availability.StartFatigue(tired, Squad)
                    : p.Id == Squads.AwayOutfielder(10, 5) ? Availability.StartFatigue(spent, Squad) : 0;
                Assert.Equal(expected, p.Fatigue, 9);
            }

            Assert.True(s.Players.Single(p => p.Id == Squads.AwayOutfielder(10, 5)).Fatigue > s.Players.Single(p => p.Id == Squads.HomeOutfielder(3)).Fatigue);
        }

        [Fact]
        public void A_tired_starter_is_rested_for_a_fit_one_of_his_line_within_the_margin()
        {
            World w = WorldGenerator.Generate(0x1770);
            int club = w.Clubs[7].Id;
            SquadSelection usual = CareerSquads.Selection(w, club, 10);
            Assert.Equal(usual.Starters.Select(p => p.Id), CareerSquads.Selection(w, club, 10, null, Squad).Starters.Select(p => p.Id));

            Player tired = usual.Starters[4];
            tired.Fitness = Squad.RestBelow - 10;
            var generous = new SquadRules { RotationMargin = 100 };
            var strict = new SquadRules { RotationMargin = -100 };
            Assert.DoesNotContain(tired.Id, CareerSquads.Selection(w, club, 10, null, generous).Starters.Select(p => p.Id));
            Assert.Contains(tired.Id, CareerSquads.Selection(w, club, 10, null, strict).Starters.Select(p => p.Id));

            // Above the threshold he plays, however tired.
            tired.Fitness = Squad.RestBelow;
            Assert.Contains(tired.Id, CareerSquads.Selection(w, club, 10, null, generous).Starters.Select(p => p.Id));
        }

        [Fact]
        public void The_quick_sims_side_is_tired_by_its_xis_condition_and_rated_lower()
        {
            World w = WorldGenerator.Generate(0x1771);
            int club = w.Clubs[11].Id;
            QuickSide fresh = CareerQuickSim.Side(w, club, 10, null, Squad);
            Assert.Equal(0, fresh.Fatigue);
            SquadSelection xi = CareerSquads.Selection(w, club, 10, null, Squad);
            for (int i = 0; i < 10; i++) xi.Starters[i].Fitness = Squad.RestBelow + 5;
            QuickSide tired = CareerQuickSim.Side(w, club, 10, null, Squad);
            Assert.True(tired.Fatigue > 0);
            Assert.Equal(Enumerable.Range(0, 10).Average(i => Availability.StartFatigue(xi.Starters[i], Squad)), tired.Fatigue, 9);

            var model = new QuickSimModel(0, 0, new[] { 0, 1, 2, 3.0 }, 1, 0, 0, 0, 0, fatigueWeight: 0.5);
            Assert.Equal(QuickSim.Rating(fresh, model) - 0.5 * tired.Fatigue, QuickSim.Rating(tired, model), 9);
            Assert.Equal(QuickSim.Rating(fresh, QuickSimModel.ElevenASide), QuickSim.Rating(new QuickSide(fresh.Tier, fresh.Capabilities), QuickSimModel.ElevenASide), 12);
        }

        [Fact]
        public void A_headless_matchday_costs_its_footballers_and_the_days_to_the_next_recover_them()
        {
            World w = WorldGenerator.Generate(0x1772);
            Season s = SeasonEngine.Start(w, 1, 0x1772, SeasonEngine.FirstRanking(w), Rules);
            List<Matchday> calendar = SeasonCalendar.Of(Rules);
            // To the league round before the first cup round: three days to recover, not seven.
            while (calendar[s.Matchday + 1].Kind != MatchdayKind.Cup) CareerSeasons.PlayMatchday(Config, w, s, Rules, 10);
            foreach (Player p in w.Players) p.Fitness = 100;
            var lineups = new Dictionary<int, Dictionary<int, int>>();
            foreach (Fixture f in SeasonEngine.Next(s, Rules))
                lineups[f.Id] = CareerSquads.Lineup(w, f.Home, f.Away, 10, Availability.OutOf(w, f.Home, MatchdayKind.League), Availability.OutOf(w, f.Away, MatchdayKind.League), Squad);
            int gap = Availability.GapAfter(Rules, s.Matchday);
            Assert.Equal(Rules.CupGapDays, gap);
            // Each footballer's attributes at kick-off: the matchday's development (#194) may grow his stamina after his match is costed.
            Dictionary<int, int[]> atKickOff = w.Players.ToDictionary(p => p.Id, p => p.Attributes.ToArray());
            List<Fixture> day = CareerSeasons.PlayMatchday(Config, w, s, Rules, 10);
            double perDay = Squad.ConditionCostPerMatch / Squad.RecoveryDays;
            int tiredCount = 0;
            foreach (Fixture f in day)
            {
                MatchResult r = s.Results[f.Id];
                foreach (ResultPlayer line in r.Players)
                {
                    Player p = w.Players.Single(x => x.Id == lineups[f.Id][line.Player]);
                    int after = 100 - (int)Math.Round(Availability.ConditionCost(new Player { Attributes = atKickOff[p.Id] }, line.Seconds, 2 * r.HalfSeconds, Squad));
                    Assert.Equal(Math.Min(100, after + (int)Math.Round(gap * perDay)), p.Fitness);
                    if (p.Fitness < 100) tiredCount++;
                }
            }

            Assert.True(tiredCount > 100, $"{tiredCount} footballers short of full for the midweek tie");
        }

        [Fact]
        public void The_humans_match_starts_tired_where_his_footballers_are()
        {
            World w = CareerSession.NewWorld(0x1773);
            int club = QuickMatch.Clubs(w)[0].Id;
            CareerSession s = CareerSession.Start(w, 0x1773, club, CareerRole.ManagerOnly, Config);
            Fixture f = s.Next()!;
            Dictionary<int, int> lineup = s.LineupFor(f);
            Player tired = w.Players.Single(p => p.Id == lineup[Squads.HomeOutfielder(2)]);
            tired.Fitness = Squad.RestBelow + 2;
            Assert.Equal(lineup, s.LineupFor(f));
            SimState state = s.MatchFor(f, 9);
            Assert.True(state.Players.Single(p => p.Id == Squads.HomeOutfielder(2)).Fatigue > 0);
            Assert.Equal(Availability.StartFatigue(tired, s.Squad), state.Players.Single(p => p.Id == Squads.HomeOutfielder(2)).Fatigue, 9);
        }

        private static string Reference => Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "condition-reference.csv");

        [Fact]
        public void The_condition_sample_reads_back_and_the_model_is_its_fit()
        {
            Assert.True(File.Exists(Reference), "no condition-reference.csv");
            string[] lines = File.ReadAllLines(Reference);
            List<ConditionMatch> sample = CareerConditionCalibration.Read(lines);
            World world = CareerQuickSimCalibration.World();
            int fixtures = (CareerQuickSimCalibration.Fixtures(world).Count + CareerConditionCalibration.Step - 1) / CareerConditionCalibration.Step;
            Assert.Equal(fixtures * CareerConditionCalibration.Levels.Length, sample.Count);
            var text = new StringWriter();
            text.NewLine = "\n";
            CareerConditionCalibration.Write(text, sample);
            Assert.Equal(string.Join("\n", lines) + "\n", text.ToString());

            (double weight, double error) = CareerConditionCalibration.Fit(world, sample, QuickSimModel.ElevenASide);
            Assert.True(error > 0);
            Assert.Equal(weight, QuickSimModel.ElevenASide.FatigueWeight, 6);
        }

        [Fact]
        public void The_hub_names_the_xi_below_full_condition_lowest_first()
        {
            World w = CareerSession.NewWorld(0x1774);
            int club = QuickMatch.Clubs(w)[3].Id;
            CareerSession s = CareerSession.Start(w, 0x1774, club, CareerRole.PlayerManager, Config);
            Assert.Equal("", CareerHub.ConditionLine(s));
            Dictionary<int, int> lineup = s.LineupFor(s.Next()!);
            Player a = w.Players.Single(p => p.Id == lineup[Squads.HomeOutfielder(0)]), b = w.Players.Single(p => p.Id == lineup[Squads.HomeOutfielder(6)]);
            a.Fitness = 88;
            b.Fitness = 72;
            string Name(Player p) => $"{p.FirstName[0]}. {p.LastName}";
            Assert.Equal($"Condition: {Name(b)} 72, {Name(a)} 88", CareerHub.ConditionLine(s));
        }
    }
}
