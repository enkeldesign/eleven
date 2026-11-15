using System;

namespace Eleven.Presentation
{
    /// <summary>
    /// An off-screen footballer's cue (#102): a pointer on the play area's edge, toward him, larger
    /// the nearer he is. Screen pixels, y down. Presentation only: it reads where the camera drew
    /// him and changes nothing.
    /// </summary>
    public readonly struct EdgeCue
    {
        public EdgeCue(double x, double y, double angle, double size)
        {
            X = x;
            Y = y;
            Angle = angle;
            Size = size;
        }

        /// <summary>px. The cue's centre, inside the play area.</summary>
        public double X { get; }

        public double Y { get; }

        /// <summary>rad. Toward the footballer, screen x right and y down: 0 points right.</summary>
        public double Angle { get; }

        /// <summary>px. The cue's width and height.</summary>
        public double Size { get; }
    }

    /// <summary>Where the edge cues go (#102). Technical references, not approved feel.</summary>
    public static class EdgeCues
    {
        /// <summary>m. How far outside the view a footballer is cued: the reference for <c>?cues=</c>.</summary>
        public const double Reach = 15;

        /// <summary>pt. A cue's size for a footballer just off the edge, and at the reach.</summary>
        public const double Largest = 16, Smallest = 9;

        /// <summary>pt. From the play area's edge to a cue's centre.</summary>
        public const double Inset = Spacing.S;

        /// <summary>
        /// The cue for a footballer drawn at <paramref name="x"/>, <paramref name="y"/>, or null when
        /// he is in the play area or further than <paramref name="reachMetres"/> outside it.
        /// </summary>
        /// <param name="left">The play area, in px.</param>
        /// <param name="pixelsPerMetre">The camera's scale: distance outside is in metres on the pitch.</param>
        /// <param name="pixelsPerPoint">For the cue's size and inset, which are in points.</param>
        public static EdgeCue? Place(double x, double y, double left, double top, double width, double height,
            double pixelsPerMetre, double pixelsPerPoint, double reachMetres = Reach)
        {
            if (reachMetres <= 0 || pixelsPerMetre <= 0 || width <= 0 || height <= 0) return null;
            double right = left + width, bottom = top + height;
            if (x >= left && x <= right && y >= top && y <= bottom) return null;

            double dx = x < left ? left - x : x > right ? x - right : 0;
            double dy = y < top ? top - y : y > bottom ? y - bottom : 0;
            double outside = Math.Sqrt(dx * dx + dy * dy) / pixelsPerMetre;
            if (outside > reachMetres) return null;

            double inset = Math.Min(Inset * pixelsPerPoint, Math.Min(width, height) / 2);
            double cx = Math.Max(left + inset, Math.Min(right - inset, x));
            double cy = Math.Max(top + inset, Math.Min(bottom - inset, y));
            double t = outside / reachMetres;
            double size = (Largest + (Smallest - Largest) * t) * pixelsPerPoint;
            return new EdgeCue(cx, cy, Math.Atan2(y - cy, x - cx), size);
        }
    }
}
