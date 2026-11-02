using Eleven.Host;
using Xunit;

namespace Eleven.Host.Tests
{
    /// <summary>
    /// The build line's dropped-frame count (#5, #55): over the last minute of play. Time the host
    /// doesn't record (play waiting under the note sheet, a stall it discards) must not leave a hole
    /// that keeps the window short.
    /// </summary>
    public class FramePacingTests
    {
        private const double Frame = 1.0 / 60;

        private static double Play(FramePacing pacing, double now, double seconds, double frame = Frame)
        {
            int frames = (int)System.Math.Round(seconds / frame);
            for (int i = 0; i < frames; i++)
            {
                now += frame;
                pacing.Record(now, frame);
            }

            return now;
        }

        [Fact]
        public void The_window_is_the_last_minute_of_play_not_of_the_clock()
        {
            var pacing = new FramePacing();
            double now = Play(pacing, 0, 30);
            now += 20; // play waited: nothing recorded
            Play(pacing, now, 40);

            Assert.InRange(pacing.CoveredSeconds, 59.4, 60.0);
        }

        [Fact]
        public void Dropped_frames_count_over_the_last_minute_of_play()
        {
            var pacing = new FramePacing();
            double now = Play(pacing, 0, 25);
            // Five frames that took three intervals each: two dropped each.
            for (int i = 0; i < 5; i++)
            {
                now += 3 * Frame;
                pacing.Record(now, 3 * Frame);
            }

            now = Play(pacing, now, 5);
            now += 20; // play waited
            Play(pacing, now, 40);

            // The last 60 s of play reach back past the wait to the slow frames.
            Assert.Equal(10, pacing.Dropped);
        }

        [Fact]
        public void A_minute_of_steady_play_drops_nothing_and_covers_the_minute()
        {
            var pacing = new FramePacing();
            Play(pacing, 0, 90);

            Assert.Equal(0, pacing.Dropped);
            Assert.InRange(pacing.CoveredSeconds, 59.4, 60.0);
        }
    }
}
