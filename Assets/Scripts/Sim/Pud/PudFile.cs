using System;
using System.Collections.Generic;

namespace Craftwar.Sim.Pud
{
    public enum PudEra : byte
    {
        Forest = 0,
        Winter = 1,
        Wasteland = 2,
        Swamp = 3,
    }

    /// <summary>Controller of a player slot (OWNR section values).</summary>
    public enum PudOwner : byte
    {
        PassiveComputer = 0x02,
        Nobody = 0x03,
        Computer = 0x04,
        Human = 0x05,
        RescuePassive = 0x06,
        RescueActive = 0x07,
    }

    public struct PudUnitEntry
    {
        public ushort X;
        public ushort Y;
        public byte Type;      // unit type id (Appendix A)
        public byte Owner;     // player slot 0-7, 15 = neutral
        public ushort Alter;   // gold mine / oil: amount * 2500; else 0 passive / 1 active
    }

    /// <summary>
    /// Warcraft 2 PUD map file, parsed per war2tools' doc/pud_format.txt
    /// (format spec by the community; parser written fresh for Craftwar).
    /// A PUD is a sequence of sections: 4-char tag + u32 length + payload,
    /// little-endian throughout.
    /// Slot arrays are 16 wide: 8 players, 7 "unusable" phantoms, 1 neutral.
    /// </summary>
    public sealed class PudFile
    {
        public const int SlotCount = 16;

        public ushort Version;
        public string Description = "";
        public uint TypeTag;

        public int Width;
        public int Height;
        public PudEra Era;

        public readonly byte[] Owner = new byte[SlotCount];
        public readonly byte[] Side = new byte[SlotCount];     // 0 human, 1 orc, 2 neutral
        public readonly ushort[] StartGold = new ushort[SlotCount];
        public readonly ushort[] StartLumber = new ushort[SlotCount];
        public readonly ushort[] StartOil = new ushort[SlotCount];
        public readonly byte[] AiType = new byte[SlotCount];

        public ushort[] Tiles = Array.Empty<ushort>();      // MTXM, row-major [y * Width + x]
        public ushort[] MoveMap = Array.Empty<ushort>();    // SQM passability flags
        public byte[] OilMap = Array.Empty<byte>();         // OILM (obsolete, 0-7)
        public ushort[] RegionMap = Array.Empty<ushort>();  // REGM action map

        public readonly List<PudUnitEntry> Units = new List<PudUnitEntry>();

        /// <summary>Raw stat-override payloads, decoded later against RuleSet (M5).</summary>
        public byte[] UnitDataOverride;   // UDTA payload if present
        public byte[] UpgradeDataOverride; // UGRD payload if present

        public bool HasSection(string tag) => _seenSections.Contains(tag);
        readonly HashSet<string> _seenSections = new HashSet<string>();

        public static PudFile Parse(byte[] data)
        {
            var pud = new PudFile();
            int pos = 0;

            while (pos + 8 <= data.Length)
            {
                string tag = ReadTag(data, pos);
                uint length = ReadU32(data, pos + 4);
                int payload = pos + 8;
                if (payload + length > data.Length)
                    throw new PudFormatException($"Section '{tag}' at {pos} overruns file ({length} bytes)");

                pud.ParseSection(tag, data, payload, (int)length);
                pud._seenSections.Add(tag);
                pos = payload + (int)length;
            }

            if (!pud._seenSections.Contains("TYPE"))
                throw new PudFormatException("Missing TYPE section — not a PUD file");
            if (pud.Width == 0 || pud.Height == 0)
                throw new PudFormatException("Missing DIM section");
            if (pud.Tiles.Length != pud.Width * pud.Height)
                throw new PudFormatException("Missing or truncated MTXM section");
            return pud;
        }

        void ParseSection(string tag, byte[] d, int p, int len)
        {
            switch (tag)
            {
                case "TYPE":
                    // 10 bytes "WAR2 MAP\0\0" + 2 unused + u32 id tag
                    const string magic = "WAR2 MAP";
                    for (int i = 0; i < magic.Length; i++)
                        if (d[p + i] != (byte)magic[i])
                            throw new PudFormatException("Bad TYPE magic");
                    if (len >= 16)
                        TypeTag = ReadU32(d, p + 12);
                    break;

                case "VER ":
                    Version = ReadU16(d, p);
                    break;

                case "DESC":
                {
                    int end = p;
                    while (end < p + len && d[end] != 0)
                        end++;
                    Description = System.Text.Encoding.ASCII.GetString(d, p, end - p);
                    break;
                }

                case "OWNR":
                    Copy(d, p, Owner, Math.Min(len, SlotCount));
                    break;

                case "SIDE":
                    Copy(d, p, Side, Math.Min(len, SlotCount));
                    break;

                case "ERA ":
                    // ERAX (extended) overrides ERA when present; ERA may hold
                    // garbage above 3 in old editors — clamp to Forest then.
                    if (!_seenSections.Contains("ERAX"))
                        Era = ClampEra(ReadU16(d, p));
                    break;

                case "ERAX":
                    Era = ClampEra(ReadU16(d, p));
                    break;

                case "DIM ":
                    Width = ReadU16(d, p);
                    Height = ReadU16(d, p + 2);
                    break;

                case "SGLD":
                    CopyU16(d, p, StartGold, Math.Min(len / 2, SlotCount));
                    break;

                case "SLBR":
                    CopyU16(d, p, StartLumber, Math.Min(len / 2, SlotCount));
                    break;

                case "SOIL":
                    CopyU16(d, p, StartOil, Math.Min(len / 2, SlotCount));
                    break;

                case "AIPL":
                    Copy(d, p, AiType, Math.Min(len, SlotCount));
                    break;

                case "MTXM":
                    Tiles = ReadU16Array(d, p, len / 2);
                    break;

                case "SQM ":
                    MoveMap = ReadU16Array(d, p, len / 2);
                    break;

                case "OILM":
                    OilMap = new byte[len];
                    Copy(d, p, OilMap, len);
                    break;

                case "REGM":
                    RegionMap = ReadU16Array(d, p, len / 2);
                    break;

                case "UNIT":
                    for (int i = 0; i + 8 <= len; i += 8)
                    {
                        Units.Add(new PudUnitEntry
                        {
                            X = ReadU16(d, p + i),
                            Y = ReadU16(d, p + i + 2),
                            Type = d[p + i + 4],
                            Owner = d[p + i + 5],
                            Alter = ReadU16(d, p + i + 6),
                        });
                    }
                    break;

                case "UDTA":
                    UnitDataOverride = SliceOverride(d, p, len);
                    break;

                case "UGRD":
                    UpgradeDataOverride = SliceOverride(d, p, len);
                    break;

                // ALOW handled at M5 (tech restrictions); unknown sections skipped.
            }
        }

        static byte[] SliceOverride(byte[] d, int p, int len)
        {
            var payload = new byte[len];
            Array.Copy(d, p, payload, 0, len);
            return payload;
        }

        static PudEra ClampEra(ushort raw) => raw <= 3 ? (PudEra)raw : PudEra.Forest;

        static string ReadTag(byte[] d, int p) =>
            new string(new[] { (char)d[p], (char)d[p + 1], (char)d[p + 2], (char)d[p + 3] });

        static ushort ReadU16(byte[] d, int p) => (ushort)(d[p] | (d[p + 1] << 8));

        static uint ReadU32(byte[] d, int p) =>
            (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));

        static void Copy(byte[] src, int p, byte[] dst, int count) =>
            Array.Copy(src, p, dst, 0, count);

        static void CopyU16(byte[] d, int p, ushort[] dst, int count)
        {
            for (int i = 0; i < count; i++)
                dst[i] = ReadU16(d, p + i * 2);
        }

        static ushort[] ReadU16Array(byte[] d, int p, int count)
        {
            var arr = new ushort[count];
            CopyU16(d, p, arr, count);
            return arr;
        }
    }

    public sealed class PudFormatException : Exception
    {
        public PudFormatException(string message) : base(message) { }
    }
}
