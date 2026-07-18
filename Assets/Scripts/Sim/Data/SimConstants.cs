namespace Craftwar.Sim
{
    /// <summary>
    /// Fixed simulation constants. Pacing/economy values are transcribed from
    /// the original game data (utype.h / unit.h / gameloop.c of the WC2 source)
    /// as facts of the original design; see docs in the plan for provenance.
    /// </summary>
    public static class SimConstants
    {
        // --- Timing ---
        // The original runs a 100 Hz clock and advances the sim every 2 ticks:
        // 50 sim updates ("cycles") per second at 100% speed. We adopt 50 Hz
        // directly so original pacing constants transfer unscaled.
        public const int TicksPerSecond = 50;
        public const int MsPerTick = 20;
        // Lockstep command turn length; commands execute at turn boundaries.
        public const int TicksPerCommandTurn = 4;

        // --- Map / coordinates (CELL.H model) ---
        public const int TilePixels = 32;      // one tile ("matrix") = 32 px
        public const int CellPixels = 8;       // 4x4 cells per tile
        public const int CellsPerTile = 4;
        public const int MaxMapSize = 128;     // PUD dims: 32/64/96/128 square

        // --- Economy (utype.h) ---
        public const int GoldPerTrip = 100;    // GOLD_HARVEST
        public const int LumberPerTrip = 100;  // LUMBER_HARVEST
        public const int OilPerTrip = 100;     // OIL_HARVEST
        public const int MillFactorPct = 25;   // lumber bonus with mill
        public const int RefineryFactorPct = 25;
        public const int KeepFactorPct = 10;   // gold bonus at keep
        public const int CastleFactorPct = 20; // gold bonus at castle
        public const int FoodPerFarm = 4;      // UNITS_PER_FARM
        public const int CostStepValue = 10;   // stored costs are value/10

        // --- Production pacing (unit.h / utype.h) ---
        public const int UnitBuildCycles = 10; // UNIT_BUILD_CYCLES
        public const int BuildTurns = 12;      // BUILD_TURNS
        public const int UnitTurns = 2;        // UNIT_TURNS
        public const int UpgradeTurns = 2;     // UPGRADE_TURNS

        // --- Harvest pacing (ticks) ---
        public const int InMineTicks = 50;      // ~1s inside the mine
        public const int InDepotTicks = 25;     // ~0.5s dropping off
        public const int ChopTicks = 450;       // ~9s per 100 lumber
        public const int CarryAmount = 100;     // per trip (GOLD/LUMBER_HARVEST)
        public const int WoodPerTile = 100;
        public const ushort ChoppedTileId = 0x0057; // grass-with-stumps (verified in forest tileset)

        // --- Repair (DISPATCH.C: REPAIR_HP=4 per event, RES_COST=1 gold+
        // 1 lumber every REPAIR_TIME=2 events; event pacing tuned to ~5/s
        // so a farm patches up in seconds like the original feel) ---
        public const int RepairHpPerEvent = 4;
        public const int RepairEventPeriodTicks = 10;
        public const int RepairEventsPerCharge = 2;
        public const int RepairChargeGold = 1;
        public const int RepairChargeLumber = 1;

        // --- Berserker regeneration research: +1 HP/s (heuristic rate) ---
        public const int RegenPeriodTicks = 50;

        // --- Combat ---
        public const int AttackCooldownTicks = 50;  // ~1 attack/sec baseline
        public const int ProjectileSpeedPxPerTick = 8;
        public const int AcquisitionPeriod = 5;     // ticks between target scans
        public const byte MissileNone = 0x1d;       // UDTA "no missile" id

        // --- Limits ---
        public const int MaxPlayers = 8;
        public const int NeutralPlayer = 15;
        public const int MaxUnits = 1200;
        public const int MaxProjectiles = 256;
    }
}
