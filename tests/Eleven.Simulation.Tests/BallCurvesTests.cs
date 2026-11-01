using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The ball curves (#24): the default config's curves are a recorded technical reference, and
    /// any curves trace back to the settings that made them.
    /// </summary>
    public class BallCurvesTests
    {
        // Golden data: it changes only when ball behaviour or the grid changes, which needs its own
        // commit with a stated reason. The current values are placeholders, not approved feel.
        private static string ReferencePath =>
            Path.Combine(RepositoryRoot(), "docs", "simulation", "ball-curves-reference.csv");

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var config = new SimConfig();
            var csv = new StringWriter();
            BallCurves.WriteCsv(csv, config, BallCurves.Characterise(config));

            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void The_suite_covers_the_actions_and_the_grid_and_every_ball_comes_to_rest()
        {
            List<BallCurve> curves = BallCurves.Characterise(new SimConfig());

            Assert.Equal(new[] { "ground-pass-short", "ground-pass-medium", "driven-shot", "lofted-pass", "chip", "bending-shot", "bending-cross", "bounce-and-roll" },
                curves.Where(c => c.Kind == "action").Select(c => c.Case));
            int grid = BallCurves.SpeedsMps.Count * BallCurves.LoftsDegrees.Count * BallCurves.SpinsRadps.Count;
            Assert.Equal(grid, curves.Count(c => c.Kind == "grid"));
            Assert.Single(curves, c => c.Kind == "config-kick");
            Assert.Equal(BallCurves.DropsMetres.Count, curves.Count(c => c.Kind == "drop"));
            Assert.All(curves, c => Assert.NotNull(c.RestSeconds));
        }

        [Fact]
        public void Curves_trace_back_to_the_build_and_config_that_made_them()
        {
            SimConfig config = new SimConfig().With("rollingFriction", 0.15);
            List<BallCurve> curves = BallCurves.Characterise(config);

            var csv = new StringWriter();
            BallCurves.WriteCsv(csv, config, curves);
            string[] rows = csv.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            Assert.All(rows, row => Assert.StartsWith($"{config.Hash:x16},", row));

            var summary = new StringWriter();
            BallCurves.WriteSummary(summary, config, new[] { "rollingFriction" }, curves);
            string text = summary.ToString();
            Assert.Contains($"build: {SimulationInfo.BuildId}", text);
            Assert.Contains($"config-hash: {config.Hash:x16}", text);
            Assert.Contains("rollingFriction: 0.15 fraction of g  (override)", text);
            Assert.Contains("not approved feel", text);
        }

        [Fact]
        public void The_summary_records_settings_exactly_enough_to_rebuild_the_config()
        {
            SimConfig config = new SimConfig().With(new[]
            {
                new KeyValuePair<string, double>("magnusCoefficient", 0.00412345),
                new KeyValuePair<string, double>("kickLoft", 0.123456789),
            });
            var summary = new StringWriter();
            BallCurves.WriteSummary(summary, config, new[] { "magnusCoefficient", "kickLoft" }, BallCurves.Characterise(config));

            // Read every tunable back from the summary and rebuild: the hash must match.
            var recorded = new List<KeyValuePair<string, double>>();
            foreach (string line in summary.ToString().Split('\n'))
            {
                foreach (TunableParameter p in SimConfig.Tunables)
                {
                    if (!line.StartsWith(p.Name + ": ")) continue;
                    string value = line.Substring(p.Name.Length + 2).Split(' ')[0];
                    recorded.Add(new KeyValuePair<string, double>(p.Name, double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            Assert.Equal(SimConfig.Tunables.Count, recorded.Count);
            Assert.Equal(config.Hash, new SimConfig().With(recorded).Hash);
        }

        // Football-shaped, for any config in the research starting ranges: what each action is,
        // not how far it must go. Checked at both ends of every starting range.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Each_action_behaves_like_its_name_across_the_starting_ranges(bool upperEnds)
        {
            SimConfig config = new SimConfig().With(SimConfig.Tunables
                .Where(p => p.StartMin.HasValue && p.Name != "kickSideSpin")
                .Select(p => new KeyValuePair<string, double>(p.Name, upperEnds ? p.StartMax!.Value : p.StartMin!.Value))
                .Append(new KeyValuePair<string, double>("kickSideSpin", 20)));
            Dictionary<string, BallCurve> a = BallCurves.Characterise(config).Where(c => c.Kind == "action").ToDictionary(c => c.Case);

            Assert.Equal(0, a["ground-pass-short"].PeakMetres);
            Assert.Equal(0, a["ground-pass-medium"].PeakMetres);
            Assert.True(a["ground-pass-medium"].TotalMetres > a["ground-pass-short"].TotalMetres);
            // A chip is steeper and shorter than a lofted pass, not necessarily higher.
            Assert.True(a["chip"].PeakMetres / a["chip"].CarryMetres > a["lofted-pass"].PeakMetres / a["lofted-pass"].CarryMetres);
            Assert.True(a["lofted-pass"].CarryMetres > a["chip"].CarryMetres);
            Assert.True(a["driven-shot"].PeakMetres < a["lofted-pass"].PeakMetres);
            Assert.True(a["bending-shot"].LateralFlightMetres > 0 && a["bending-shot"].LateralMetres > 0);
            Assert.True(a["bending-cross"].LateralFlightMetres > 0 && a["bending-cross"].LateralMetres > 0);
            Assert.True(a["bounce-and-roll"].Bounces > 0 && a["bounce-and-roll"].RolloutMetres > 0);
        }

        internal static string RepositoryRoot()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "docs")) && File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            }

            throw new DirectoryNotFoundException("Repository root (with AGENTS.md) not found above the test binaries.");
        }
    }
}
