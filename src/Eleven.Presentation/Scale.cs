namespace Eleven.Presentation
{
    // All lengths are points: CSS pixels on the web, and the UI's reference unit in Unity.

    /// <summary>The spacing rhythm (#48), named by step.</summary>
    public static class Spacing
    {
        public const int Xxs = 3;
        public const int Xs = 6;
        public const int S = 9;
        public const int M = 12;
        public const int L = 18;
        public const int Xl = 24;
        public const int Xxl = 36;

        public static readonly int[] Steps = { Xxs, Xs, S, M, L, Xl, Xxl };
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
        public static readonly Shape Control = new Shape(12, 3, 3);
        public static readonly Shape PrimaryAction = new Shape(12, 3, 4);
        public static readonly Shape Card = new Shape(18, 4, 6);
        public static readonly Shape Sheet = new Shape(24, 4, 8);
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
        public static readonly TypeStyle Micro = new TypeStyle(11, Weight.Bold, 0.04, uppercase: true);
        public static readonly TypeStyle Small = new TypeStyle(13, Weight.Regular);
        public static readonly TypeStyle Body = new TypeStyle(15, Weight.Regular);
        public static readonly TypeStyle Label = new TypeStyle(13, Weight.Bold, 0.04, uppercase: true);
        public static readonly TypeStyle Data = new TypeStyle(15, Weight.Bold, tabular: true);
        public static readonly TypeStyle Title = new TypeStyle(19, Weight.Bold);
        public static readonly TypeStyle Heading = new TypeStyle(26, Weight.Heavy, -0.01);
        public static readonly TypeStyle Display = new TypeStyle(36, Weight.Heavy, -0.02);
    }

    /// <summary>UI motion (#48). Gameplay timing is not a UI token.</summary>
    public static class MotionScale
    {
        public const int FastMilliseconds = 120;
        public const int BaseMilliseconds = 180;

        /// <summary>The common easing, as cubic-bezier control points (x1, y1, x2, y2).</summary>
        public static readonly (double X1, double Y1, double X2, double Y2) Easing = (0.2, 0, 0, 1);

        /// <summary>A duration under the reduced-motion preference: movement goes, the static state cue stays.</summary>
        public static int Duration(int milliseconds, bool reducedMotion) => reducedMotion ? 0 : milliseconds;
    }

    /// <summary>Named stacking order, lowest first: no component picks its own number.</summary>
    public enum Layer
    {
        Match = 0,
        Hud = 100,
        Controls = 200,
        Overlay = 300,
        Sheet = 400,
        Dialog = 500,
        Status = 600,
    }

    /// <summary>Touch and focus geometry.</summary>
    public static class Interaction
    {
        /// <summary>The smallest frequently used bounded target, both ways (iOS/iPadOS guidance).</summary>
        public const int MinTarget = 44;

        /// <summary>The gap between a focused component and its focus ring.</summary>
        public const int FocusGap = 2;

        /// <summary>The focus ring's width.</summary>
        public const int FocusRing = 3;

        /// <summary>The inner edge's width, inside a control's top border.</summary>
        public const int InnerEdge = 2;
    }
}
