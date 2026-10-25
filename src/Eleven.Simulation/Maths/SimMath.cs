using System;

namespace Eleven.Simulation.Maths
{
    /// <summary>
    /// The simulation's only maths entry point for <c>sin</c>, <c>cos</c>, <c>atan2</c> and
    /// <c>sqrt</c>. Simulation code never calls <c>System.Math</c> or <c>MathF</c> for these; the
    /// banned-API analyzer enforces that.
    ///
    /// Domain checks live here rather than in the backends so both backends fail identically on
    /// out-of-domain input. A failure is an exception, which is deterministic; a NaN would
    /// silently enter state.
    /// </summary>
    public sealed class SimMath
    {
        /// <summary>Largest |x| accepted by <see cref="Sin"/> and <see cref="Cos"/>, in radians.</summary>
        public const double MaxTrigArgument = 1e5;

        private readonly IMathBackend _backend;

        public SimMath(MathBackend backend)
        {
            Backend = backend;
            _backend = backend switch
            {
                MathBackend.SystemMath => new SystemMathBackend(),
                MathBackend.Portable => new PortableMathBackend(),
                _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown maths backend."),
            };
        }

        public MathBackend Backend { get; }

        public double Sin(double x)
        {
            CheckTrigArgument(x);
            return _backend.Sin(x);
        }

        public double Cos(double x)
        {
            CheckTrigArgument(x);
            return _backend.Cos(x);
        }

        /// <summary>atan2 for finite inputs. <c>Atan2(0, 0)</c> is 0.</summary>
        public double Atan2(double y, double x)
        {
            if (!IsFinite(y) || !IsFinite(x))
            {
                throw new ArgumentOutOfRangeException(nameof(y), $"Atan2({y}, {x}) needs finite arguments.");
            }

            return y == 0 && x == 0 ? 0 : _backend.Atan2(y, x);
        }

        /// <summary>Square root of a finite, non-negative value.</summary>
        public double Sqrt(double x)
        {
            if (!IsFinite(x) || x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, "Sqrt needs a finite, non-negative argument.");
            }

            return x == 0 ? 0 : _backend.Sqrt(x);
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
