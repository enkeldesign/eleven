using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#94: the air-contact characterisation and air contact in CPU matches, and their recorded technical references.</summary>
    public class AirCurvesTests
    {
        private static string Reference(string name) => Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", name);

        [Fact]
        public void Each_height_band_makes_its_contact_and_the_curves_match_their_reference()
        {
            SimConfig config = SpikeScenario.Config();
            List<AirCurve> curves = AirCurves.Characterise(config, PlayerCapabilities.Reference);
            foreach (AirCurve c in curves)
            {
                double z = c.HeightMetres;
                string expected = z <= config.KickMaxBallHeight ? "kick"
                    : z >= config.HeaderMinHeight && z <= config.HeaderMaxHeight ? "header"
                    : z <= config.VolleyMaxHeight ? "volley" : "miss";
                Assert.True(expected == c.Contact, $"{z} m: {c.Contact}, expected {expected}");
            }

            // A header is slower than the same kick on the ground, within #94's research range.
            double kick = curves.First(c => c.Contact == "kick" && c.IncomingMps == 0).SpeedMps;
            Assert.All(curves.Where(c => c.Contact == "header"), c => Assert.InRange(c.SpeedMps / kick, 0.3, 0.75));

            // A volley errs more the faster the ball comes.
            List<AirCurve> volleys = curves.Where(c => c.Contact == "volley" && c.Heading == 0.6 && c.HeightMetres == 0.7).OrderBy(c => c.IncomingMps).ToList();
            Assert.True(volleys.Last().MaxErrorRadians > volleys.First().MaxErrorRadians);

            var csv = new StringWriter();
            AirCurves.WriteCsv(csv, config, curves);
            Assert.Equal(File.ReadAllText(Reference("air-curves-reference.csv")), csv.ToString());
        }

        [Fact]
        public void Cpu_matches_head_and_volley_and_match_their_reference()
        {
            SimConfig config = SpikeScenario.Config();
            List<AirTrial> trials = AirTrials.Run(config);
            Assert.True(trials.Sum(t => t.Volleys + t.Headers) > 0, "no air contact in any match");
            var csv = new StringWriter();
            AirTrials.WriteCsv(csv, config, trials);
            Assert.Equal(File.ReadAllText(Reference("air-trials-reference.csv")), csv.ToString());
        }
    }
}
