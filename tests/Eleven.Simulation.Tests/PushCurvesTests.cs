using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#63: the push-and-chase characterisation beside FREE's short touches, and its recorded technical reference.</summary>
    public class PushCurvesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "push-reference.csv");

        private static readonly SimConfig Config = SpikeScenario.Config();

        private static readonly List<PushCurve> Default = PushCurves.Characterise(Config);

        private static PushCurve Of(double pushSpeed, string profile) => Default.Single(c => c.PushSpeed == pushSpeed && c.Profile == profile);

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            PushCurves.WriteCsv(csv, Config, Default);

            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void Every_push_speed_is_measured_at_three_profiles_beside_free()
        {
            Assert.Equal(PushCurves.Speeds.Count * 3, Default.Count);
            Assert.All(new[] { "good", "bench", "poor" }, p => Assert.Equal(0, Of(0, p).Pushes));
            Assert.All(Default.Where(c => c.PushSpeed > 0), c => Assert.True(c.Pushes > 0, $"{c.PushSpeed} m/s {c.Profile} never pushed"));
        }

        [Fact]
        public void Free_keeps_the_ball_in_reach_and_every_sprint_flick_connects()
        {
            PushCurve free = Of(0, "bench");
            Assert.True(free.PeakSeparationMetres <= Config.ControlReach + 0.15);
            Assert.Equal(1, free.FlickConnectShare);
        }

        [Fact]
        public void A_harder_push_runs_further_and_takes_longer_to_catch()
        {
            PushCurve[] rising = PushCurves.Speeds.Where(s => s > 0).Select(s => Of(s, "bench")).ToArray();
            for (int i = 1; i < rising.Length; i++)
            {
                Assert.True(rising[i].PeakSeparationMetres > rising[i - 1].PeakSeparationMetres, $"peak at {rising[i].PushSpeed} m/s");
                Assert.True(rising[i].CycleSeconds > rising[i - 1].CycleSeconds, $"cycle at {rising[i].PushSpeed} m/s");
                Assert.True(rising[i].FlickConnectShare <= rising[i - 1].FlickConnectShare, $"flicks at {rising[i].PushSpeed} m/s");
            }
        }

        [Fact]
        public void A_moderate_push_has_a_timing_window_and_an_overpush_is_lost()
        {
            // A push the footballer catches, with flicks that connect early and late in the chase
            // and miss between; and one they don't catch within the time that counts as lost.
            PushCurve moderate = Of(1.0, "bench");
            Assert.False(moderate.Lost);
            Assert.True(moderate.PeakSeparationMetres > Config.ControlReach + 0.5);
            Assert.InRange(moderate.FlickConnectShare, 0.01, 0.5);
            Assert.True(moderate.FlickWindowReopensSeconds > moderate.FlickWindowClosesSeconds + 1, "no gap between the flicks that connect");

            Assert.True(Of(4.0, "bench").Lost);
        }

        [Fact]
        public void A_poor_footballers_push_is_weaker()
        {
            // The push is a bounded touch: with less ball control it reaches less of what it asked.
            Assert.True(Of(1.5, "poor").CycleSeconds < Of(1.5, "good").CycleSeconds - 0.3);
        }
    }
}
