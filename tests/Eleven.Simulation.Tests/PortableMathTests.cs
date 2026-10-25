using System;
using Eleven.Simulation.Maths;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Accuracy of the simulation-owned backend against System.Math over its supported domain.
    /// The bounds are a few ulp of the results' magnitude; docs/simulation/determinism.md says why.
    /// </summary>
    public class PortableMathTests
    {
        private static readonly SimMath Portable = new SimMath(MathBackend.Portable);

        // Absolute, 4 ulp of 1 (measured worst: 1 ulp of 1, 2.2e-16).
        private const double TrigBound = 9e-16;

        // Absolute, 4 ulp of π (measured worst: 4.4e-16, 1 ulp of π).
        private const double Atan2Bound = 1.8e-15;

        // Relative, 2 ulp: Newton's method from a 6 % guess converges to within an ulp (measured: 1).
        private const double SqrtRelativeBound = 4.5e-16;

        [Fact]
        public void Sin_and_cos_match_system_math_over_a_few_turns()
        {
            double worst = 0;
            for (int i = -200_000; i <= 200_000; i++)
            {
                double x = i * 1e-4 * Math.PI / 2;
                worst = Math.Max(worst, Math.Abs(Portable.Sin(x) - Math.Sin(x)));
                worst = Math.Max(worst, Math.Abs(Portable.Cos(x) - Math.Cos(x)));
            }

            Assert.True(worst <= TrigBound, $"worst error {worst:e3}");
        }

        [Fact]
        public void Sin_and_cos_match_system_math_up_to_the_domain_limit()
        {
            double worst = 0;
            for (int i = 0; i <= 100_000; i++)
            {
                // Irregular spacing so arguments don't sit on multiples of π/2.
                double x = SimMath.MaxTrigArgument * i / 100_000.0 - 0.123456789 * (i % 7);
                if (Math.Abs(x) > SimMath.MaxTrigArgument) continue;
                worst = Math.Max(worst, Math.Abs(Portable.Sin(x) - Math.Sin(x)));
                worst = Math.Max(worst, Math.Abs(Portable.Cos(x) - Math.Cos(x)));
            }

            Assert.True(worst <= TrigBound, $"worst error {worst:e3}");
        }

        [Fact]
        public void Atan2_matches_system_math_in_every_octant()
        {
            double worst = 0;
            for (int i = -300; i <= 300; i++)
            {
                for (int j = -300; j <= 300; j++)
                {
                    if (i == 0 && j == 0) continue;
                    double y = i * 0.37 + 0.001 * j;
                    double x = j * 0.41 - 0.002 * i;
                    worst = Math.Max(worst, Math.Abs(Portable.Atan2(y, x) - Math.Atan2(y, x)));
                }
            }

            Assert.True(worst <= Atan2Bound, $"worst error {worst:e3}");
        }

        [Theory]
        [InlineData(1e-300, 1)]
        [InlineData(1e300, 1)]
        [InlineData(1, 1e-300)]
        [InlineData(-1, 1e-12)]
        [InlineData(0, -5)]
        [InlineData(5, 0)]
        public void Atan2_handles_extreme_ratios_and_axes(double y, double x)
        {
            Assert.True(Math.Abs(Portable.Atan2(y, x) - Math.Atan2(y, x)) <= Atan2Bound);
        }

        [Fact]
        public void Sqrt_matches_system_math_from_subnormal_to_huge()
        {
            double worst = 0;
            void Check(double x) => worst = Math.Max(worst, Math.Abs(Portable.Sqrt(x) - Math.Sqrt(x)) / Math.Sqrt(x));

            foreach (double subnormal in new[] { 4.9e-324, 1e-320, 3.3e-315, 1.5e-310 }) Check(subnormal);
            for (double x = 2.2250738585072014e-308; x < 1e300; x *= 1.0137) Check(x);

            Assert.True(worst <= SqrtRelativeBound, $"worst relative error {worst:e3}");
        }

        [Theory]
        [InlineData(MathBackend.SystemMath)]
        [InlineData(MathBackend.Portable)]
        public void Out_of_domain_arguments_fail_the_same_way_on_both_backends(MathBackend backend)
        {
            var math = new SimMath(backend);
            Assert.Throws<ArgumentOutOfRangeException>(() => math.Sin(SimMath.MaxTrigArgument * 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => math.Cos(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => math.Atan2(double.PositiveInfinity, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => math.Sqrt(-1e-9));
            Assert.Equal(0, math.Atan2(0, 0));
            Assert.Equal(0, math.Sqrt(0));
        }
    }
}
