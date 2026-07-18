namespace Craftwar.Sim
{
    public enum Race : byte
    {
        Human = 0,
        Orc = 1,
        Neutral = 2,
    }

    /// <summary>Per-player economy and status. Index = player slot 0-7.</summary>
    public struct PlayerState
    {
        public bool InGame;
        public Race Race;
        public int Gold;
        public int Lumber;
        public int Oil;
        public int FoodUsed;
        public int FoodMax;

        /// <summary>Completed research, one bit per UpgradeId (52 used bits).
        /// ALOW start-spells are pre-seeded here at match setup.</summary>
        public ulong Researched;

        // PUD ALOW restriction masks (all-ones when the map has no ALOW).
        public uint AllowedUnits;     // units/buildings bit order
        public uint AllowedUpgrades;  // upgrade bit order
        public uint AllowedSpells;    // spell bit order

        public bool HasResearched(UpgradeId u) =>
            u != UpgradeId.None && (Researched & (1ul << (int)u)) != 0;

        /// <summary>0, 1 or 2 — how many tiers of a two-level upgrade line.</summary>
        public int UpgradeLevel(UpgradeId level1, UpgradeId level2) =>
            (HasResearched(level1) ? 1 : 0) + (HasResearched(level2) ? 1 : 0);

        public void HashInto(ref StateHash h)
        {
            h.Add((byte)(InGame ? 1 : 0));
            h.Add((byte)Race);
            h.Add(Gold);
            h.Add(Lumber);
            h.Add(Oil);
            h.Add(FoodUsed);
            h.Add(FoodMax);
            h.Add(Researched);
            h.Add(AllowedUnits);
            h.Add(AllowedUpgrades);
            h.Add(AllowedSpells);
        }
    }
}
