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

        // A framed area 200 by 300 px either side of its centre; closest 20 px/m, widest 5 px/m.
        private const double HalfW = 200, HalfH = 300, Closest = 20, Widest = 5;

        private static (double X, double Y, double Zoom) Fit(CameraEase e, double px, double bx) => e.Fit(px, 0, bx, 0, HalfW, HalfH, Closest, Widest, Frame);

        private static bool InView((double X, double Y, double Zoom) v, double x) => System.Math.Abs(x - v.X) * v.Zoom <= HalfW;

        [Fact]
        public void Codex_on_141_a_zoom_out_with_both_in_view_eases_rather_than_snapping()
        {
            // Settled close on a footballer and the ball 4 m apart; then both 9 m from the same
            // midpoint. The fit wants 16.7 px/m; they are still in view at 20, so the zoom eases.
            CameraEase e = Eased();
            for (int i = 0; i < 120; i++) Fit(e, -2, 2);
            Assert.Equal(20, e.Zoom, 6);
            var v = Fit(e, -9, 9);
            Assert.True(v.Zoom > 19, $"{v.Zoom:F2} px/m: it snapped toward {e.Fitted:F2}");
            Assert.True(InView(v, -9) && InView(v, 9));
        }

        [Fact]
        public void A_long_pass_is_followed_out_with_the_ball_never_leaving_the_view()
        {
            // The ball rolls away from the footballer at 15 m/s: the zoom lags its fit at times,
            // yet ball and footballer stay in view in every frame.
            CameraEase e = Eased();
            for (int i = 0; i < 120; i++) Fit(e, 0, 2);
            bool lagged = false;
            for (int i = 0; i < 120; i++)
            {
                double ball = 2 + 0.25 * i;
                var v = Fit(e, 0, ball);
                if (v.Zoom > Widest + 1e-9)
                {
                    Assert.True(InView(v, ball), $"frame {i}: ball at {ball:F1} m out of view at {v.Zoom:F2} px/m about {v.X:F2}");
                    Assert.True(InView(v, 0), $"frame {i}: footballer out of view");
                }

                lagged |= v.Zoom > e.Fitted + 1e-6;
            }

            Assert.True(lagged, "the zoom never lagged its fit: no easing out at all");
        }

        [Fact]
        public void Below_the_widest_view_the_centre_is_the_eased_one()
        {
            CameraEase e = Eased();
            for (int i = 0; i < 120; i++)
            {
                var v = Fit(e, 0, 0.1 * i);
                if (v.Zoom > Widest + 1e-9) Assert.Equal((e.CentreX, e.CentreY), (v.X, v.Y));
            }
        }

        [Fact]
        public void At_the_widest_view_the_footballer_stays_in_view_with_the_margin()
        {
            CameraEase e = Eased();
            for (int i = 0; i < 300; i++) Fit(e, 0, 200);
            var v = Fit(e, 0, 200);
            Assert.Equal(Widest, v.Zoom, 9);
            Assert.True(System.Math.Abs(0 - v.X) <= HalfW / Widest - CameraEase.Margin + 1e-9);
        }
    }
}
