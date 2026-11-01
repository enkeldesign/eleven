using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#36: the headless movement characterisation and its recorded technical reference.</summary>
    public class MovementCurvesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "movement-reference.csv");

        private static readonly SimConfig Config = new SimConfig();

        private static readonly List<MovementCurve> Default = MovementCurves.Characterise(Config, PlayerCapabilities.Reference);

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            MovementCurves.WriteCsv(csv, Config, Default);

            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void The_grid_spans_the_research_starting_ranges_and_the_profile_comes_first()
        {
            Assert.Equal(new[] { 7.0, 8, 9, 10 }, MovementCurves.MaxSpeedsMps);
            Assert.Equal(new[] { 12.0, 18, 24, 30 }, MovementCurves.AccelerationsMps2);

            MovementCurve profile = Default[0];
            Assert.Equal("profile", profile.Kind);
            Assert.Equal((7.0, 25.0), (profile.MaxSpeedMps, profile.AccelerationMps2));
            Assert.Equal(16, Default.Count(c => c.Kind == "grid"));
            Assert.Equal(17, Default.Count);

            MovementCurve requested = MovementCurves.Characterise(Config, PlayerCapabilities.Reference.With("maxSpeed", 9.5))[0];
            Assert.Equal((9.5, 25.0), (requested.MaxSpeedMps, requested.AccelerationMps2));
        }

        // With one acceleration bounding every change of velocity, a change of size Δv takes Δv/a
        // seconds and leaves the player Δv²/2a metres from where an instant change would put them
        // (docs/simulation/movement.md). The measurement agrees to within the tick.
        [Fact]
        public void Every_change_of_velocity_costs_its_size_over_the_acceleration()
        {
            double dt = Config.TickSeconds;
            foreach (MovementCurve c in Default)
            {
                double v = c.MaxSpeedMps, a = c.AccelerationMps2;
                void Costs(string what, double change, double seconds, double lagMetres)
                {
                    Assert.True(Math.Abs(seconds - change / a) <= dt, $"{v}/{a} {what}: {seconds} s, expected {change / a} s");
                    Assert.True(Math.Abs(lagMetres - change * change / (2 * a)) <= change * dt, $"{v}/{a} {what}: {lagMetres} m, expected {change * change / (2 * a)} m");
                }

                Costs("full speed", v, c.FullSpeedSeconds, v * c.FullSpeedSeconds - c.FullSpeedMetres);
                Costs("stop", v, c.StopSeconds, c.StopMetres);
                Costs("turn 45", 2 * v * Math.Sin(Math.PI / 8), c.Turn45Seconds, c.Turn45LagMetres);
                Costs("turn 90", Math.Sqrt(2) * v, c.Turn90Seconds, c.Turn90LagMetres);
                Costs("turn 180", 2 * v, c.Turn180Seconds, c.Turn180LagMetres);

                // A turn cuts the corner in velocity: the slowest point is the chord's midpoint.
                Assert.InRange(c.Turn45MinSpeedMps, v * Math.Cos(Math.PI / 8) - a * dt, v * Math.Cos(Math.PI / 8) + a * dt);
                Assert.InRange(c.Turn90MinSpeedMps, v * Math.Cos(Math.PI / 4) - a * dt, v * Math.Cos(Math.PI / 4) + a * dt);
            }
        }

        [Fact]
        public void Quicker_players_respond_sooner_and_faster_players_cover_ground_sooner()
        {
            foreach (double v in MovementCurves.MaxSpeedsMps)
            {
                List<MovementCurve> byAcceleration = Default.Where(c => c.Kind == "grid" && c.MaxSpeedMps == v).OrderBy(c => c.AccelerationMps2).ToList();
                for (int i = 1; i < byAcceleration.Count; i++)
                {
                    MovementCurve slower = byAcceleration[i - 1], quicker = byAcceleration[i];
                    Assert.True(quicker.FullSpeedSeconds < slower.FullSpeedSeconds);
                    Assert.True(quicker.StopMetres < slower.StopMetres);
                    Assert.True(quicker.Turn180Seconds < slower.Turn180Seconds);
                    Assert.True(quicker.Split10mSeconds < slower.Split10mSeconds);
                }
            }

            foreach (double a in MovementCurves.AccelerationsMps2)
            {
                List<MovementCurve> bySpeed = Default.Where(c => c.Kind == "grid" && c.AccelerationMps2 == a).OrderBy(c => c.MaxSpeedMps).ToList();
                for (int i = 1; i < bySpeed.Count; i++)
                {
                    Assert.True(bySpeed[i].Split10mSeconds <= bySpeed[i - 1].Split10mSeconds);
                    Assert.True(bySpeed[i].Split5mSeconds <= bySpeed[i - 1].Split5mSeconds);
                    Assert.True(bySpeed[i].Turn180LagMetres > bySpeed[i - 1].Turn180LagMetres, "more speed is more to turn around");
                }
            }
        }

        [Fact]
        public void Curves_trace_back_to_the_build_config_and_capabilities_that_made_them()
        {
            PlayerCapabilities profile = new PlayerCapabilities { MaxSpeed = 8.123456789, Acceleration = 17.25, KickPower = 0.875, KickError = 0.0125 };
            List<MovementCurve> curves = MovementCurves.Characterise(Config, profile);

            var csv = new StringWriter();
            MovementCurves.WriteCsv(csv, Config, curves);
            string[] rows = csv.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            Assert.All(rows, row => Assert.StartsWith($"{Config.Hash:x16},", row));

            var summary = new StringWriter();
            MovementCurves.WriteSummary(summary, Config, profile, curves);
            string text = summary.ToString();
            Assert.Contains($"build: {SimulationInfo.BuildId}", text);
            Assert.Contains($"config-hash: {Config.Hash:x16}", text);
            Assert.Contains($"tick-rate: {Config.TickRate}", text);
            Assert.Contains("not approved feel", text);

            // Every capability is written exactly, so the summary rebuilds the very profile.
            PlayerCapabilities recorded = default;
            foreach (string line in text.Split('\n'))
            {
                foreach (TunableParameter p in PlayerCapabilities.Parameters)
                {
                    if (!line.StartsWith(p.Name + ": ")) continue;
                    recorded = recorded.With(p.Name, double.Parse(line.Substring(p.Name.Length + 2).Split(' ')[0], CultureInfo.InvariantCulture));
                }
            }

            Assert.Equal(profile, recorded);
        }
    }
}
