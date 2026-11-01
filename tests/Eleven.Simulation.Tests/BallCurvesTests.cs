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
        public void The_grid_covers_every_combination_and_every_ball_comes_to_rest()
        {
            List<BallCurve> curves = BallCurves.Characterise(new SimConfig());

            int grid = BallCurves.SpeedsMps.Count * BallCurves.LoftsDegrees.Count * BallCurves.SpinsRadps.Count;
            Assert.Equal(grid, curves.Count(c => c.Kind == "kick"));
            Assert.Single(curves, c => c.Kind == "config-kick");
            Assert.Equal(BallCurves.DropsMetres.Count, curves.Count(c => c.Kind == "drop"));
            Assert.All(curves, c => Assert.NotNull(c.RestSeconds));
        }

        [Fact]
        public void Curves_trace_back_to_the_build_and_config_that_made_them()
        {
            SimConfig config = new SimConfig().With("airDrag", 0.015);
            List<BallCurve> curves = BallCurves.Characterise(config);

            var csv = new StringWriter();
            BallCurves.WriteCsv(csv, config, curves);
            string[] rows = csv.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            Assert.All(rows, row => Assert.StartsWith($"{config.Hash:x16},", row));

            var summary = new StringWriter();
            BallCurves.WriteSummary(summary, config, new[] { "airDrag" }, curves);
            string text = summary.ToString();
            Assert.Contains($"build: {SimulationInfo.BuildId}", text);
            Assert.Contains($"config-hash: {config.Hash:x16}", text);
            Assert.Contains("airDrag: 0.015 1/m  (override)", text);
            Assert.Contains("not approved feel", text);
        }

        private static string RepositoryRoot()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "docs")) && File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            }

            throw new DirectoryNotFoundException("Repository root (with AGENTS.md) not found above the test binaries.");
        }
    }
}
