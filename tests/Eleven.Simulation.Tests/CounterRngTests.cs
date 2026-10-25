using System.Collections.Generic;
using Eleven.Simulation.Randomness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>INV-6: independent, counter-based randomness.</summary>
    public class CounterRngTests
    {
        private const ulong Seed = 12345;

        // Draws from KickDirection and KickSpin in one tick, interleaved, the way a consumer might.
        private static List<double> SpinDraws(int directionDraws)
        {
            var direction = new StreamDraws(Seed, 7, RandomStream.KickDirection, 1);
            var spin = new StreamDraws(Seed, 7, RandomStream.KickSpin, 1);
            var spins = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                for (int d = 0; d < directionDraws; d++) direction.NextUniform();
                spins.Add(spin.NextUniform());
            }

            return spins;
        }

        [Fact]
        public void An_extra_draw_in_one_stream_leaves_other_streams_unchanged()
        {
            Assert.Equal(SpinDraws(directionDraws: 1), SpinDraws(directionDraws: 2));
        }

        [Fact]
        public void Every_key_part_changes_the_draw()
        {
            double baseline = CounterRng.Uniform(Seed, 10, RandomStream.KickDirection, 1, 0);
            Assert.NotEqual(baseline, CounterRng.Uniform(Seed + 1, 10, RandomStream.KickDirection, 1, 0));
            Assert.NotEqual(baseline, CounterRng.Uniform(Seed, 11, RandomStream.KickDirection, 1, 0));
            Assert.NotEqual(baseline, CounterRng.Uniform(Seed, 10, RandomStream.KickSpin, 1, 0));
            Assert.NotEqual(baseline, CounterRng.Uniform(Seed, 10, RandomStream.KickDirection, 2, 0));
            Assert.NotEqual(baseline, CounterRng.Uniform(Seed, 10, RandomStream.KickDirection, 1, 1));
        }

        [Fact]
        public void Draws_are_in_the_unit_interval_and_roughly_uniform()
        {
            int below = 0;
            const int n = 100_000;
            for (int i = 0; i < n; i++)
            {
                double u = CounterRng.Uniform(Seed, i, RandomStream.KickSpin, 3, 0);
                Assert.InRange(u, 0.0, 0.9999999999999999);
                if (u < 0.5) below++;
            }

            Assert.InRange(below, n / 2 - 1000, n / 2 + 1000);
        }

        [Fact]
        public void The_function_is_pinned()
        {
            // Golden value: the algorithm is part of the determinism contract. A change here
            // changes every random outcome and needs its own commit with a stated reason.
            Assert.Equal(0xa640cc13852cd8c7UL, CounterRng.Hash(1, 2, RandomStream.KickDirection, 3, 4));
        }
    }
}
