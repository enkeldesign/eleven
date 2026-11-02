namespace Eleven.Presentation
{
    // All lengths are points: CSS pixels on the web, and the UI's reference unit in Unity.

    /// <summary>The spacing rhythm (#48), named by step.</summary>
    public static class Space
    {
        public const int Xxs = 0;
        public const int Xs = 0;
        public const int S = 0;
        public const int M = 0;
        public const int L = 0;
        public const int Xl = 0;
        public const int Xxl = 0;

        public static readonly int[] Steps = new int[0];
    }

    /// <summary>A component family's geometry and elevation.</summary>
    public readonly struct Shape
    {
        public Shape(int radius, int border, int shadow)
        {
            Radius = radius;
            Border = border;
            Shadow = shadow;
        }

        public int Radius { get; }

        public int Border { get; }

        /// <summary>The hard shadow's offset, down and right, with no blur. Pressed controls move towards it.</summary>
        public int Shadow { get; }
    }

    public static class Shapes
    {
        public static readonly Shape Control = default;
        public static readonly Shape PrimaryAction = default;
        public static readonly Shape Card = default;
        public static readonly Shape Sheet = default;
    }

    public enum Weight
    {
        Regular,
        Bold,
        Heavy,
    }

    /// <summary>A type role: size in points, weight, tracking in em, and how it renders.</summary>
    public readonly struct TypeStyle
    {
        public TypeStyle(int size, Weight weight, double tracking = 0, bool uppercase = false, bool tabular = false)
        {
            Size = size;
            Weight = weight;
            Tracking = tracking;
            Uppercase = uppercase;
            Tabular = tabular;
        }

        public int Size { get; }

        public Weight Weight { get; }

        public double Tracking { get; }

        /// <summary>Rendered in uppercase; the source text, and any accessible name, stay in natural case.</summary>
        public bool Uppercase { get; }

        /// <summary>Fixed-width figures, for values that change.</summary>
        public bool Tabular { get; }
    }

    /// <summary>The type hierarchy (#48): heavy display and labels, calmer prose, tabular data.</summary>
    public static class TypeScale
    {
        public static readonly TypeStyle Micro = default;
        public static readonly TypeStyle Small = default;
        public static readonly TypeStyle Body = default;
        public static readonly TypeStyle Label = default;
        public static readonly TypeStyle Data = default;
        public static readonly TypeStyle Title = default;
        public static readonly TypeStyle Heading = default;
        public static readonly TypeStyle Display = default;
    }

    /// <summary>UI motion (#48). Gameplay timing is not a UI token.</summary>
    public static class Motion
    {
        public const int FastMilliseconds = 0;
        public const int BaseMilliseconds = 0;

        /// <summary>The common easing, as cubic-bezier control points (x1, y1, x2, y2).</summary>
        public static readonly (double X1, double Y1, double X2, double Y2) Easing = default;

        /// <summary>A duration under the reduced-motion preference: movement goes, the static state cue stays.</summary>
        public static int Duration(int milliseconds, bool reducedMotion) => milliseconds;
    }

    /// <summary>Named stacking order, lowest first: no component picks its own number.</summary>
    public enum Layer
    {
        Match = 0,
        Hud = 0,
        Controls = 0,
        Overlay = 0,
        Sheet = 0,
        Dialog = 0,
        Status = 0,
    }

    /// <summary>Touch and focus geometry.</summary>
    public static class Interaction
    {
        /// <summary>The smallest frequently used bounded target, both ways (iOS/iPadOS guidance).</summary>
        public const int MinTarget = 0;

        /// <summary>The gap between a focused component and its focus ring.</summary>
        public const int FocusGap = 0;

        /// <summary>The focus ring's width.</summary>
        public const int FocusRing = 0;
    }
}
