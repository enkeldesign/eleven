using System;

namespace Eleven.Simulation.Maths
{
    /// <summary>
    /// The simulation's only maths entry point for <c>sin</c>, <c>cos</c>, <c>atan2</c> and
    /// <c>sqrt</c>, backed by <see cref="PortableMath"/> (ADR-0002). Simulation code never calls
    /// <c>System.Math</c> or <c>MathF</c> for these; the banned-API analyzer enforces that.
    ///
    /// Domain checks live here, in front of the implementation. A failure is an exception, which
    /// is deterministic; a NaN would silently enter state.
    /// </summary>
    public static class SimMath
    {
        /// <summary>Largest |x| accepted by <see cref="Sin"/> and <see cref="Cos"/>, in radians.</summary>
        public const double MaxTrigArgument = 1e5;

        public static double Sin(double x)
        {
            CheckTrigArgument(x);
            return PortableMath.Sin(x);
        }

        public static double Cos(double x)
        {
            CheckTrigArgument(x);
            return PortableMath.Cos(x);
        }

        /// <summary>atan2 for finite inputs. <c>Atan2(0, 0)</c> is 0.</summary>
        public static double Atan2(double y, double x)
        {
            if (!IsFinite(y) || !IsFinite(x))
            {
                throw new ArgumentOutOfRangeException(nameof(y), $"Atan2({y}, {x}) needs finite arguments.");
            }

            return y == 0 && x == 0 ? 0 : PortableMath.Atan2(y, x);
        }

        /// <summary>Square root of a finite, non-negative value.</summary>
        public static double Sqrt(double x)
        {
            if (!IsFinite(x) || x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, "Sqrt needs a finite, non-negative argument.");
            }

            return x == 0 ? 0 : PortableMath.Sqrt(x);
        }

        private static void CheckTrigArgument(double x)
        {
            if (!IsFinite(x) || x > MaxTrigArgument || x < -MaxTrigArgument)
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Trig argument outside ±{MaxTrigArgument}.");
            }
        }

        internal static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
