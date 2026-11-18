using Eleven.Host;
using Xunit;

namespace Eleven.Host.Tests
{
    /// <summary>#139, from Erik's capture: "The zoom is wild. In/out all the time."</summary>
    public class CameraEaseTests
    {
        private const double Frame = 1.0 / 60;

        private static CameraEase Eased() => new CameraEase { OutSeconds = 0.3, InSeconds = 1.5, Hold = 0.1, PanSeconds = 0.25 };

        // Settled at zoom 20 px/m and the centre at the origin.
        private static CameraEase Settled()
        {
            CameraEase e = Eased();
            e.StepZoom(20, 100, Frame);
            e.StepCentre(0, 0, Frame);
            return e;
        }

        [Fact]
        public void At_zero_it_follows_at_once_as_before()
        {
            var e = new CameraEase();
            e.StepZoom(20, 100, Frame);
            e.StepZoom(40, 100, Frame);
            e.StepCentre(10, -5, Frame);
            Assert.Equal((40.0, 10.0, -5.0), (e.Zoom, e.CentreX, e.CentreY));
        }

        [Fact]
        public void The_first_frame_takes_its_target_and_a_snap_starts_over()
        {
            CameraEase e = Eased();
            e.StepZoom(25, 100, Frame);
            Assert.Equal(25, e.Zoom);
            e.StepZoom(40, 100, Frame);
            Assert.NotEqual(40, e.Zoom);
            e.Snap();
            e.StepZoom(40, 100, Frame);
            e.StepCentre(7, 7, Frame);
            Assert.Equal((40.0, 7.0, 7.0), (e.Zoom, e.CentreX, e.CentreY));
        }

        [Fact]
        public void It_zooms_in_slowly_and_out_promptly()
        {
            CameraEase inward = Settled();
            inward.StepZoom(40, 100, Frame);
            double inStep = inward.Zoom - 20;
            Assert.InRange(inStep, 0.01, 0.5);

            CameraEase outward = Settled();
            outward.StepZoom(10, 100, Frame);
            double outStep = 20 - outward.Zoom;
            Assert.True(outStep > 3 * inStep * 10 / 20, $"out {outStep:F3} px/m a frame, in {inStep:F3}");

            // Three time constants in, most of the way there.
            for (int i = 0; i < (int)(3 * 1.5 / Frame); i++) inward.StepZoom(40, 100, Frame);
            Assert.InRange(inward.Zoom, 38, 40);
        }

        [Fact]
        public void It_never_zooms_closer_than_keeps_both_in_view()
        {
            CameraEase e = Settled();
            e.StepZoom(10, 12, Frame);
            Assert.True(e.Zoom <= 12, $"{e.Zoom:F2} px/m, limit 12");
        }

        [Fact]
        public void A_small_change_is_left_alone()
        {
            CameraEase e = Settled();
            for (int i = 0; i < 60; i++) e.StepZoom(21.5, 100, Frame);
            Assert.Equal(20, e.Zoom);
        }

        [Fact]
        public void The_centre_pans_rather_than_cuts()
        {
            CameraEase e = Settled();
            e.StepCentre(20, 0, Frame);
            Assert.InRange(e.CentreX, 0.1, 5);
            for (int i = 0; i < (int)(3 * 0.25 / Frame); i++) e.StepCentre(20, 0, Frame);
            Assert.InRange(e.CentreX, 18.5, 20);
        }
    }
}
