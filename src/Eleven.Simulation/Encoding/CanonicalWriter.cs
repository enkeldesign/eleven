using System;

namespace Eleven.Simulation.Encoding
{
    /// <summary>
    /// Writes the canonical encoding used for state hashes, config hashes and snapshots:
    /// little-endian integers, doubles as their IEEE-754 binary64 bit pattern (little-endian),
    /// booleans as one byte (0 or 1). Values are written in a fixed documented order with no
    /// padding, names or tags. NaN and ±Infinity are refused rather than encoded.
    /// </summary>
    public sealed class CanonicalWriter
    {
        private byte[] _buffer;
        private int _length;

        public CanonicalWriter(int capacity = 256)
        {
            _buffer = new byte[capacity];
        }

        public int Length => _length;

        /// <summary>Empties the writer, keeping its buffer, so per-tick hashing doesn't allocate.</summary>
        public void Clear() => _length = 0;

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteInt32(int value) => WriteUInt64Bytes(unchecked((uint)value), 4);

        public void WriteInt64(long value) => WriteUInt64Bytes(unchecked((ulong)value), 8);

        public void WriteUInt64(ulong value) => WriteUInt64Bytes(value, 8);

        /// <summary>
        /// Writes the exact bit pattern, so −0 and +0 encode differently: they can behave
        /// differently later (for example in atan2), so they count as different state.
        /// </summary>
        public void WriteDouble(double value, string field)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException($"Non-finite value {value} in '{field}' can't be encoded.");
            }

            WriteUInt64Bytes(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)), 8);
        }

        public void WriteBytes(byte[] bytes)
        {
            Ensure(bytes.Length);
            Array.Copy(bytes, 0, _buffer, _length, bytes.Length);
            _length += bytes.Length;
        }

        /// <summary>UTF-8 bytes prefixed with their int32 length.</summary>
        public void WriteString(string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        public ulong Hash() => Fnv1a64.Hash(_buffer, 0, _length);

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Array.Copy(_buffer, result, _length);
            return result;
        }

        private void WriteUInt64Bytes(ulong value, int count)
        {
            Ensure(count);
            for (int i = 0; i < count; i++)
            {
                _buffer[_length++] = (byte)(value >> (8 * i));
            }
        }

        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length) return;
            int size = _buffer.Length * 2;
            while (size < _length + extra) size *= 2;
            Array.Resize(ref _buffer, size);
        }
    }
}
