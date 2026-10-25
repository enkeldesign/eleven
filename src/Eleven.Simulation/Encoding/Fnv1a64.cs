namespace Eleven.Simulation.Encoding
{
    /// <summary>
    /// 64-bit FNV-1a. Chosen for the spike because it is a few lines that every runtime computes
    /// identically. Every step is a bijection on the 64-bit state, so two equal-length inputs
    /// that differ in exactly one byte always hash differently. It is a divergence detector, not
    /// a cryptographic hash.
    /// </summary>
    public static class Fnv1a64
    {
        public const ulong OffsetBasis = 0xCBF29CE484222325UL;
        public const ulong Prime = 0x100000001B3UL;

        public static ulong Hash(byte[] data) => Hash(data, 0, data.Length);

        public static ulong Hash(byte[] data, int offset, int count)
        {
            ulong h = OffsetBasis;
            unchecked
            {
                for (int i = offset; i < offset + count; i++)
                {
                    h = (h ^ data[i]) * Prime;
                }
            }

            return h;
        }

        /// <summary>Continues a hash over the 8 little-endian bytes of <paramref name="value"/>.</summary>
        public static ulong Append(ulong h, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h = (h ^ (byte)(value >> (8 * i))) * Prime;
                }
            }

            return h;
        }
    }
}
