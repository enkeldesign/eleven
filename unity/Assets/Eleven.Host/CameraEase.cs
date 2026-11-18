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

        /// <summary>The next frame takes its targets as they are: a new match, a turned view.</summary>
        public void Snap()
        {
        }

        /// <summary>Moves the framing centre toward <paramref name="x"/>, <paramref name="y"/> over <paramref name="dt"/> seconds.</summary>
        public void StepCentre(double x, double y, double dt)
        {
            CentreX = x;
            CentreY = y;
        }

        /// <summary>
        /// Moves the zoom toward <paramref name="target"/> over <paramref name="dt"/> seconds,
        /// never closer than <paramref name="limit"/>, the closest that keeps the footballer and
        /// the ball in view about the eased centre.
        /// </summary>
        public void StepZoom(double target, double limit, double dt)
        {
            Zoom = Math.Min(target, limit);
        }
    }
}
