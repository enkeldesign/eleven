namespace Eleven.Simulation.Maths
{
    /// <summary>
    /// Which implementation sits behind the maths boundary. Part of <see cref="SimConfig"/> and
    /// therefore of the config hash. The numeric values are encoded and must not change.
    /// </summary>
    public enum MathBackend
    {
        /// <summary><c>System.Math</c>: whatever the runtime provides.</summary>
        SystemMath = 1,

        /// <summary>The simulation's own implementation, built from IEEE-754 basic operations only.</summary>
        Portable = 2,
    }

    /// <summary>One implementation of the maths boundary. Inputs are already domain-checked by <see cref="SimMath"/>.</summary>
    internal interface IMathBackend
    {
        double Sin(double x);
        double Cos(double x);
        double Atan2(double y, double x);
        double Sqrt(double x);
    }
}
