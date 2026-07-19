namespace Craftwar.Sim
{
    public enum SimEventKind : byte
    {
        None = 0,
        CommandDenied,        // A = (ushort)DenyReason, B = command Param
        TrainComplete,        // B = trained TypeId, UnitPacked = producing building
        ResearchComplete,     // B = (ushort)UpgradeId
        ConstructionComplete, // B = TypeId, UnitPacked = building
        UpgradeComplete,      // building tier swap done; B = new TypeId
        BuildSiteBlocked,
        UnderAttack,          // B = victim TypeId, UnitPacked = victim (throttled)
        MineCollapsed,
        PlayerDefeated,       // Player = the slot that just lost
        PlayerVictorious,     // Player = the slot that just won
    }

    public enum DenyReason : byte
    {
        None = 0,
        NotEnoughGold,
        NotEnoughLumber,
        NotEnoughOil,
        NotEnoughFood,
        TechUnavailable,
        Busy,
        SiteBlocked,
    }

    /// <summary>
    /// Derived per-tick output for the presentation layer, exactly like
    /// GameState.TileChanges: written by the sim from state transitions it has
    /// already decided, never read back by the sim, never hashed. Nothing here
    /// can affect determinism — the same (map, seed, command log) produces the
    /// same events precisely because the events are a pure function of the
    /// state transitions, not an input to them.
    /// </summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public byte Player;
        public ushort A, B;
        public uint UnitPacked;
    }
}
