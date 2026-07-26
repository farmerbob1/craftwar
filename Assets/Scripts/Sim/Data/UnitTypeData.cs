using System;

namespace Craftwar.Sim
{
    [Flags]
    public enum UnitTypeFlags : uint
    {
        None = 0,
        LandUnit = 1u << 0,
        AirUnit = 1u << 1,
        ExplodeOnDeath = 1u << 2,
        SeaUnit = 1u << 3,
        Critter = 1u << 4,
        Building = 1u << 5,
        Submarine = 1u << 6,
        SeesSubmarine = 1u << 7,
        Peon = 1u << 8,
        Tanker = 1u << 9,
        Transport = 1u << 10,
        OilSource = 1u << 11,
        GoldDepot = 1u << 12,
        CanGroundAttack = 1u << 14,
        Undead = 1u << 15,
        ShoreBuilding = 1u << 16,
        CanCast = 1u << 17,
        LumberDepot = 1u << 18,
        CanAttack = 1u << 19,
        Tower = 1u << 20,
        OilPatch = 1u << 21,
        GoldMine = 1u << 22,
        Hero = 1u << 23,
        OilDepot = 1u << 24,
        KilledByExorcism = 1u << 25,
        Mage = 1u << 26,
        Organic = 1u << 27,
    }

    /// <summary>
    /// Static per-unit-type stats, one row per PUD unit type id (110 rows).
    /// Field meanings follow the PUD UDTA section; costs are stored REAL
    /// (already multiplied by 10 from the on-disk /10 encoding).
    /// </summary>
    public struct UnitTypeData
    {
        public int Sight;
        public int Hp;
        public bool HasMagic;
        public int BuildTime;        // 6 units = 1 second
        public int GoldCost;         // real value (disk value x10)
        public int LumberCost;
        public int OilCost;
        public int SizeW, SizeH;     // pixel size
        public int BoxW, BoxH;       // selection box
        public int AttackRange;
        public int ReactRangeComputer;
        public int ReactRangeHuman;
        public int Armor;
        public bool RectSelectable;
        public int Priority;
        public int BasicDamage;
        public int PiercingDamage;
        public bool WeaponsUpgradable;
        public bool ArmorUpgradable;
        public byte MissileWeapon;   // 0x1d = none
        public byte MoveDomain;      // 0 land, 1 air, 2 naval (appearance/domain)
        public int DecayRate;        // x6 seconds, 0 = never
        public int Annoy;
        public byte RightClickAction;
        public int PointValue;
        public byte CanTarget;       // bit1 land, bit2 sea, bit4 air
        public UnitTypeFlags Flags;

        public bool Is(UnitTypeFlags f) => (Flags & f) != 0;

        /// <summary>Fingerprint for the build-identity handshake. Declaration
        /// order, like every other HashInto in the sim.</summary>
        public void HashInto(ref StateHash h)
        {
            h.Add(Sight);
            h.Add(Hp);
            h.Add((byte)(HasMagic ? 1 : 0));
            h.Add(BuildTime);
            h.Add(GoldCost);
            h.Add(LumberCost);
            h.Add(OilCost);
            h.Add(SizeW);
            h.Add(SizeH);
            h.Add(BoxW);
            h.Add(BoxH);
            h.Add(AttackRange);
            h.Add(ReactRangeComputer);
            h.Add(ReactRangeHuman);
            h.Add(Armor);
            h.Add((byte)(RectSelectable ? 1 : 0));
            h.Add(Priority);
            h.Add(BasicDamage);
            h.Add(PiercingDamage);
            h.Add((byte)(WeaponsUpgradable ? 1 : 0));
            h.Add((byte)(ArmorUpgradable ? 1 : 0));
            h.Add(MissileWeapon);
            h.Add(MoveDomain);
            h.Add(DecayRate);
            h.Add(Annoy);
            h.Add(RightClickAction);
            h.Add(PointValue);
            h.Add(CanTarget);
            h.Add((uint)Flags);
        }
    }

    /// <summary>
    /// Parser for the PUD UDTA payload layout (110-unit parallel arrays).
    /// Handles both the PUD section form (leading u16 "use default data"
    /// word) and the BNE unitdata.dat form (no leading word). Layout per
    /// war2tools doc/pud_format.txt + libpud parse.c (MIT).
    /// </summary>
    public static class UdtaParser
    {
        public const int UnitCount = 110;
        public const int PayloadSizeTrimmed = 5694;   // without leading word or swamp tail
        public const int PayloadSizeWithTail = 5948;  // + obsolete 127-word swamp block

        public static UnitTypeData[] Parse(byte[] payload, bool hasLeadingWord)
        {
            int b = hasLeadingWord ? 2 : 0; // base offset
            int size = payload.Length - b;
            if (size != PayloadSizeTrimmed && size != PayloadSizeWithTail)
                throw new ArgumentException($"UDTA payload size {size} not recognized");

            var units = new UnitTypeData[UnitCount];
            for (int i = 0; i < UnitCount; i++)
            {
                uint sizePacked = U32(payload, b + 2446 + i * 4);
                uint boxPacked = U32(payload, b + 2886 + i * 4);
                units[i] = new UnitTypeData
                {
                    Sight = (int)U32(payload, b + 1236 + i * 4),
                    Hp = U16(payload, b + 1676 + i * 2),
                    HasMagic = payload[b + 1896 + i] != 0,
                    BuildTime = payload[b + 2006 + i],
                    GoldCost = payload[b + 2116 + i] * SimConstants.CostStepValue,
                    LumberCost = payload[b + 2226 + i] * SimConstants.CostStepValue,
                    OilCost = payload[b + 2336 + i] * SimConstants.CostStepValue,
                    SizeW = (int)(sizePacked >> 16),
                    SizeH = (int)(sizePacked & 0xFFFF),
                    BoxW = (int)(boxPacked >> 16),
                    BoxH = (int)(boxPacked & 0xFFFF),
                    AttackRange = payload[b + 3326 + i],
                    ReactRangeComputer = payload[b + 3436 + i],
                    ReactRangeHuman = payload[b + 3546 + i],
                    Armor = payload[b + 3656 + i],
                    RectSelectable = payload[b + 3766 + i] != 0,
                    Priority = payload[b + 3876 + i],
                    BasicDamage = payload[b + 3986 + i],
                    PiercingDamage = payload[b + 4096 + i],
                    WeaponsUpgradable = payload[b + 4206 + i] != 0,
                    ArmorUpgradable = payload[b + 4316 + i] != 0,
                    MissileWeapon = payload[b + 4426 + i],
                    MoveDomain = payload[b + 4536 + i],
                    DecayRate = payload[b + 4646 + i],
                    Annoy = payload[b + 4756 + i],
                    // Right-click action table only covers the first 58 types.
                    RightClickAction = i < 58 ? payload[b + 4866 + i] : (byte)0xFF,
                    PointValue = U16(payload, b + 4924 + i * 2),
                    CanTarget = payload[b + 5144 + i],
                    Flags = (UnitTypeFlags)U32(payload, b + 5254 + i * 4),
                };
            }
            return units;
        }

        static ushort U16(byte[] d, int p) => (ushort)(d[p] | (d[p + 1] << 8));

        static uint U32(byte[] d, int p) =>
            (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));
    }
}
