using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #130, M5's gate: the CPU's predictions equal the simulation (INV-13). Through CPU matches, every
    /// free ball's predicted flight is the simulation's own ball, step for step, until it is played.
    /// </summary>
    public class PredictionTests
    {
        [Theory]
        [InlineData(5, 3UL)]
        [InlineData(10, 11UL)]
        public void In_a_match_every_free_balls_predicted_flight_is_the_simulations_own(int perSide, ulong seed)
        {
            PredictionTrial t = PredictionTrials.One(SpikeScenario.Config(), perSide, seed, 60);
            Assert.True(t.Predictions > 100, $"{t.Predictions} predictions made");
            Assert.True(t.Checked > 1000, $"{t.Checked} predicted positions checked");
            Assert.True(t.Differed == 0, $"{t.Differed} of {t.Checked} differed; the first: {t.FirstDifference}");
        }
    }
}
