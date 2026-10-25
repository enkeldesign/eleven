using System;

namespace Eleven.Simulation.Maths
{
    /// <summary>
    /// The runtime's own maths. The only type in simulation code allowed to call
    /// <c>System.Math</c> transcendentals; the banned-API analyzer names it explicitly.
    /// </summary>
    internal sealed class SystemMathBackend : IMathBackend
    {
        public double Sin(double x) => Math.Sin(x);
        public double Cos(double x) => Math.Cos(x);
        public double Atan2(double y, double x) => Math.Atan2(y, x);
        public double Sqrt(double x) => Math.Sqrt(x);
    }
}
