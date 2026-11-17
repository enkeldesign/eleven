using System.Collections.Generic;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#126's measures: a switch's cost is compared with the same seed unswitched, over the same window.</summary>
    public class TacticTrialsTests
    {
        [Fact]
        public void A_switchs_control_is_the_same_seeds_unswitched_match_over_the_window_from_the_switch()
        {
            var none = new TacticTrial { Kind = "switch", Name = "none", Seed = 1, AwayShotTicks = { 100, 500, 530, 900 }, AwayGoalTicks = { 530 } };
            var late = new TacticTrial { Kind = "switch", Name = "stoppage-8s", Seed = 1, SwitchedAt = 480 };
            var other = new TacticTrial { Kind = "switch", Name = "none", Seed = 2, AwayShotTicks = { 490 } };
            TacticTrials.Pair(new[] { none, other, late }, windowTicks: 100);

            // Shots at 500 and 530 fall in [480, 580); the one at 100, where the switch was asked, does not.
            Assert.Equal((2, 1), (late.ControlShots, late.ControlGoals));
        }
    }
}
