using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#69: the keeper's characterisation and its recorded technical reference.</summary>
    public class KeeperShotsTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "keeper-shots-reference.csv");

        private static readonly SimConfig Config = new SimConfig();

        private static readonly List<KeeperShot> Default = KeeperShots.Characterise(Config);

        private static bool Saved(KeeperShot s) => s.Outcome == "held" || s.Outcome == "parried";

        [Fact]
        public void The_default_shots_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            KeeperShots.WriteCsv(csv, Config, Default);
            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void He_never_dives_or_meets_the_ball_before_his_reaction_and_never_dives_further_than_he_can()
        {
            foreach (KeeperShot s in Default)
            {
                KeeperCapabilities keeper = s.Profile == "slow" ? KeeperCapabilities.Slow : KeeperCapabilities.Reference;
                string at = $"{s.Profile} {s.DistanceMetres} m {s.Target} {s.SpeedMps} m/s";
                Assert.Equal(Simulator.ReactionTicksFor(keeper, Config) * Config.TickSeconds, s.ReactionSeconds, 9);
                if (!double.IsNaN(s.DiveSeconds)) Assert.True(s.DiveSeconds >= s.ReactionSeconds - 1e-9, $"{at}: dived at {s.DiveSeconds:F3} s");

                // On his feet he shuffles at most 0.4 of his top speed; a dive adds its own reach.
                double feet = 0.4 * PlayerCapabilities.Reference.MaxSpeed * 4;
                Assert.True(s.LateralMetres <= feet + keeper.DiveSpeed * Config.KeeperDiveSeconds + 0.05, $"{at}: moved {s.LateralMetres:F2} m across");
            }
        }

        [Fact]
        public void The_reference_keeper_saves_every_central_shot_and_more_than_the_slow_one()
        {
            Assert.All(Default.Where(s => s.Profile == "reference" && s.Target == "centre"), s => Assert.True(Saved(s), $"{s.DistanceMetres} m {s.SpeedMps} m/s: {s.Outcome}"));
            int reference = Default.Count(s => s.Profile == "reference" && Saved(s)), slow = Default.Count(s => s.Profile == "slow" && Saved(s));
            Assert.True(reference > slow, $"reference saves {reference}, slow {slow}");
        }
    }
}
