using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#65: the free pass's characterisation and its recorded technical reference.</summary>
    public class FreePassCurvesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "free-pass-reference.csv");

        private static readonly SimConfig Config = SpikeScenario.Config();

        private static readonly List<FreePassCurve> Default = FreePassCurves.Characterise(Config, PlayerCapabilities.Reference);

        private static FreePassCurve Of(string model, string name, string profile = "reference") =>
            Default.Single(c => c.Model == model && c.Case == name && c.Profile == profile);

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            FreePassCurves.WriteCsv(csv, Config, Default);
            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void A_faster_passer_launches_a_faster_free_pass_and_a_standing_one_keeps_the_standing_weighting()
        {
            foreach (string model in new[] { "free", "attached" })
            {
                double standing = Of(model, "standing").LaunchSpeedMps;
                Assert.True(standing < Of(model, "walking").LaunchSpeedMps, model);
                Assert.True(Of(model, "walking").LaunchSpeedMps < Of(model, "jogging").LaunchSpeedMps, model);
                Assert.True(Of(model, "jogging").LaunchSpeedMps < Of(model, "sprinting").LaunchSpeedMps, model);

                double weighted = System.Math.Sqrt(Config.PassArrivalSpeed * Config.PassArrivalSpeed + 2 * Config.RollingFriction * Config.Gravity * Config.PassFreeDistance)
                    - Config.RollingFriction * Config.Gravity * Config.TickSeconds;
                Assert.True(System.Math.Abs(standing - weighted) < 0.02, $"{model}: standing launch {standing:F3} m/s, the standing weighting {weighted:F3}");
            }
        }

        [Fact]
        public void Every_free_pass_runs_ahead_of_its_passer_and_is_released()
        {
            foreach (FreePassCurve c in Default)
            {
                Assert.True(c.LeadQuarterMetres > c.LeadAtContactMetres && c.LeadHalfMetres > c.LeadQuarterMetres && c.LeadSecondMetres > c.LeadHalfMetres,
                    $"{c.Model}/{c.Case}/{c.Profile}: lead {c.LeadAtContactMetres:F2}, {c.LeadQuarterMetres:F2}, {c.LeadHalfMetres:F2}, {c.LeadSecondMetres:F2} m");
                Assert.Equal(0, c.TouchesAfter);
            }
        }

        [Fact]
        public void A_weak_kicker_is_held_to_their_kick_power()
        {
            foreach (string model in new[] { "free", "attached" })
            {
                FreePassCurve weak = Of(model, "sprinting", "weak-kick");
                Assert.True(weak.LaunchSpeedMps <= weak.KickPower * Config.KickSpeed + 1e-9, $"{model}: {weak.LaunchSpeedMps:F3} m/s");
                Assert.True(weak.LaunchSpeedMps < Of(model, "sprinting").LaunchSpeedMps, model);
            }
        }
    }
}
