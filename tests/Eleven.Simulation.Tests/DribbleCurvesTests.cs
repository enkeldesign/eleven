using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#59, #60: the ball-control characterisation under FREE and ATTACHED, and its recorded technical reference.</summary>
    public class DribbleCurvesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "dribble-reference.csv");

        private static readonly SimConfig Config = SpikeScenario.Config();

        private static readonly List<DribbleCurve> Default = DribbleCurves.Characterise(Config);

        private static DribbleCurve Of(string name, string profile, string model = "free") => Default.Single(c => c.Case == name && c.Profile == profile && c.Model == model);

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            DribbleCurves.WriteCsv(csv, Config, Default);

            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        [Fact]
        public void The_characterisation_runs_every_case_under_both_models()
        {
            Assert.Equal(ControlModel.Free, Config.ControlModel);
            Assert.Equal(Default.Count(c => c.Model == "free"), Default.Count(c => c.Model == "attached"));
            Assert.All(Default.Where(c => c.Model == "free" && c.Case != "receive-and-go" && c.Case != "difficult-first-touch" && c.Case != "sprint-stop"),
                c => Assert.True(c.Touches > 0, $"free/{c.Case}/{c.Profile}: no touches"));
            Assert.All(Default.Where(c => c.Model == "attached"), c => Assert.Equal(0, c.Touches));
            Assert.All(Default.Where(c => c.Model == "attached" && (c.Case == "kick-release" || c.Case == "receive-and-go" || c.Case == "switch-in")),
                c => Assert.True(c.Acquired > 0, $"attached/{c.Case}/{c.Profile}: never taken"));
        }

        // ATTACHED (#60): the same relationships, and its own.
        [Fact]
        public void Under_attached_the_ball_runs_further_ahead_at_speed_and_the_correction_is_bounded()
        {
            SimConfig attached = Config.WithControlModel(ControlModel.Attached);
            foreach (string profile in new[] { "good", "poor" })
            {
                Assert.True(Of("sprint", profile, "attached").MeanLeadMetres > Of("straight-jog", profile, "attached").MeanLeadMetres, profile);
                foreach (DribbleCurve c in Default.Where(c => c.Model == "attached" && c.Profile == profile && !double.IsNaN(c.MaxCorrectionMps2)))
                {
                    double most = attached.AttachedAcceleration * (0.5 + 0.5 * c.BallControl) + attached.RollingFriction * attached.Gravity + 1e-6;
                    Assert.True(c.MaxCorrectionMps2 <= most, $"attached/{c.Case}/{profile}: {c.MaxCorrectionMps2:F2} m/s², more than {most:F2}");
                }
            }
        }

        [Fact]
        public void Under_attached_a_sharper_turn_and_poorer_control_lose_more()
        {
            Assert.True(Of("turn-90", "poor", "attached").MaxSeparationMetres > Of("turn-45", "poor", "attached").MaxSeparationMetres);
            Assert.True(Of("turn-45", "poor", "attached").LostSeconds > Of("turn-45", "good", "attached").LostSeconds);
        }

        [Fact]
        public void Under_attached_a_good_first_touch_settles_sooner_and_the_hard_ball_beats_the_poor_one()
        {
            Assert.True(Of("receive-and-go", "good", "attached").SettleSeconds < Of("receive-and-go", "poor", "attached").SettleSeconds);
            Assert.True(double.IsNaN(Of("difficult-first-touch", "poor", "attached").SettleSeconds));
            Assert.True(Of("difficult-first-touch", "poor", "attached").Released > 0);
        }

        [Fact]
        public void Under_attached_a_kick_releases_the_ball_and_a_sprint_flick_connects()
        {
            foreach (string profile in new[] { "good", "poor" })
            {
                DribbleCurve kick = Of("kick-release", profile, "attached");
                Assert.True(kick.Released > 0, $"{profile}: the kick didn't release the ball");
                Assert.True(kick.ReleaseSpeedMps > 5, $"{profile}: released at {kick.ReleaseSpeedMps:F1} m/s");
                Assert.Equal(0, kick.TouchesAfterKick);
                Assert.True(Of("sprint-flick", profile, "attached").FlickConnectShare > 0.5, profile);
            }
        }

        [Fact]
        public void A_switch_into_either_model_mid_dribble_takes_control_on_its_first_step()
        {
            foreach (string model in new[] { "free", "attached" })
            {
                foreach (string profile in new[] { "good", "poor" })
                {
                    Assert.Equal(0, Of("switch-in", profile, model).ControlAfterSwitchSeconds);
                }
            }
        }

        // The relationships #29 and SOL's handoff ask the range to show; the values themselves are
        // technical references.
        [Fact]
        public void The_ball_runs_further_ahead_at_speed()
        {
            foreach (string profile in new[] { "good", "poor" })
            {
                Assert.True(Of("sprint", profile).MeanLeadMetres > Of("straight-jog", profile).MeanLeadMetres, profile);
            }
        }

        [Fact]
        public void A_sharper_turn_and_poorer_control_lose_more()
        {
            Assert.True(Of("turn-90", "poor").MaxSeparationMetres > Of("turn-45", "poor").MaxSeparationMetres);
            Assert.True(Of("turn-90", "poor").LostSeconds > Of("turn-90", "good").LostSeconds);
            Assert.True(Of("turn-45", "poor").LostSeconds >= Of("turn-45", "good").LostSeconds);
        }

        [Fact]
        public void A_good_first_touch_settles_sooner_and_a_hard_ball_beats_both()
        {
            Assert.True(Of("receive-and-go", "good").SettleSeconds < Of("receive-and-go", "poor").SettleSeconds);
            Assert.True(double.IsNaN(Of("difficult-first-touch", "good").SettleSeconds));
            Assert.True(double.IsNaN(Of("difficult-first-touch", "poor").SettleSeconds));
        }

        // SOL on #59: the reference lead keeps a flick at a sprint playable, and a lead beyond the
        // kick reach makes it miss. The share itself is a technical reference.
        [Fact]
        public void A_sprint_flick_connects_at_the_reference_lead_and_not_far_beyond_kick_reach()
        {
            foreach (string profile in new[] { "good", "poor" })
            {
                DribbleCurve c = Of("sprint-flick", profile);
                Assert.True(c.Touches > 0, $"{profile}: not dribbling before the flick");
                Assert.True(c.FlickConnectShare > 0.5, $"{profile}: {c.FlickConnectShare:P0} of sprint flicks connect");
            }

            SimConfig far = Config.With("dribbleLeadFast", 1.2).With("controlReach", 1.3);
            DribbleCurve beyond = DribbleCurves.Characterise(far).Single(c => c.Model == "free" && c.Case == "sprint-flick" && c.Profile == "good");
            Assert.True(beyond.Touches > 0, "not dribbling at the far lead");
            Assert.True(beyond.FlickConnectShare < Of("sprint-flick", "good").FlickConnectShare,
                $"lead 1.2 m: {beyond.FlickConnectShare:P0}; reference: {Of("sprint-flick", "good").FlickConnectShare:P0}");
        }

        [Fact]
        public void A_kick_releases_the_ball()
        {
            foreach (string profile in new[] { "good", "poor" })
            {
                DribbleCurve c = Of("kick-release", profile);
                Assert.True(c.Touches > 0, $"{profile}: not dribbling before the kick");
                Assert.True(c.ReleaseSpeedMps > 5, $"{profile}: released at {c.ReleaseSpeedMps:F1} m/s");
                Assert.Equal(0, c.TouchesAfterKick);
            }
        }
    }
}
