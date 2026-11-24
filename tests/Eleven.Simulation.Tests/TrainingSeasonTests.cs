using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #196: training in a career. A club's intensity shapes its matchday's growth and recovery;
    /// on Auto every club trains as its manager chooses; the hub cycles your club's training and
    /// says what it does; and the measurement puts the intensities against each other.
    /// </summary>
    public class TrainingSeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(ulong seed, int club = 0)
        {
            World w = CareerSession.NewWorld(seed);
            return CareerSession.Start(w, seed, w.Clubs[club].Id, CareerRole.ManagerOnly, Config);
        }

        [Fact]
        public void A_clubs_intensity_shapes_its_matchdays_growth_and_recovery()
        {
            CareerSession light = New(0x196), hard = New(0x196);
            int club = light.World.Clubs[4].Id;
            light.World.Clubs[4].Training = TrainingIntensity.Light;
            hard.World.Clubs[4].Training = TrainingIntensity.Hard;
            light.Simulate();
            hard.Simulate();
            List<Player> Mine(CareerSession s) => s.World.Players.Where(p => p.ClubId == club).OrderBy(p => p.Id).ToList();
            double Grown(CareerSession s) => Mine(s).Sum(p => p.SeasonPoints + p.Progress);
            Assert.True(Grown(hard) > Grown(light), $"hard {Grown(hard):F2}, light {Grown(light):F2}");
            int tired = Mine(light).Count(p => p.Fitness < 100);
            Assert.True(tired > 0, "nobody short of full after the matchday");
            Assert.True(Mine(light).Sum(p => p.Fitness) > Mine(hard).Sum(p => p.Fitness), "light recovers more");
        }

        [Fact]
        public void On_auto_every_club_trains_as_its_manager_chooses()
        {
            CareerSession auto = New(0x1962), chosen = New(0x1962);
            int gap = Availability.GapAfter(auto.Rules, 0);
            var kickOff = auto.World.Clubs.ToDictionary(c => c.Id, c => CareerSeasons.XiCondition(auto.World, c.Id, CareerSession.PerSide, MatchdayKind.League, auto.Squad));
            foreach (Club c in chosen.World.Clubs) c.Training = Training.Choose(kickOff[c.Id], gap);
            Assert.Contains(chosen.World.Clubs, c => c.Training == TrainingIntensity.Hard || gap < TrainingRules.Reference.HardGapDays);
            auto.Simulate();
            chosen.Simulate();
            foreach (Club c in chosen.World.Clubs) c.Training = TrainingIntensity.Auto;
            Assert.Equal(WorldCodec.Encode(chosen.World), WorldCodec.Encode(auto.World));
        }

        [Fact]
        public void The_hub_cycles_your_clubs_training_and_says_what_it_does()
        {
            CareerSession s = New(0x1963, 2);
            Club mine = s.World.Clubs.Single(c => c.Id == s.Club);
            Assert.Equal(TrainingIntensity.Auto, mine.Training);
            Assert.StartsWith("Training: Auto, your manager's choice", CareerHub.TrainingLine(s));
            Assert.Equal(TrainingIntensity.Light, s.CycleTraining());
            Assert.Equal("Training: Light. Slower development, faster recovery, fewer injuries.", CareerHub.TrainingLine(s));
            Assert.Equal(TrainingIntensity.Normal, s.CycleTraining());
            Assert.Equal("Training: Normal.", CareerHub.TrainingLine(s));
            Assert.Equal(TrainingIntensity.Hard, s.CycleTraining());
            Assert.Equal("Training: Hard. Faster development, slower recovery, more injuries.", CareerHub.TrainingLine(s));
            Assert.Equal(TrainingIntensity.Auto, s.CycleTraining());
            Assert.Equal(TrainingIntensity.Auto, mine.Training);
        }

        [Fact]
        public void The_measurement_puts_the_intensities_against_each_other()
        {
            List<CareerTraining.Run> runs = CareerTraining.Measure(0x196, 2);
            Assert.Equal(new[] { TrainingIntensity.Light, TrainingIntensity.Normal, TrainingIntensity.Hard, TrainingIntensity.Auto }, runs.Select(r => r.Intensity));
            CareerTraining.Run light = runs[0], normal = runs[1], hard = runs[2], auto = runs[3];
            Assert.True(light.YoungGrowth < normal.YoungGrowth && normal.YoungGrowth < hard.YoungGrowth);
            Assert.True(light.KickOffCondition > normal.KickOffCondition && normal.KickOffCondition > hard.KickOffCondition);
            Assert.True(light.InjuriesPerClubSeason < normal.InjuriesPerClubSeason && normal.InjuriesPerClubSeason < hard.InjuriesPerClubSeason);
            Assert.InRange(auto.Drift, -0.3, 0.3);
            Assert.Contains("Auto", CareerTraining.Report(0x196, 1));
        }
    }
}
