using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#102: the human's view as the host frames it, and the off-screen fairness measurement.</summary>
    public class FairnessTests
    {
        [Fact]
        public void The_view_frames_the_footballer_and_the_ball_at_the_closest_zoom_when_they_are_close()
        {
            var player = new Vec2(0, 0);
            var ball = new Vec2(1, 0);
            Assert.True(HumanView.Shows(player, ball, new Vec2(0.5, HumanView.Closest - 0.1)));
            Assert.False(HumanView.Shows(player, ball, new Vec2(0.5, HumanView.Closest + 0.1)));
            Assert.True(HumanView.Shows(player, ball, new Vec2(0.5 + HumanView.Closest * HumanView.Aspect - 0.1, 0)));
            Assert.False(HumanView.Shows(player, ball, new Vec2(0.5 + HumanView.Closest * HumanView.Aspect + 0.1, 0)));
        }

        [Fact]
        public void The_view_zooms_out_to_fit_both_but_no_wider_than_the_widest()
        {
            // 20 m apart across: half of it plus the margin, 13 m either side of the middle.
            Assert.True(HumanView.Shows(new Vec2(0, -10), new Vec2(0, 10), new Vec2(0, 12.9)));
            Assert.False(HumanView.Shows(new Vec2(0, -10), new Vec2(0, 10), new Vec2(0, 13.1)));

            // 60 m apart: the widest view, which no longer fits them both.
            Assert.False(HumanView.Shows(new Vec2(0, -30), new Vec2(0, 30), new Vec2(0, HumanView.Widest + 0.1)));
        }

        [Fact]
        public void The_indicators_reach_extends_what_counts_as_seen()
        {
            var at = new Vec2(0, HumanView.Closest + 10);
            Assert.False(HumanView.Shows(Vec2.Zero, Vec2.Zero, at));
            Assert.True(HumanView.Shows(Vec2.Zero, Vec2.Zero, at, FairnessTrials.IndicatorReach));
        }

        [Fact]
        public void A_short_5v5_match_measures_decisions_and_stays_within_the_bound()
        {
            SimConfig config = SpikeScenario.Config().With("halfLength", 60);
            FairnessTrial t = FairnessTrials.One(config, "pro", CpuSkill.Pro, seeds: 1);
            Assert.True(t.OpponentDecisions > 100 && t.MateDecisions > 100, $"{t.OpponentDecisions} and {t.MateDecisions} decisions");
            Assert.True(t.OpponentOnUnindicated <= t.OpponentOnHidden && t.OpponentOnHiddenInKind <= t.OpponentOnHidden);
            Assert.True(FairnessTrials.Within(t), $"opponents {t.OpponentShare:P2}, teammates {t.MateShare:P2}");
        }
    }
}
