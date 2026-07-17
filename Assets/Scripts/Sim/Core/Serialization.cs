using System;

namespace Craftwar.Sim
{
    /// <summary>
    /// Minimal little-endian byte writer shared by network packets, replays and
    /// state serialization. Explicit per-byte writes: identical output on every
    /// platform regardless of endianness.
    /// </summary>
    public struct ByteWriter
    {
        public byte[] Buffer;
        public int Position;

        public ByteWriter(byte[] buffer)
        {
            Buffer = buffer;
            Position = 0;
        }

        public void WriteByte(byte v) => Buffer[Position++] = v;

        public void WriteUShort(ushort v)
        {
            Buffer[Position++] = (byte)v;
            Buffer[Position++] = (byte)(v >> 8);
        }

        public void WriteUInt(uint v)
        {
            Buffer[Position++] = (byte)v;
            Buffer[Position++] = (byte)(v >> 8);
            Buffer[Position++] = (byte)(v >> 16);
            Buffer[Position++] = (byte)(v >> 24);
        }

        public void WriteInt(int v) => WriteUInt((uint)v);

        public void WriteULong(ulong v)
        {
            WriteUInt((uint)v);
            WriteUInt((uint)(v >> 32));
        }
    }

    public struct ByteReader
    {
        public byte[] Buffer;
        public int Position;
        public int Length;

        public ByteReader(byte[] buffer, int length = -1)
        {
            Buffer = buffer;
            Position = 0;
            Length = length < 0 ? buffer.Length : length;
        }

        void Require(int count)
        {
            if (Position + count > Length)
                throw new System.IO.EndOfStreamException($"Read past end at {Position}+{count}/{Length}");
        }

        public byte ReadByte()
        {
            Require(1);
            return Buffer[Position++];
        }

        public ushort ReadUShort()
        {
            Require(2);
            ushort v = (ushort)(Buffer[Position] | (Buffer[Position + 1] << 8));
            Position += 2;
            return v;
        }

        public uint ReadUInt()
        {
            Require(4);
            uint v = (uint)(Buffer[Position]
                | (Buffer[Position + 1] << 8)
                | (Buffer[Position + 2] << 16)
                | (Buffer[Position + 3] << 24));
            Position += 4;
            return v;
        }

        public int ReadInt() => (int)ReadUInt();

        public ulong ReadULong()
        {
            ulong lo = ReadUInt();
            ulong hi = ReadUInt();
            return lo | (hi << 32);
        }
    }
}
