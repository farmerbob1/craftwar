using System;

namespace Craftwar.Import.War2
{
    /// <summary>
    /// Warcraft 2 DOS .WAR archive reader (maindat.war etc.).
    /// Ported from war2tools' libwar2 (MIT, Copyright (c) 2014-2017
    /// Jean Guyomarc'h) — https://github.com/war2/war2tools
    ///
    /// Container: u32 magic 0x19, u16 entry count, u16 file id, then one u32
    /// absolute offset per entry. Each entry starts with u32 header:
    /// top byte = flags (0x00 raw, 0x20 LZSS-compressed), low 24 bits =
    /// uncompressed length.
    /// </summary>
    public sealed class War2Archive
    {
        public const uint Magic = 0x19;

        readonly byte[] _data;
        readonly int[] _entryOffsets; // -1 = absent entry

        public int EntryCount => _entryOffsets.Length;
        public ushort FileId { get; }

        public War2Archive(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (data.Length < 8 || ReadU32(0) != Magic)
                throw new War2FormatException("Not a .WAR archive (bad magic)");

            int count = ReadU16(4);
            FileId = (ushort)ReadU16(6);
            _entryOffsets = new int[count];
            for (int i = 0; i < count; i++)
            {
                uint off = ReadU32(8 + i * 4);
                // Offsets past EOF mark absent entries (libwar2 stores NULL).
                _entryOffsets[i] = off >= data.Length ? -1 : (int)off;
            }
        }

        public bool HasEntry(int entry) =>
            entry >= 0 && entry < _entryOffsets.Length && _entryOffsets[entry] >= 0;

        /// <summary>Extract and (if needed) decompress an entry. Null if absent.</summary>
        public byte[] ExtractEntry(int entry)
        {
            if (!HasEntry(entry))
                return null;

            int p = _entryOffsets[entry];
            uint header = ReadU32(p);
            int flags = (int)(header >> 24);
            int ulen = (int)(header & 0x00FFFFFF);
            p += 4;

            switch (flags)
            {
                case 0x00:
                {
                    var raw = new byte[ulen];
                    Array.Copy(_data, p, raw, 0, ulen);
                    return raw;
                }
                case 0x20:
                    return DecompressLzss(p, ulen);
                default:
                    throw new War2FormatException($"Entry {entry}: unknown flags 0x{flags:X2}");
            }
        }

        /// <summary>
        /// LZSS variant with a 4 KB ring buffer. Control byte holds 8 flags
        /// (LSB first): 1 = literal byte, 0 = back-reference word where the
        /// top 4 bits are length-3 and the low 12 bits an ABSOLUTE ring index.
        /// Every output byte (literal or copied) is also appended to the ring.
        /// </summary>
        byte[] DecompressLzss(int src, int ulen)
        {
            var output = new byte[ulen];
            var ring = new byte[4096];
            int ringWrite = 0;
            int outPos = 0;

            while (outPos < ulen)
            {
                int bits = _data[src++];
                for (int i = 0; i < 8 && outPos < ulen; i++, bits >>= 1)
                {
                    if ((bits & 1) != 0)
                    {
                        byte b = _data[src++];
                        output[outPos++] = b;
                        ring[ringWrite++ & 0xFFF] = b;
                    }
                    else
                    {
                        int w = _data[src] | (_data[src + 1] << 8);
                        src += 2;
                        int len = (w >> 12) + 3;
                        int ringRead = w & 0x0FFF;
                        while (len-- > 0 && outPos < ulen)
                        {
                            byte b = ring[ringRead++ & 0xFFF];
                            output[outPos++] = b;
                            ring[ringWrite++ & 0xFFF] = b;
                        }
                    }
                }
            }
            return output;
        }

        uint ReadU32(int p) =>
            (uint)(_data[p] | (_data[p + 1] << 8) | (_data[p + 2] << 16) | (_data[p + 3] << 24));

        int ReadU16(int p) => _data[p] | (_data[p + 1] << 8);
    }

    public sealed class War2FormatException : Exception
    {
        public War2FormatException(string message) : base(message) { }
    }
}
