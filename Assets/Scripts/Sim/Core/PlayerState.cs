namespace Craftwar.Sim
{
    public enum Race : byte
    {
        Human = 0,
        Orc = 1,
        Neutral = 2,
    }

    /// <summary>
    /// Who drives a slot. Distinct from <see cref="PlayerState.InGame"/>: the PUD
    /// marks passive-computer and rescue slots as in-game so their units spawn,
    /// but they are not melee participants. Victory keys off this, never off
    /// InGame — otherwise a map with a rescue slot never resolves.
    /// </summary>
    public enum Controller : byte
    {
        None = 0,
        Human,
        Computer,
    }

    /// <summary>Melee standing of a slot. Hashed: it gates nothing today but the
    /// campaign track (M13) will branch on it, and it must survive save/replay.</summary>
    public enum PlayerOutcome : byte
    {
        Playing = 0,
        Defeated,
        Victorious,
    }

    /// <summary>Per-player economy and status. Index = player slot 0-7.</summary>
    public struct PlayerState
    {
        public bool InGame;
        public Race Race;

        /// <summary>Melee participant kind. Set once at Setup, never mutated.</summary>
        public Controller Controller;

        /// <summary>Alliance group. Free-for-all is modelled as every slot on its
        /// own team (Team = slot index), so the evaluator needs no special case.</summary>
        public byte Team;

        /// <summary>Mutated by TickVictory. The authoritative match result — the
        /// view polls this rather than relying on catching a one-frame event.</summary>
        public PlayerOutcome Outcome;

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

        // AI difficulty handicaps (set once at Setup from the lobby, then const).
        // Integer, identity at zero, so a match with no handicap hashes exactly as
        // before. Applied by sim systems (Deposit / EffectiveSight), never by the
        // out-of-sim AiPlayer — that keeps the cheat deterministic. Also usable as
        // a general per-player handicap in custom games later.
        public int HarvestBonusTenths; // +N/10 of every gold/wood/oil drop-off
        public int SightBonus;         // +N tiles of sight on every unit

        public bool HasResearched(UpgradeId u) =>
            u != UpgradeId.None && (Researched & (1ul << (int)u)) != 0;

        /// <summary>0, 1 or 2 — how many tiers of a two-level upgrade line.</summary>
        public int UpgradeLevel(UpgradeId level1, UpgradeId level2) =>
            (HasResearched(level1) ? 1 : 0) + (HasResearched(level2) ? 1 : 0);

        public void HashInto(ref StateHash h)
        {
            h.Add((byte)(InGame ? 1 : 0));
            h.Add((byte)Race);
            h.Add((byte)Controller);
            h.Add(Team);
            h.Add((byte)Outcome);
            h.Add(Gold);
            h.Add(Lumber);
            h.Add(Oil);
            h.Add(FoodUsed);
            h.Add(FoodMax);
            h.Add(Researched);
            h.Add(AllowedUnits);
            h.Add(AllowedUpgrades);
            h.Add(AllowedSpells);
            h.Add(HarvestBonusTenths);
            h.Add(SightBonus);
        }
    }
}
