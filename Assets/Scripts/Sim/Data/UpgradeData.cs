using System;

namespace Craftwar.Sim
{
    /// <summary>One row of the UGRD upgrade table (52 upgrades).</summary>
    public struct UpgradeData
    {
        public int Time;
        public int Gold;    // full value on disk (NOT /10)
        public int Lumber;
        public int Oil;
        public int Icon;
        public int Group;
        public uint Flags;

        /// <summary>Fingerprint for the build-identity handshake.</summary>
        public void HashInto(ref StateHash h)
        {
            h.Add(Time);
            h.Add(Gold);
            h.Add(Lumber);
            h.Add(Oil);
            h.Add(Icon);
            h.Add(Group);
            h.Add(Flags);
        }
    }

    /// <summary>
    /// Parser for the PUD UGRD payload (52-upgrade parallel arrays), with or
    /// without the leading "use default data" word (absent in BNE
    /// upgrades.dat). Layout per war2tools doc/pud_format.txt (MIT).
    /// </summary>
    public static class UgrdParser
    {
        public const int UpgradeCount = 52;
        public const int PayloadSize = 780; // without leading word

        public static UpgradeData[] Parse(byte[] payload, bool hasLeadingWord)
        {
            int b = hasLeadingWord ? 2 : 0;
            if (payload.Length - b != PayloadSize)
                throw new ArgumentException($"UGRD payload size {payload.Length - b} != {PayloadSize}");

            var upgrades = new UpgradeData[UpgradeCount];
            for (int i = 0; i < UpgradeCount; i++)
            {
                upgrades[i] = new UpgradeData
                {
                    Time = payload[b + i],
                    Gold = U16(payload, b + 52 + i * 2),
                    Lumber = U16(payload, b + 156 + i * 2),
                    Oil = U16(payload, b + 260 + i * 2),
                    Icon = U16(payload, b + 364 + i * 2),
                    Group = U16(payload, b + 468 + i * 2),
                    Flags = U32(payload, b + 572 + i * 4),
                };
            }
            return upgrades;
        }

        static ushort U16(byte[] d, int p) => (ushort)(d[p] | (d[p + 1] << 8));

        static uint U32(byte[] d, int p) =>
            (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));
    }
}
