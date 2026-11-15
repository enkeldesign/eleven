using System;
using Eleven.Presentation;
using Xunit;

namespace Eleven.Presentation.Tests
{
    /// <summary>#102: the off-screen cues' placement.</summary>
    public class EdgeCueTests
    {
        // A 400 × 600 px play area at the origin, 10 px a metre, 2 px a point.
        private static EdgeCue? At(double x, double y, double reach = EdgeCues.Reach) => EdgeCues.Place(x, y, 0, 0, 400, 600, 10, 2, reach);

        [Fact]
        public void A_footballer_in_view_has_no_cue()
        {
            Assert.Null(At(200, 300));
            Assert.Null(At(0, 600));
        }

        [Fact]
        public void A_footballer_within_reach_off_an_edge_is_cued_on_it_pointing_at_him()
        {
            EdgeCue? c = At(200, -50);
            Assert.NotNull(c);
            Assert.Equal(200, c!.Value.X, 6);
            Assert.Equal(EdgeCues.Inset * 2, c.Value.Y, 6);
            Assert.Equal(-Math.PI / 2, c.Value.Angle, 6);

            EdgeCue? side = At(450, 300);
            Assert.Equal(400 - EdgeCues.Inset * 2, side!.Value.X, 6);
            Assert.Equal(0, side.Value.Angle, 6);
        }

        [Fact]
        public void A_cue_stays_inside_the_play_area_even_in_a_corner()
        {
            EdgeCue? c = At(-30, 640);
            Assert.NotNull(c);
            Assert.InRange(c!.Value.X - c.Value.Size / 2, 0, 400);
            Assert.InRange(c.Value.Y + c.Value.Size / 2, 0, 600);
        }

        [Fact]
        public void A_nearer_footballer_has_a_larger_cue_and_one_past_the_reach_none()
        {
            double near = At(200, -10)!.Value.Size, far = At(200, -140)!.Value.Size;
            Assert.True(near > far, $"{near} px near, {far} px far");
            Assert.InRange(far, EdgeCues.Smallest * 2, EdgeCues.Largest * 2);
            Assert.Null(At(200, -151));
            Assert.Null(At(200, -10, reach: 0));
        }
    }
}
