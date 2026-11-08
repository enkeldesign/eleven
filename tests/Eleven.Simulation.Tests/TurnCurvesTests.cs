using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #66 and #75: the turn and cut characterisation under FREE and ATTACHED, off and on, snapped
    /// and swept, and its recorded technical reference.
    /// </summary>
    public class TurnCurvesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "turn-reference.csv");

        private static readonly SimConfig Config = SpikeScenario.Config();

        private static readonly List<TurnCurve> Default = TurnCurves.Characterise(Config);

        private static TurnCurve Of(string model, string turn, string profile, string speed, double degrees, double sweep = 0) =>
            Default.Single(c => c.Model == model && c.Turn == turn && c.Profile == profile && c.Speed == speed && c.Degrees == degrees && c.SweepSeconds == sweep);

        private static readonly double[] Sweeps = { 0, TurnCurves.SweepSeconds };

        public static readonly TheoryData<string> Models = new TheoryData<string> { "free", "attached" };

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            TurnCurves.WriteCsv(csv, Config, Default);

            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void Every_case_runs_under_both_models_off_and_on_snapped_and_swept_from_every_touch_phase()
        {
            Assert.Equal(2 * 2 * 3 * 2 * 4 * 2, Default.Count);
            Assert.All(Default, c => Assert.Equal(Config.TouchIntervalTicks, c.Starts));
            foreach (string model in new[] { "free", "attached" })
            foreach (string turn in new[] { "off", "on" })
            foreach (double sweep in Sweeps)
                Assert.Equal(24, Default.Count(c => c.Model == model && c.Turn == turn && c.SweepSeconds == sweep));
        }

        [Fact]
        public void Under_attached_a_change_short_of_the_turn_angle_measures_the_same_off_and_on()
        {
            // The cut is FREE's alone (#75).
            foreach (TurnCurve on in Default.Where(c => c.Model == "attached" && c.Turn == "on" && c.Degrees <= 90))
            {
                TurnCurve off = Of("attached", "off", on.Profile, on.Speed, on.Degrees, on.SweepSeconds);
                Assert.Equal(Row(off), Row(on));
            }
        }

        [Theory]
        [MemberData(nameof(Models))]
        public void With_the_turn_good_control_gets_the_ball_ahead_sooner_and_it_trails_less(string model)
        {
            foreach (string speed in new[] { "half", "sprint" })
            foreach (double degrees in new[] { 135.0, 180.0 })
            {
                // Snapped, as #66 measured them.
                TurnCurve off = Of(model, "off", "good", speed, degrees), on = Of(model, "on", "good", speed, degrees);
                string at = $"{model}, {speed}, {degrees}°";
                Assert.True(on.Kept == on.Starts && on.Kept >= off.Kept, $"{at}: kept {on.Kept} with the turn, {off.Kept} without");
                Assert.True(on.ForwardMaxSeconds < off.ForwardMaxSeconds / 2, $"{at}: ahead in {on.ForwardMaxSeconds:F2} s with the turn, {off.ForwardMaxSeconds:F2} s without");
                Assert.True(on.TrailMaxMetres < off.TrailMaxMetres / 2, $"{at}: trails {on.TrailMaxMetres:F2} m with the turn, {off.TrailMaxMetres:F2} m without");
                Assert.True(on.BehindMaxSeconds < off.BehindMaxSeconds / 2, $"{at}: behind {on.BehindMaxSeconds:F2} s with the turn, {off.BehindMaxSeconds:F2} s without");
            }
        }

        [Fact]
        public void With_the_turn_poorer_control_keeps_fewer_sprint_reversals_or_turns_them_slower()
        {
            foreach (double degrees in new[] { 135.0, 180.0 })
            {
                TurnCurve good = Of("free", "on", "good", "sprint", degrees), poor = Of("free", "on", "poor", "sprint", degrees);
                Assert.True(poor.Kept < good.Kept, $"free, {degrees}°: control 0.3 kept {poor.Kept}, 0.9 {good.Kept}");

                good = Of("attached", "on", "good", "sprint", degrees);
                poor = Of("attached", "on", "poor", "sprint", degrees);
                Assert.True(poor.Kept < good.Kept || poor.ForwardMaxSeconds > good.ForwardMaxSeconds + 0.3,
                    $"attached, {degrees}°: control 0.3 kept {poor.Kept} and got ahead in {poor.ForwardMaxSeconds:F2} s, 0.9 kept {good.Kept} in {good.ForwardMaxSeconds:F2} s");
            }
        }

        // Every column but the config's identity.
        private static string Row(TurnCurve c)
        {
            var csv = new StringWriter();
            TurnCurves.WriteCsv(csv, Config, new[] { c });
            string line = csv.ToString().Split('\n')[1];
            return line.Substring(line.IndexOf(',', line.IndexOf(',', line.IndexOf(',') + 1) + 1));
        }
    }
}
