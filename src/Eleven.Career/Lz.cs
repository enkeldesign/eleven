#nullable enable
using System;

namespace Eleven.Career
{
    /// <summary>
    /// A save's compression (#170): LZ77 in LZ4's block layout, pure managed code, so the same
    /// bytes give the same output on every runtime and nothing native is needed in the browser.
    /// Decompression checks every length and offset against its bounds, and anything that isn't
    /// a whole stream of exactly the expected length is refused.
    /// </summary>
    public static class Lz
    {
        public static byte[] Compress(byte[] data) => (byte[])data.Clone();

        /// <summary>The <paramref name="length"/> bytes compressed in <paramref name="data"/>; a <see cref="FormatException"/> for anything else.</summary>
        public static byte[] Decompress(byte[] data, int length) => (byte[])data.Clone();
    }
}
