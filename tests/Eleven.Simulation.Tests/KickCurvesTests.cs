using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#37: the headless kick characterisation and its recorded technical reference.</summary>
    public class KickCurvesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "kick-reference.csv");

        private static readonly SimConfig Config = new SimConfig();

        private static readonly List<KickCurve> Default = KickCurves.Characterise(Config, PlayerCapabilities.Reference);

        private static IEnumerable<KickCurve> Launches => Default.Where(c => c.Kind == "launch");

        private static IEnumerable<KickCurve> Shaped(string @case, string input) => Default.Where(c => c.Kind == "aftertouch" && c.Case == @case && c.Input == input);

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            KickCurves.WriteCsv(csv, Default);

            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void The_aftertouch_windows_span_the_research_starting_range()
        {
            TunableParameter window = SimConfig.Tunables.Single(p => p.Name == "aftertouchSeconds");
            Assert.Equal(new[] { window.StartMin!.Value, 0.20, 0.25, window.StartMax!.Value }, KickCurves.WindowsSeconds);
            Assert.Equal(20, Launches.Count());
            Assert.Equal(KickCurves.ShapedKicks.Count * KickCurves.WindowsSeconds.Count * 4, Default.Count(c => c.Kind == "aftertouch"));
            Assert.All(Default.Where(c => c.Kind == "aftertouch"), c => Assert.Equal(Config.With("aftertouchSeconds", c.WindowSeconds).Hash, c.ConfigHash));
        }

        // An aftertouchSeconds override is measured as well as the starting-range windows, and summarised.
        [Theory]
        [InlineData(0.18)]
        [InlineData(0.0)]
        [InlineData(0.6)]
        public void A_configured_window_off_the_grid_is_measured_too(double window)
        {
            SimConfig config = Config.With("aftertouchSeconds", window);
            List<KickCurve> curves = KickCurves.Characterise(config, PlayerCapabilities.Reference);

            Assert.Equal(KickCurves.ShapedKicks.Count * 4, curves.Count(c => c.Kind == "aftertouch" && c.WindowSeconds == window));
            Assert.Equal(KickCurves.ShapedKicks.Count * (KickCurves.WindowsSeconds.Count + 1) * 4, curves.Count(c => c.Kind == "aftertouch"));
            double[] windows = curves.Where(c => c.Kind == "aftertouch" && c.Case == "lofted" && c.Input == "none").Select(c => c.WindowSeconds).ToArray();
            Assert.Equal(windows.OrderBy(w => w), windows);

            var summary = new StringWriter();
            KickCurves.WriteSummary(summary, config, PlayerCapabilities.Reference, curves);
            Assert.Contains($"key: ", summary.ToString());
        }

        // With no drag and no spin, a lofted launch is a projectile: it peaks at vz²/2g and lands
        // after 2vz/g, having carried h · 2vz/g. The measurement agrees to within a tick.
        [Fact]
        public void A_launch_without_aftertouch_flies_as_a_projectile()
        {
            double dt = Config.TickSeconds, g = Config.Gravity;
            foreach (KickCurve c in Launches)
            {
                Assert.Equal(c.Strength * PlayerCapabilities.Reference.KickPower * Config.KickSpeed, c.LaunchSpeedMps, 12);
                Assert.Equal(c.Loft * Config.KickLoft * 180 / Math.PI, c.LaunchElevationDegrees, 12);
                Assert.InRange(Math.Abs(c.ErrorDegrees), 0, PlayerCapabilities.Reference.KickError * 180 / Math.PI);
                Assert.Equal(0, c.LateralMetres, 9);
                if (c.Loft == 0) continue;

                double elevation = c.LaunchElevationDegrees * Math.PI / 180;
                double vz = c.LaunchSpeedMps * Math.Sin(elevation), h = c.LaunchSpeedMps * Math.Cos(elevation);
                Assert.True(Math.Abs(c.PeakMetres - vz * vz / (2 * g)) <= vz * dt, $"{c.Strength}/{c.Loft}: peak {c.PeakMetres}, expected {vz * vz / (2 * g)}");
                Assert.True(Math.Abs(c.FlightSeconds - 2 * vz / g) <= dt, $"{c.Strength}/{c.Loft}: flight {c.FlightSeconds}, expected {2 * vz / g}");
                Assert.True(Math.Abs(c.CarryMetres - h * 2 * vz / g) <= h * dt, $"{c.Strength}/{c.Loft}: carry {c.CarryMetres}, expected {h * 2 * vz / g}");
            }
        }

        [Fact]
        public void Harder_kicks_go_further_and_more_loft_goes_higher()
        {
            foreach (double loft in KickCurves.Lofts.Where(l => l > 0))
            {
                double[] carries = Launches.Where(c => c.Loft == loft).OrderBy(c => c.Strength).Select(c => c.CarryMetres).ToArray();
                Assert.True(carries.Zip(carries.Skip(1), (a, b) => b > a).All(x => x), $"carry at loft {loft}: {string.Join(", ", carries)}");
            }

            foreach (double strength in KickCurves.Strengths)
            {
                double[] peaks = Launches.Where(c => c.Strength == strength).OrderBy(c => c.Loft).Select(c => c.PeakMetres).ToArray();
                Assert.True(peaks.Zip(peaks.Skip(1), (a, b) => b > a).All(x => x), $"peak at strength {strength}: {string.Join(", ", peaks)}");
            }
        }

        // The window's weights sum to 1, so its length changes when input acts, not how much: full
        // sideways input turns a ground pass by exactly the bend, and back or forward input changes
        // an airborne kick's vertical speed by exactly the lift, at every window.
        [Fact]
        public void Full_aftertouch_shapes_by_the_configured_amount_at_every_window()
        {
            double bend = Config.AftertouchBend * 180 / Math.PI;
            Assert.All(Shaped("ground-pass", "left"), c => Assert.Equal(bend, c.TurnDegrees, 9));
            Assert.All(Shaped("ground-pass", "left"), c => Assert.True(c.LateralMetres > 0));
            Assert.All(Shaped("lofted", "back"), c => Assert.Equal(Config.AftertouchLift, c.LiftMps, 9));
            Assert.All(Shaped("lofted", "forward"), c => Assert.Equal(-Config.AftertouchLift, c.LiftMps, 9));
            Assert.All(Shaped("lofted", "back"), c => Assert.True(c.PeakMetres > Shaped("lofted", "none").First().PeakMetres));

            // In the air, the spin left by sideways input curves the ball on after the window closes.
            Assert.All(Shaped("lofted", "left"), c =>
            {
                Assert.InRange(c.SpinRadps, 0.5 * Config.KickSideSpin, Config.KickSideSpin);
                Assert.True(c.TurnDegrees > bend);
                Assert.True(c.LateralMetres > Shaped("ground-pass", "left").First().LateralMetres);
            });

            // A ball on the ground can't be driven into it.
            Assert.All(Shaped("ground-pass", "forward"), c => Assert.Equal(Shaped("ground-pass", "none").First().TotalMetres, c.TotalMetres));
        }

        [Fact]
        public void Less_kick_power_makes_every_launch_softer()
        {
            PlayerCapabilities weaker = PlayerCapabilities.Reference.With("kickPower", 0.8);
            List<KickCurve> curves = KickCurves.Characterise(Config, weaker);
            foreach (KickCurve c in curves.Where(c => c.Kind == "launch"))
            {
                KickCurve reference = Launches.Single(r => r.Strength == c.Strength && r.Loft == c.Loft);
                Assert.Equal(0.8 * reference.LaunchSpeedMps, c.LaunchSpeedMps, 12);
                Assert.True(c.TotalMetres < reference.TotalMetres);
            }
        }

        [Fact]
        public void Curves_trace_back_to_the_build_config_and_profile_that_made_them()
        {
            // A ball value too: the kick tool takes every tunable, so its summary must record every one.
            SimConfig config = Config.With("aftertouchBend", 0.123456789).With("kickSpeed", 21.5).With("rollingFriction", 0.2);
            PlayerCapabilities profile = PlayerCapabilities.Reference.With("kickPower", 0.875).With("kickError", 0.0125);
            List<KickCurve> curves = KickCurves.Characterise(config, profile);

            var csv = new StringWriter();
            KickCurves.WriteCsv(csv, curves);
            string[] rows = csv.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            Assert.All(rows.Take(20), row => Assert.StartsWith($"{config.Hash:x16},launch,", row));

            var summary = new StringWriter();
            KickCurves.WriteSummary(summary, config, profile, curves);
            string text = summary.ToString();
            Assert.Contains($"build: {SimulationInfo.BuildId}", text);
            Assert.Contains($"config-hash: {config.Hash:x16}", text);
            Assert.Contains($"config-format: {SimConfig.FormatVersion}", text);
            Assert.Contains("not approved feel", text);

            // Every tunable and every capability is written exactly, so the summary rebuilds both.
            var recorded = new List<KeyValuePair<string, double>>();
            PlayerCapabilities capabilities = default;
            foreach (string line in text.Split('\n'))
            {
                int colon = line.IndexOf(": ", StringComparison.Ordinal);
                if (colon <= 0) continue;
                string name = line.Substring(0, colon);
                string value = line.Substring(colon + 2).Split(' ')[0];
                if (SimConfig.Tunables.Any(p => p.Name == name)) recorded.Add(new KeyValuePair<string, double>(name, double.Parse(value, CultureInfo.InvariantCulture)));
                if (PlayerCapabilities.Parameters.Any(p => p.Name == name)) capabilities = capabilities.With(name, double.Parse(value, CultureInfo.InvariantCulture));
            }

            Assert.Equal(SimConfig.Tunables.Count, recorded.Count);
            Assert.Contains("rollingFriction: 0.2 fraction of g  (override)", text);
            Assert.Equal(config.Hash, new SimConfig().With(recorded).Hash);
            Assert.Equal(profile, capabilities);
        }
    }
}
