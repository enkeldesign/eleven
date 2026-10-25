namespace Eleven.Simulation.Randomness
{
    /// <summary>
    /// What a random draw is for. Each purpose is its own stream (INV-6). The numeric values are
    /// part of the random function's key and must not change.
    /// </summary>
    public enum RandomStream
    {
        KickDirection = 1,
        KickSpin = 2,
    }

    /// <summary>
    /// Counter-based randomness (INV-6): every draw is a pure function
    /// <c>f(seed, tick, stream, entity, draw)</c>, with no generator state carried between draws
    /// or ticks. A consumer that draws more or less can't shift any other consumer's values.
    ///
    /// Algorithm (documented in docs/simulation/determinism.md): the key parts are folded in one
    /// at a time through the SplitMix64 finaliser, and the top 53 bits of the result give a
    /// double in [0, 1).
    /// </summary>
    public static class CounterRng
    {
        public static ulong Hash(ulong seed, long tick, RandomStream stream, int entity, int draw)
        {
            unchecked
            {
                ulong h = Mix(seed ^ 0x9E3779B97F4A7C15UL);
                h = Mix(h ^ (ulong)tick);
                h = Mix(h ^ (((ulong)(uint)stream << 32) | (uint)entity));
                return Mix(h ^ (uint)draw);
            }
        }

        /// <summary>A uniform double in [0, 1).</summary>
        public static double Uniform(ulong seed, long tick, RandomStream stream, int entity, int draw) =>
            (Hash(seed, tick, stream, entity, draw) >> 11) * (1.0 / (1UL << 53));

        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }

    /// <summary>
    /// Sequential draws from one stream for one entity within one tick. A local helper only: the
    /// draw counter starts at 0 every tick and is never stored, so it is not simulation state.
    /// </summary>
    public struct StreamDraws
    {
        private readonly ulong _seed;
        private readonly long _tick;
        private readonly RandomStream _stream;
        private readonly int _entity;
        private int _draw;

        public StreamDraws(ulong seed, long tick, RandomStream stream, int entity)
        {
            _seed = seed;
            _tick = tick;
            _stream = stream;
            _entity = entity;
            _draw = 0;
        }

        public double NextUniform() => CounterRng.Uniform(_seed, _tick, _stream, _entity, _draw++);
    }
}
