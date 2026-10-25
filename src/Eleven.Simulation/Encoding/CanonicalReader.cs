using System;

namespace Eleven.Simulation.Encoding
{
    /// <summary>Reads what <see cref="CanonicalWriter"/> wrote. Fails on truncated input or non-finite doubles.</summary>
    public sealed class CanonicalReader
    {
        private readonly byte[] _buffer;
        private int _position;

        public CanonicalReader(byte[] buffer)
        {
            _buffer = buffer;
        }

        public bool AtEnd => _position == _buffer.Length;

        public byte ReadByte()
        {
            Require(1);
            return _buffer[_position++];
        }

        public bool ReadBool()
        {
            byte b = ReadByte();
            if (b > 1) throw new FormatException($"Invalid boolean byte {b}.");
            return b == 1;
        }

        public int ReadInt32() => unchecked((int)(uint)ReadUInt64Bytes(4));

        public long ReadInt64() => unchecked((long)ReadUInt64Bytes(8));

        public ulong ReadUInt64() => ReadUInt64Bytes(8);

        public double ReadDouble(string field)
        {
            double value = BitConverter.Int64BitsToDouble(unchecked((long)ReadUInt64Bytes(8)));
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new FormatException($"Non-finite value in '{field}'.");
            }

            return value;
        }

        public string ReadString()
        {
            int length = ReadInt32();
            if (length < 0) throw new FormatException("Negative string length.");
            Require(length);
            string value = System.Text.Encoding.UTF8.GetString(_buffer, _position, length);
            _position += length;
            return value;
        }

        private ulong ReadUInt64Bytes(int count)
        {
            Require(count);
            ulong value = 0;
            for (int i = 0; i < count; i++)
            {
                value |= (ulong)_buffer[_position++] << (8 * i);
            }

            return value;
        }

        private void Require(int count)
        {
            if (count > _buffer.Length - _position) throw new FormatException("Unexpected end of data.");
        }
    }
}
