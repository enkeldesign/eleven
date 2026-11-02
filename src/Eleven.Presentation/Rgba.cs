// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Globalization;

namespace Eleven.Presentation
{
    /// <summary>An sRGB colour with alpha, eight bits per channel.</summary>
    public readonly struct Rgba : IEquatable<Rgba>
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public Rgba(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>From "#RRGGBB" or "#RRGGBBAA".</summary>
        public static Rgba Hex(string hex)
        {
            if (hex == null || hex.Length < 1 || hex[0] != '#' || (hex.Length != 7 && hex.Length != 9))
            {
                throw new FormatException($"'{hex}' is not #RRGGBB or #RRGGBBAA.");
            }

            byte Channel(int at) => byte.Parse(hex.Substring(at, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return new Rgba(Channel(1), Channel(3), Channel(5), hex.Length == 9 ? Channel(7) : (byte)255);
        }

        /// <summary>"#RRGGBB", or "#RRGGBBAA" when not opaque.</summary>
        public string ToHex() => A == 255
            ? string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", R, G, B)
            : string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", R, G, B, A);

        /// <summary>This colour with its alpha replaced, 0–1.</summary>
        public Rgba WithAlpha(double alpha) => new Rgba(R, G, B, (byte)Math.Round(Math.Max(0, Math.Min(1, alpha)) * 255));

        /// <summary>This colour composited over an opaque background.</summary>
        public Rgba Over(Rgba background)
        {
            double a = A / 255.0;
            byte Mix(byte top, byte bottom) => (byte)Math.Round(top * a + bottom * (1 - a));
            return new Rgba(Mix(R, background.R), Mix(G, background.G), Mix(B, background.B));
        }

        /// <summary>WCAG 2.x relative luminance, 0–1, of this colour treated as opaque.</summary>
        public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

        /// <summary>WCAG 2.x contrast ratio, 1–21, between two opaque colours.</summary>
        public static double Contrast(Rgba a, Rgba b)
        {
            double la = a.Luminance, lb = b.Luminance;
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        // sRGB channel to linear light (WCAG 2.x).
        private static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public bool Equals(Rgba other) => R == other.R && G == other.G && B == other.B && A == other.A;

        public override bool Equals(object? obj) => obj is Rgba other && Equals(other);

        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

        public override string ToString() => ToHex();
    }
}
