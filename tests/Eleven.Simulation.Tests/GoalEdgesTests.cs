using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#68: the goal's edge cases, measured, and their recorded technical reference.</summary>
    public class GoalEdgesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "goal-edges-reference.csv");

        private static readonly SimConfig Config = new SimConfig();

        private static readonly List<GoalEdge> Default = GoalEdges.Characterise(Config);

        [Fact]
        public void The_default_edges_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            GoalEdges.WriteCsv(csv, Config, Default);
            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        // A ball clear of the frame by more than a centimetre either way never touches it: inside it
        // scores, outside it misses. Within a ball's radius of the frame's faces, it hits.
        [Fact]
        public void Clear_of_the_frame_a_shot_scores_or_misses_and_near_it_the_shot_hits()
        {
            double r = Config.BallRadius, w = Config.PostWidth;
            foreach (GoalEdge e in Default)
            {
                string at = $"{e.Kind} {e.SpeedMps} m/s at {e.OffsetMetres:+0.00;-0.00} m";
                if (e.OffsetMetres < -r - 0.01) Assert.True(e.Outcome == "goal" && e.Part == "none", $"{at}: {e.Outcome}, {e.Part}");
                else if (e.OffsetMetres > w + r + 0.01) Assert.True(e.Outcome == (e.Kind == "ground" ? "wide" : "over") && e.Part == "none", $"{at}: {e.Outcome}, {e.Part}");
                else if (e.OffsetMetres > -r + 0.01 && e.OffsetMetres < w + r - 0.01) Assert.True(e.Part != "none", $"{at}: missed the frame");
            }
        }

        [Fact]
        public void A_contact_never_returns_more_speed_than_the_shot_had()
        {
            foreach (GoalEdge e in Default.Where(e => e.Part != "none"))
            {
                Assert.True(e.ReboundMps < e.SpeedMps, $"{e.Kind} {e.SpeedMps} m/s at {e.OffsetMetres:F2} m: rebound {e.ReboundMps:F3} m/s");
            }
        }

        [Fact]
        public void A_shot_straight_at_a_post_or_the_bar_comes_straight_back()
        {
            // Aimed at the axis: offset postWidth / 2, the nearest step to it.
            foreach (GoalEdge e in Default.Where(e => System.Math.Abs(e.OffsetMetres - Config.PostWidth / 2) < 0.026))
            {
                Assert.True(e.TurnDegrees > 160, $"{e.Kind} {e.SpeedMps} m/s at {e.OffsetMetres:F2} m turned only {e.TurnDegrees:F1}°");
            }
        }
    }
}
