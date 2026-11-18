using System;

namespace Eleven.Host
{
    /// <summary>
    /// The camera's easing (#139), from Erik's capture: "The zoom is wild. In/out all the time."
    /// The fitted zoom and framing centre change every frame with the footballer and the ball;
    /// this follows them smoothly. Engine-free, so .NET tests it. Zoom is in pixels per metre:
    /// smaller is further out. Presentation only: it reads nothing but what it is given (INV-12).
    /// </summary>
    internal sealed class CameraEase
    {
        /// <summary>s. The time constant zooming out; 0 snaps.</summary>
        public double OutSeconds { get; set; }

        /// <summary>s. The time constant zooming in; 0 snaps.</summary>
        public double InSeconds { get; set; }

        /// <summary>The dead zone: a change of zoom smaller than this fraction of it is left alone.</summary>
        public double Hold { get; set; }

        /// <summary>s. The time constant of the framing centre; 0 snaps.</summary>
        public double PanSeconds { get; set; }

        public double Zoom { get; private set; }

        public double CentreX { get; private set; }

        public double CentreY { get; private set; }

        /// <summary>The last frame's fitted zoom, unsmoothed: what the camera showed before the easing, for the trace.</summary>
        public double Fitted { get; private set; }

        private bool _zoomSet, _centreSet, _zooming;

        // A move of the zoom ends within this fraction of its target.
        private const double Settled = 0.005;

        /// <summary>The next frame takes its targets as they are: a new match, a turned view.</summary>
        public void Snap() => _zoomSet = _centreSet = _zooming = false;

        /// <summary>Moves the framing centre toward <paramref name="x"/>, <paramref name="y"/> over <paramref name="dt"/> seconds.</summary>
        public void StepCentre(double x, double y, double dt)
        {
            if (!_centreSet || PanSeconds <= 0)
            {
                CentreX = x;
                CentreY = y;
                _centreSet = true;
                return;
            }

            double a = Approach(dt, PanSeconds);
            CentreX += (x - CentreX) * a;
            CentreY += (y - CentreY) * a;
        }

        /// <summary>
        /// Moves the zoom toward <paramref name="target"/> over <paramref name="dt"/> seconds,
        /// never closer than <paramref name="limit"/>, the closest that keeps the footballer and
        /// the ball in view about the eased centre.
        /// </summary>
        public void StepZoom(double target, double limit, double dt)
        {
            if (!_zoomSet || Zoom <= 0)
            {
                Zoom = Math.Min(target, limit);
                _zoomSet = true;
                return;
            }

            // The dead zone decides whether a move starts; once moving it settles all the way, so
            // the zoom neither jitters on small changes nor stops short of where it was going.
            double change = target - Zoom;
            if (Math.Abs(change) > Hold * Zoom) _zooming = true;
            else if (Math.Abs(change) <= Settled * Zoom) _zooming = false;
            if (_zooming)
            {
                // Out (smaller) promptly, in slowly; either at zero snaps.
                double seconds = change < 0 ? OutSeconds : InSeconds;
                Zoom += seconds > 0 ? change * Approach(dt, seconds) : change;
            }

            if (Zoom > limit) Zoom = limit;
        }

        /// <summary>
        /// The view of one frame (#139): the footballer at (<paramref name="px"/>, <paramref name="py"/>)
        /// and the ball at (<paramref name="bx"/>, <paramref name="by"/>), in metres on axes aligned
        /// with the screen, framed in an area <paramref name="halfWidth"/> by <paramref name="halfHeight"/>
        /// pixels either side of its centre, between the <paramref name="closest"/> and
        /// <paramref name="widest"/> zooms (pixels per metre). Steps the easing and gives the centre
        /// and the zoom to show.
        /// </summary>
        public (double X, double Y, double Zoom) Fit(double px, double py, double bx, double by, double halfWidth, double halfHeight,
            double closest, double widest, double dt)
        {
            const double margin = Margin, keep = Keep;
            StepCentre((px + bx) / 2, (py + by) / 2, dt);
            double cx = CentreX, cy = CentreY;

            // The fitted zoom: both with the margin round them, about their midpoint.
            double target = Math.Min(closest, Math.Min(halfWidth / (Math.Abs(bx - px) / 2 + margin), halfHeight / (Math.Abs(by - py) / 2 + margin)));
            target = Math.Max(target, widest);
            Fitted = target;

            // Never closer than keeps both in view, `keep` to spare, about the eased centre.
            double needX = Math.Max(Math.Abs(px - cx), Math.Abs(bx - cx)) + keep;
            double needY = Math.Max(Math.Abs(py - cy), Math.Abs(by - cy)) + keep;
            double limit = Math.Max(widest, Math.Min(closest, Math.Min(halfWidth / needX, halfHeight / needY)));
            StepZoom(target, limit, dt);
            double zoom = Math.Max(widest, Math.Min(closest, Zoom));

            // The footballer stays in view with the margin.
            double reachX = Math.Max(0, halfWidth / zoom - margin), reachY = Math.Max(0, halfHeight / zoom - margin);
            double overX = Math.Abs(px - cx) - reachX, overY = Math.Abs(py - cy) - reachY;
            if (overX > 0) cx += Math.Sign(px - cx) * overX;
            if (overY > 0) cy += Math.Sign(py - cy) * overY;
            return (cx, cy, zoom);
        }

        /// <summary>m. The framing's margin round the footballer and the ball (#31).</summary>
        public const double Margin = 3;

        /// <summary>m. The least room the eased zoom keeps round both, about the eased centre.</summary>
        public const double Keep = Margin;

        // The share of the way a first-order follower covers in dt at the time constant.
        private static double Approach(double dt, double seconds) => 1 - Math.Exp(-dt / seconds);
    }
}
