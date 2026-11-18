using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #122: the CPU taker's penalty against the reference keeper. Professional penalties are scored
    /// about 75 % of the time; about 17 % are saved and 8 % miss the goal. Technical references,
    /// not approved feel.
    /// </summary>
    public class PenaltyTests
    {
        [Fact]
        public void The_benchmark_counts_every_kick_of_its_shootouts()
        {
            PenaltyTrial t = PenaltyTrials.Run(SpikeScenario.Config(), 5, 6);
            Assert.True(t.Kicks >= 30, $"{t.Kicks} kicks in 6 shootouts");
            Assert.Equal(t.Kicks, t.Scored + t.Saved + t.Missed);
            Assert.True(t.Frame + t.Short <= t.Missed, $"{t.Frame} off the frame and {t.Short} short of {t.Missed} missed");
        }

        [Fact]
        public void The_reference_taker_scores_near_footballs_rate()
        {
            // About 75 % scored, 17 % saved and 8 % missed. Over this many kicks a share's standard
            // error is about 2 %, so the bands are wide enough for the sample and narrow enough to
            // hold the taker near football's split.
            PenaltyTrial t = PenaltyTrials.Run(SpikeScenario.Config(), 5, PenaltyTrials.Shootouts);
            double scored = (double)t.Scored / t.Kicks, saved = (double)t.Saved / t.Kicks, missed = (double)t.Missed / t.Kicks;
            string split = $"{t.Kicks} kicks: {scored:P0} scored, {saved:P0} saved, {missed:P0} missed";
            Assert.True(t.Kicks >= 300, split);
            Assert.True(scored >= 0.65 && scored <= 0.85, split);
            Assert.True(saved >= 0.08 && saved <= 0.27, split);
            Assert.True(missed <= 0.15, split);
        }
    }
}
