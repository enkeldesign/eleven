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

        [Fact]
        public void With_the_cut_good_and_bench_control_keep_every_45_and_90_degree_cut_from_every_touch_phase()
        {
            foreach (string profile in new[] { "good", "bench" })
            foreach (string speed in new[] { "half", "sprint" })
            foreach (double degrees in new[] { 45.0, 90.0 })
            {
                TurnCurve on = Of("free", "on", profile, speed, degrees);
                Assert.True(on.Kept == on.Starts, $"{profile}, {speed}, {degrees}°: kept {on.Kept} of {on.Starts}");
            }

            // At a sprint, swept as a thumb sweeps a stick, too.
            foreach (string profile in new[] { "good", "bench" })
            foreach (double degrees in new[] { 45.0, 90.0 })
            {
                TurnCurve on = Of("free", "on", profile, "sprint", degrees, TurnCurves.SweepSeconds);
                Assert.True(on.Kept == on.Starts, $"{profile}, sprint, {degrees}° swept: kept {on.Kept} of {on.Starts}");
            }
        }

        [Fact]
        public void With_the_cut_poorer_control_retains_fewer_sprint_cuts_and_none_is_free()
        {
            foreach (double degrees in new[] { 45.0, 90.0 })
            foreach (double sweep in Sweeps)
            {
                TurnCurve good = Of("free", "on", "good", "sprint", degrees, sweep), poor = Of("free", "on", "poor", "sprint", degrees, sweep);
                Assert.True(poor.Retained < good.Retained, $"{degrees}°, swept {sweep} s: control 0.3 retained {poor.Retained}, 0.9 {good.Retained}");
                Assert.True(poor.Kept <= poor.Starts / 2, $"{degrees}°, swept {sweep} s: control 0.3 kept {poor.Kept} of {poor.Starts}");
            }
        }

        [Fact]
        public void With_the_cut_a_45_degree_cut_is_no_harder_than_a_90_and_half_stick_no_harder_than_a_sprint()
        {
            foreach (string profile in new[] { "good", "bench", "poor" })
            {
                foreach (string speed in new[] { "half", "sprint" })
                {
                    TurnCurve at45 = Of("free", "on", profile, speed, 45), at90 = Of("free", "on", profile, speed, 90);
                    Assert.True(at45.Retained >= at90.Retained, $"{profile}, {speed}: retained {at45.Retained} at 45°, {at90.Retained} at 90°");
                    if (at45.Kept > 0 && at90.Kept > 0)
                    {
                        Assert.True(at45.KickReadyMaxSeconds <= at90.KickReadyMaxSeconds, $"{profile}, {speed}: kick-ready in {at45.KickReadyMaxSeconds:F2} s at 45°, {at90.KickReadyMaxSeconds:F2} s at 90°");
                    }
                }

                foreach (double degrees in new[] { 45.0, 90.0 })
                {
                    TurnCurve half = Of("free", "on", profile, "half", degrees), sprint = Of("free", "on", profile, "sprint", degrees);
                    Assert.True(half.Retained >= sprint.Retained, $"{profile}, {degrees}°: retained {half.Retained} at half stick, {sprint.Retained} at a sprint");
                }
            }

            TurnCurve poor45 = Of("free", "on", "poor", "sprint", 45), poor90 = Of("free", "on", "poor", "sprint", 90);
            Assert.True(poor45.Retained > poor90.Retained, $"control 0.3 at a sprint retained {poor45.Retained} at 45°, {poor90.Retained} at 90°");
        }

        [Fact]
        public void The_cut_leaves_snapped_turns_and_attached_unchanged()
        {
            foreach (TurnCurve on in Default.Where(c => c.Turn == "on" && (c.Model == "attached" || (c.Degrees >= 135 && c.SweepSeconds == 0))))
            {
                TurnCurve noCut = Without.Single(c => c.Model == on.Model && c.Turn == "on" && c.Profile == on.Profile && c.Speed == on.Speed && c.Degrees == on.Degrees && c.SweepSeconds == on.SweepSeconds);
                Assert.Equal(Row(noCut), Row(on));
            }
        }

        // The reference with the cut off and the turn on: #66 as it was.
        private static readonly List<TurnCurve> Without = TurnCurves.Characterise(Config.With("cutStrength", 0));

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
