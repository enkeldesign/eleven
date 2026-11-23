#nullable enable
using System;
using System.Collections.Generic;

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
        private const int MinMatch = 4, MaxOffset = 65535, HashBits = 15;

        // A sequence: a token (literals' count in its high nibble, the match's length less four in
        // its low), more count bytes when a nibble is 15, the literals, then the match's offset as
        // two bytes, low first, and more length bytes. The last sequence is literals only.
        public static byte[] Compress(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var output = new List<byte>(data.Length / 3 + 16);
            var table = new int[1 << HashBits];
            for (int i = 0; i < table.Length; i++) table[i] = -1;
            int n = data.Length, anchor = 0, at = 0;
            while (at + MinMatch <= n)
            {
                int h = Hash(data, at), candidate = table[h];
                table[h] = at;
                if (candidate >= 0 && at - candidate <= MaxOffset && Same4(data, candidate, at))
                {
                    int length = MinMatch;
                    while (at + length < n && data[candidate + length] == data[at + length]) length++;
                    Emit(output, data, anchor, at - anchor, at - candidate, length);
                    at += length;
                    anchor = at;
                }
                else
                {
                    at++;
                }
            }

            Emit(output, data, anchor, n - anchor, 0, 0);
            return output.ToArray();
        }

        /// <summary>The <paramref name="length"/> bytes compressed in <paramref name="data"/>; a <see cref="FormatException"/> for anything else.</summary>
        public static byte[] Decompress(byte[] data, int length)
        {
            if (data == null) throw new FormatException("No compressed bytes.");
            if (length < 0) throw new FormatException($"Invalid length {length}.");
            var output = new byte[length];
            int ip = 0, op = 0;
            while (true)
            {
                if (ip >= data.Length) throw new FormatException("The compressed bytes end mid-sequence.");
                int token = data[ip++];
                int literals = Count(data, ref ip, token >> 4);
                if (literals > data.Length - ip || literals > length - op) throw new FormatException("A literal run runs past its bounds.");
                Buffer.BlockCopy(data, ip, output, op, literals);
                ip += literals;
                op += literals;
                if (ip == data.Length)
                {
                    if ((token & 15) != 0) throw new FormatException("The last sequence has a match.");
                    if (op != length) throw new FormatException($"The compressed bytes hold {op} bytes, not {length}.");
                    return output;
                }

                if (ip + 2 > data.Length) throw new FormatException("A match's offset is cut.");
                int offset = data[ip] | (data[ip + 1] << 8);
                ip += 2;
                if (offset == 0 || offset > op) throw new FormatException($"A match's offset {offset} reaches before the start.");
                int match = Count(data, ref ip, token & 15) + MinMatch;
                if (match > length - op) throw new FormatException("A match runs past the expected length.");
                for (int k = 0; k < match; k++, op++) output[op] = output[op - offset];
            }
        }

        private static int Count(byte[] data, ref int ip, int nibble)
        {
            int count = nibble;
            if (nibble != 15) return count;
            while (true)
            {
                if (ip >= data.Length) throw new FormatException("A count is cut.");
                int b = data[ip++];
                count += b;
                if (count > int.MaxValue / 2) throw new FormatException("A count overflows.");
                if (b != 255) return count;
            }
        }

        private static void Emit(List<byte> output, byte[] data, int from, int literals, int offset, int match)
        {
            int extra = match > 0 ? match - MinMatch : 0;
            output.Add((byte)((Math.Min(literals, 15) << 4) | (match > 0 ? Math.Min(extra, 15) : 0)));
            if (literals >= 15) More(output, literals - 15);
            for (int i = 0; i < literals; i++) output.Add(data[from + i]);
            if (match == 0) return;
            output.Add((byte)(offset & 0xFF));
            output.Add((byte)(offset >> 8));
            if (extra >= 15) More(output, extra - 15);
        }

        private static void More(List<byte> output, int rest)
        {
            while (rest >= 255)
            {
                output.Add(255);
                rest -= 255;
            }

            output.Add((byte)rest);
        }

        private static bool Same4(byte[] d, int a, int b) => d[a] == d[b] && d[a + 1] == d[b + 1] && d[a + 2] == d[b + 2] && d[a + 3] == d[b + 3];

        private static int Hash(byte[] d, int i)
        {
            uint v = (uint)(d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24));
            return (int)((v * 2654435761u) >> (32 - HashBits));
        }
    }
}
