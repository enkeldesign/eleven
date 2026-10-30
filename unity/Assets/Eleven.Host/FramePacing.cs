using System;
using System.Collections.Generic;

namespace Eleven.Host
{
    /// <summary>
    /// Counts dropped frames over the last minute, for the device session's frame-pacing
    /// measurement (#5).
    ///
    /// The display's frame interval is the shortest frame time the device sustains: the 5th
    /// percentile of the window, remembered as a running minimum, and never longer than 1/60 s,
    /// the slowest refresh rate ELEVEN targets. So 60 Hz and 120 Hz displays both work, and a
    /// sustained slowdown can't redefine "on time" and hide the frames it drops. A frame that took
    /// more than 1.5 intervals stands for the frames that should have been shown in its place: one
    /// that took three intervals dropped two. Frames the host discards as pauses (backgrounded,
    /// stalled) are not recorded at all.
    /// </summary>
    internal sealed class FramePacing
    {
        public const double WindowSeconds = 60;
        private const double ReportEverySeconds = 0.5;
        private const double SlowestDisplayInterval = 1.0 / 60;
        private const int CalibrationFrames = 20;

        private readonly Queue<(double At, double Seconds)> _frames = new Queue<(double, double)>();
        private double[] _sorted = new double[1024];
        private double _reportedAt = double.NegativeInfinity;

        /// <summary>Frames recorded in the window.</summary>
        public int Frames { get; private set; }

        /// <summary>Frames that should have been shown but weren't, in the window.</summary>
        public int Dropped { get; private set; }

        /// <summary>The display's frame interval, as calibrated so far.</summary>
        public double IntervalSeconds { get; private set; } = SlowestDisplayInterval;

        /// <summary>Seconds of play the window covers: up to <see cref="WindowSeconds"/>.</summary>
        public double CoveredSeconds { get; private set; }

        /// <param name="now">When the frame ended, in seconds on any steady clock.</param>
        /// <param name="seconds">How long the frame took.</param>
        public void Record(double now, double seconds)
        {
            _frames.Enqueue((now, seconds));
            while (_frames.Count > 0 && _frames.Peek().At <= now - WindowSeconds) _frames.Dequeue();
            if (now - _reportedAt >= ReportEverySeconds) Report(now);
        }

        /// <summary>Starts a new window, e.g. after a pause. The display's interval is kept.</summary>
        public void Clear()
        {
            _frames.Clear();
            Frames = Dropped = 0;
            CoveredSeconds = 0;
            _reportedAt = double.NegativeInfinity;
        }

        private void Report(double now)
        {
            _reportedAt = now;
            int count = _frames.Count;
            if (_sorted.Length < count) _sorted = new double[Math.Max(count, _sorted.Length * 2)];
            int i = 0;
            double covered = 0;
            foreach ((double _, double seconds) in _frames)
            {
                _sorted[i++] = seconds;
                covered += seconds;
            }

            Array.Sort(_sorted, 0, count);
            if (count >= CalibrationFrames) IntervalSeconds = Math.Min(IntervalSeconds, _sorted[count / 20]);

            double interval = IntervalSeconds;
            int dropped = 0;
            for (int j = count - 1; j >= 0 && _sorted[j] > 1.5 * interval; j--)
            {
                dropped += (int)Math.Round(_sorted[j] / interval) - 1;
            }

            Frames = count;
            Dropped = dropped;
            CoveredSeconds = Math.Min(WindowSeconds, covered);
        }
    }
}
