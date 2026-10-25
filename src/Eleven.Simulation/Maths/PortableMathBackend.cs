using System;

namespace Eleven.Simulation.Maths
{
    /// <summary>
    /// The simulation's own maths, built only from IEEE-754 basic operations (+, −, ×, ÷), which
    /// every conforming runtime rounds identically, plus exact helpers (<c>Math.Round</c>,
    /// <c>Math.Abs</c>, bit casts). Its results depend on no platform library.
    ///
    /// Supported domain, enforced by <see cref="SimMath"/>:
    /// sin and cos for |x| ≤ 1e5 rad; atan2 and sqrt for all finite inputs (sqrt: x ≥ 0).
    /// Accuracy against <c>System.Math</c> is asserted in <c>PortableMathTests</c>; the bounds and
    /// their justification are in docs/simulation/determinism.md.
    ///
    /// This is spike evidence for ADR-0002, not ELEVEN's final maths model.
    /// </summary>
    internal sealed class PortableMathBackend : IMathBackend
    {
        // π/2 split into three parts (fdlibm's pio2_1, pio2_2, pio2_3). The first two have 33
        // significant bits, so n × part is exact for |n| < 2^20, which covers |x| ≤ 1e5.
        private const double PiOver2Part1 = 1.57079632673412561417e+00;
        private const double PiOver2Part2 = 6.07710050630396597660e-11;
        private const double PiOver2Part3 = 2.02226624871116645580e-21;
        private const double TwoOverPi = 6.36619772367581382433e-01;

        private const double Pi = 3.14159265358979311600e+00;
        private const double PiOver2 = 1.57079632679489655800e+00;
        private const double PiOver4 = 7.85398163397448278999e-01;
        private const double TanPiOver8 = 4.14213562373095034470e-01;

        public double Sin(double x)
        {
            double r = Reduce(x, out int quadrant);
            switch (quadrant)
            {
                case 0: return SinKernel(r);
                case 1: return CosKernel(r);
                case 2: return -SinKernel(r);
                default: return -CosKernel(r);
            }
        }

        public double Cos(double x)
        {
            double r = Reduce(x, out int quadrant);
            switch (quadrant)
            {
                case 0: return CosKernel(r);
                case 1: return -SinKernel(r);
                case 2: return -CosKernel(r);
                default: return SinKernel(r);
            }
        }

        public double Atan2(double y, double x)
        {
            double ax = Math.Abs(x);
            double ay = Math.Abs(y);

            // t = min/max in [0, 1]; swapped means the angle is measured from the y axis.
            bool swapped = ay > ax;
            double t = swapped ? ax / ay : ay / ax;

            // Shift [tan(π/8), 1] down by π/4: atan(t) = π/4 + atan((t − 1)/(t + 1)).
            bool shifted = t > TanPiOver8;
            double u = shifted ? (t - 1) / (t + 1) : t;

            // Halve the angle once: atan(u) = 2·atan(u / (1 + √(1 + u²))), so |v| ≤ 0.199.
            double v = u / (1 + Sqrt(1 + u * u));
            double angle = 2 * AtanSeries(v);

            if (shifted) angle += PiOver4;
            if (swapped) angle = PiOver2 - angle;
            if (x < 0) angle = Pi - angle;

            // The sign bit, not y < 0: atan2(−0, −1) is −π.
            return BitConverter.DoubleToInt64Bits(y) < 0 ? -angle : angle;
        }

        public double Sqrt(double x)
        {
            // Bring subnormal and tiny inputs into the normal range, where the bit-level guess works.
            double scale = 1;
            if (x < 1e-300)
            {
                x *= 1.0715086071862673e301; // 2^1000
                scale = 3.054936363499605e-151; // 2^-500
            }

            // Halving the exponent bits gives a guess within about 6 %; Newton's method then
            // roughly squares the relative error each step, so six steps reach full precision.
            long bits = BitConverter.DoubleToInt64Bits(x);
            double y = BitConverter.Int64BitsToDouble((bits >> 1) + 0x1FF8000000000000L);
            for (int i = 0; i < 6; i++)
            {
                y = 0.5 * (y + x / y);
            }

            return y * scale;
        }

        /// <summary>Reduces x to r in about [−π/4, π/4] with x = r + quadrant·π/2 (mod 2π).</summary>
        private static double Reduce(double x, out int quadrant)
        {
            double n = Math.Round(x * TwoOverPi);
            double r = ((x - n * PiOver2Part1) - n * PiOver2Part2) - n * PiOver2Part3;
            quadrant = (int)((long)n & 3);
            return r;
        }

        // Taylor series in Horner form. On |r| ≤ π/4 the first omitted terms are below 5e-17 (sin)
        // and 3e-18 (cos), well under the rounding error of the evaluation.
        private static double SinKernel(double r)
        {
            double r2 = r * r;
            return r * (1 + r2 * (-1.0 / 6 + r2 * (1.0 / 120 + r2 * (-1.0 / 5040 + r2 * (1.0 / 362880
                + r2 * (-1.0 / 39916800 + r2 * (1.0 / 6227020800 + r2 * (-1.0 / 1307674368000))))))));
        }

        private static double CosKernel(double r)
        {
            double r2 = r * r;
            return 1 + r2 * (-1.0 / 2 + r2 * (1.0 / 24 + r2 * (-1.0 / 720 + r2 * (1.0 / 40320
                + r2 * (-1.0 / 3628800 + r2 * (1.0 / 479001600 + r2 * (-1.0 / 87178291200
                + r2 * (1.0 / 20922789888000))))))));
        }

        // atan(v) = v − v³/3 + v⁵/5 − …; for |v| ≤ 0.199 the first omitted term (v²⁵/25) is below 1e-19.
        private static double AtanSeries(double v)
        {
            double v2 = v * v;
            double sum = 0;
            for (int k = 23; k >= 1; k -= 2)
            {
                double term = 1.0 / k;
                sum = (((k >> 1) & 1) == 0 ? term : -term) + v2 * sum;
            }

            return v * sum;
        }
    }
}
