using System;
using System.Linq;
using Eleven.Presentation;
using Xunit;

namespace Eleven.Presentation.Tests
{
    /// <summary>#101: the first action sprites.</summary>
    public class ActionSpriteTests
    {
        public static TheoryData<Pose> Poses()
        {
            var data = new TheoryData<Pose>();
            foreach (Pose p in ActionSprites.All) data.Add(p);
            return data;
        }

        [Theory]
        [MemberData(nameof(Poses))]
        public void Each_grid_is_a_full_square_of_known_colours(Pose pose)
        {
            for (int row = 0; row < ActionSprites.Size; row++)
            {
                string r = ActionSprites.Row(pose, row);
                Assert.Equal(ActionSprites.Size, r.Length);
                Assert.All(r, c => Assert.Contains(c, ".oksthfbg"));
            }

            Assert.Throws<IndexOutOfRangeException>(() => ActionSprites.Row(pose, ActionSprites.Size));
        }

        [Theory]
        [MemberData(nameof(Poses))]
        public void The_kit_is_the_sides_token_unchanged_and_outlined(Pose pose)
        {
            Rgba[] pixels = ActionSprites.Draw(pose, Domain.Opponent);
            Assert.Equal(ActionSprites.Size * ActionSprites.Size, pixels.Length);
            if (pose != Pose.Grounded && pose != Pose.KeeperDive) Assert.Contains(Domain.Opponent, pixels);
            Assert.Contains(ActionSprites.Outline, pixels);
        }

        [Fact]
        public void Each_kit_reads_on_both_grass_shades_and_its_outline_edges_it()
        {
            // The kit is the silhouette's mass against the grass; the ink outline separates it from
            // a neighbour's and the shorts.
            foreach (Rgba kit in new[] { Domain.Player, Domain.Keeper, Domain.Opponent, Domain.OpponentKeeper })
            {
                Assert.True(Rgba.Contrast(kit, Domain.Pitch) >= 3, $"{kit} on the pitch");
                Assert.True(Rgba.Contrast(kit, Domain.PitchMown) >= 3, $"{kit} on the mown pitch");
                Assert.True(Rgba.Contrast(ActionSprites.Outline, kit) >= 3, $"the outline on {kit}");
            }
        }

        [Fact]
        public void Every_pose_has_its_own_silhouette()
        {
            bool[][] masks = ActionSprites.All.Select(p => ActionSprites.Draw(p, Domain.Player).Select(c => c.A > 0).ToArray()).ToArray();
            for (int a = 0; a < masks.Length; a++)
            {
                for (int b = a + 1; b < masks.Length; b++)
                {
                    int differ = masks[a].Zip(masks[b], (x, y) => x != y).Count(d => d);
                    Assert.True(differ >= 6, $"{ActionSprites.All[a]} and {ActionSprites.All[b]} differ in {differ} pixels");
                }
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0.3, 0)]
        [InlineData(0.5, 45)]
        [InlineData(Math.PI / 2, 90)]
        [InlineData(-Math.PI / 2, 270)]
        [InlineData(Math.PI, 180)]
        [InlineData(-Math.PI, 180)]
        public void A_heading_is_drawn_to_the_nearest_eighth_of_a_turn(double radians, float degrees)
        {
            Assert.Equal(degrees, ActionSprites.Facing(radians));
        }
    }
}
