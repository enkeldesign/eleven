using System;
using System.Linq;
using Eleven.Simulation.Randomness;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #170: a save's compression. Anything round-trips, empty, incompressible or repetitive; a
    /// save shrinks several times over; and a stream that isn't whole, or of another length, is
    /// refused with a FormatException, never another exception or a runaway output.
    /// </summary>
    public class LzTests
    {
        private static byte[] Random(int n, ulong seed)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)(CounterRng.Uniform(seed, i, RandomStream.World, 0, 0) * 256);
            return b;
        }

        [Fact]
        public void Anything_round_trips()
        {
            var cases = new[]
            {
                Array.Empty<byte>(), new byte[] { 7 }, new byte[100_000], Random(70_000, 1), Random(5, 2),
                Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray(),
                Enumerable.Range(0, 300_000).Select(i => (byte)((i / 700) % 3 == 0 ? i * 31 : 9)).ToArray(),
            };
            foreach (byte[] data in cases)
            {
                byte[] packed = Lz.Compress(data);
                Assert.Equal(data, Lz.Decompress(packed, data.Length));
                Assert.Equal(packed, Lz.Compress(data));
                Assert.True(packed.Length <= data.Length + data.Length / 200 + 16, $"{data.Length} grew to {packed.Length}");
            }

            Assert.True(Lz.Compress(new byte[100_000]).Length < 1_000);
        }

        [Fact]
        public void A_save_shrinks_several_times_over()
        {
            World w = WorldGenerator.Generate(0x170);
            byte[] world = WorldCodec.Encode(w);
            byte[] packed = Lz.Compress(world);
            Assert.True(packed.Length * 3 < world.Length, $"{world.Length} bytes to {packed.Length}");
            Assert.Equal(world, Lz.Decompress(packed, world.Length));
        }

        [Fact]
        public void A_stream_not_whole_or_of_another_length_is_refused()
        {
            byte[] data = WorldCodec.Encode(WorldGenerator.Generate(0x171));
            byte[] packed = Lz.Compress(data);
            Assert.Throws<FormatException>(() => Lz.Decompress(packed, data.Length + 1));
            Assert.Throws<FormatException>(() => Lz.Decompress(packed, data.Length - 1));
            Assert.Throws<FormatException>(() => Lz.Decompress(packed.Take(packed.Length - 3).ToArray(), data.Length));
            Assert.Throws<FormatException>(() => Lz.Decompress(packed, -1));

            // Garbage, or a stream damaged anywhere: refused, or at most the expected length back.
            for (int k = 0; k < 400; k++)
            {
                byte[] bad = k < 200 ? Random(1 + k * 13, (ulong)k + 10) : (byte[])packed.Clone();
                if (k >= 200) bad[(k * 7919) % bad.Length] ^= (byte)(1 + k % 255);
                try
                {
                    byte[] got = Lz.Decompress(bad, data.Length);
                    Assert.Equal(data.Length, got.Length);
                }
                catch (FormatException)
                {
                }
            }
        }
    }
}
