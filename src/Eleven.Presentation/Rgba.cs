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
        public static Rgba Hex(string hex) => default;

        /// <summary>"#RRGGBB", or "#RRGGBBAA" when not opaque.</summary>
        public string ToHex() => "";

        /// <summary>This colour with its alpha replaced, 0–1.</summary>
        public Rgba WithAlpha(double alpha) => this;

        /// <summary>This colour composited over an opaque background.</summary>
        public Rgba Over(Rgba background) => this;

        /// <summary>WCAG 2.x relative luminance, 0–1, of this colour treated as opaque.</summary>
        public double Luminance => 0;

        /// <summary>WCAG 2.x contrast ratio, 1–21, between two opaque colours.</summary>
        public static double Contrast(Rgba a, Rgba b) => 1;

        public bool Equals(Rgba other) => R == other.R && G == other.G && B == other.B && A == other.A;

        public override bool Equals(object? obj) => obj is Rgba other && Equals(other);

        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

        public override string ToString() => ToHex();
    }
}
