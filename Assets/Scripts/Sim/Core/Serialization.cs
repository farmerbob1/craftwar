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
        /// <summary>The backing store. Reassigned by <see cref="Ensure"/> when it
        /// grows, so never copy out of the array you passed in — call
        /// <see cref="ToArray"/> (or read <c>w.Buffer</c>) instead.</summary>
        public byte[] Buffer;
        public int Position;

        public ByteWriter(byte[] buffer)
        {
            Buffer = buffer;
            Position = 0;
        }

        /// <summary>Start with a guess; the writer grows past it if needed.</summary>
        public ByteWriter(int capacity)
        {
            Buffer = new byte[capacity < 16 ? 16 : capacity];
            Position = 0;
        }

        /// <summary>Guarantee room for <paramref name="count"/> more bytes,
        /// doubling as needed. Growth is why variable-size payloads (state
        /// snapshots, turn packets) do not have to be size-estimated up front.</summary>
        public void Ensure(int count)
        {
            int needed = Position + count;
            if (Buffer != null && needed <= Buffer.Length)
                return;
            int cap = Buffer == null || Buffer.Length == 0 ? 16 : Buffer.Length;
            while (cap < needed)
                cap *= 2;
            var grown = new byte[cap];
            if (Buffer != null)
                Array.Copy(Buffer, grown, Position);
            Buffer = grown;
        }

        /// <summary>The bytes written so far, trimmed. The only safe way to
        /// harvest the result once growth is possible.</summary>
        public byte[] ToArray()
        {
            var result = new byte[Position];
            if (Position > 0)
                Array.Copy(Buffer, result, Position);
            return result;
        }

        public void WriteByte(byte v)
        {
            Ensure(1);
            Buffer[Position++] = v;
        }

        public void WriteUShort(ushort v)
        {
            Ensure(2);
            Buffer[Position++] = (byte)v;
            Buffer[Position++] = (byte)(v >> 8);
        }

        public void WriteUInt(uint v)
        {
            Ensure(4);
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

        /// <summary>Length-prefixed raw block (u32 count + bytes).</summary>
        public void WriteBytes(byte[] src, int offset, int count)
        {
            WriteUInt((uint)count);
            if (count <= 0)
                return;
            Ensure(count);
            Array.Copy(src, offset, Buffer, Position, count);
            Position += count;
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

        /// <summary>Mirror of <see cref="ByteWriter.WriteBytes"/>.</summary>
        public byte[] ReadBytes()
        {
            int count = (int)ReadUInt();
            if (count < 0)
                throw new System.IO.InvalidDataException($"Negative block length {count}");
            Require(count);
            var result = new byte[count];
            if (count > 0)
                Array.Copy(Buffer, Position, result, 0, count);
            Position += count;
            return result;
        }
    }
}
