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

        /// <summary>
        /// Simulation behaviour generation. Bump this whenever a change can alter
        /// simulation outcomes — new/changed systems, tuned constants, a
        /// different order of operations. Peers compare it at join time, so a
        /// mismatch becomes a refused connection instead of a desync a few
        /// hundred turns in. It is NOT part of the state hash.
        /// </summary>
        public const uint SimVersion = 1;

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
        public const int InOilTicks = 10;       // OIL_HARVEST_TIME: pumping is quicker
        public const int InDepotTicks = 25;     // ~0.5s dropping off
        public const int ChopTicks = 450;       // ~9s per 100 lumber
        public const int CarryAmount = 100;     // per trip (GOLD/LUMBER_HARVEST)
        public const int WoodPerTile = 100;
        // The real chopping art has NO map-tile (MTXM) id — the removed-tree
        // stumps and single-tree column pieces are "special" megatiles only
        // addressable by megatile number (121-123, 126 in every era; same
        // trick Stratagus uses). The view's tile catalog registers those
        // megatiles under these synthetic ids.
        public const ushort ChoppedTileId = 0xFF7E;    // removed-tree stumps
        public const ushort OneTreeTopTileId = 0xFF79; // lone column, top piece
        public const ushort OneTreeMidTileId = 0xFF7A; // lone column, middle
        public const ushort OneTreeBotTileId = 0xFF7B; // lone column, bottom
        // A tree can be walled off (mines/buildings): after this long without
        // path progress the peon retargets a tree near itself, then gives up
        // (the original's find_new_tree -> ORDER_GUARD fallback).
        public const int WoodStuckTicks = 75;
        /// <summary>Give up boarding if the transport never becomes reachable
        /// (it is sitting in open water rather than docked at the coast).</summary>
        public const int BoardStuckTicks = 150;
        public const int WoodSearchRadius = 15; // tile_find_tree range

        // --- Repair (DISPATCH.C: REPAIR_HP=4 per event, RES_COST=1 gold+
        // 1 lumber every REPAIR_TIME=2 events; event pacing tuned to ~5/s
        // so a farm patches up in seconds like the original feel) ---
        public const int RepairHpPerEvent = 4;
        public const int RepairEventPeriodTicks = 10;
        public const int RepairEventsPerCharge = 2;
        public const int RepairChargeGold = 1;
        public const int RepairChargeLumber = 1;

        /// <summary>Minimum gap between "under attack" notifications per player
        /// (10 s at 50 Hz). Presentation throttle only — never gates sim logic.</summary>
        public const int UnderAttackNotifyTicks = 500;

        // --- Berserker regeneration research: +1 HP/s (heuristic rate) ---
        public const int RegenPeriodTicks = 50;

        // --- Combat ---
        public const int AttackCooldownTicks = 50;  // ~1 attack/sec baseline
        public const int ProjectileSpeedPxPerTick = 8;
        public const int AcquisitionPeriod = 5;     // ticks between target scans
        public const byte MissileNone = 0x1d;       // UDTA "no missile" id

        // --- Splash (ground-targeted) projectiles: catapult/ballista/ship
        // cannon, BULLET.C bullet_create + damage_area. The impact point is
        // the target's position at launch plus a random pixel drift; damage
        // falls off from full to a quarter beyond the inner radius, both
        // fixed squared-pixel thresholds against max(dx^2, dy^2) — i.e. a
        // square blast, not a circle, exactly as the original computes it. ---
        public const int SplashDriftRange = 8;   // net_rand & (BULLET_DRIFT=7)
        public const int SplashDriftOffset = 3;  // - (BULLET_DRIFT/2)
        public const int SplashFullRadiusSqPx = (TilePixels * TilePixels) / 2 - 1;
        public const int SplashOuterRadiusSqPx =
            SplashFullRadiusSqPx + TilePixels * TilePixels + (TilePixels * TilePixels) / 4;

        /// <summary>Ticks between critter fidget rolls — twice a second. With the
        /// original's odds (~47 in 256 rolls become a step) a sheep wanders a
        /// tile every few seconds, which is the pace it keeps in WC2.</summary>
        public const int CritterFidgetTicks = 25;

        /// <summary>Ticks between victory evaluations — one second at 50 Hz.
        /// A full unit scan is cheap; running it every tick would be waste, and
        /// running it on a counter would be a desync hazard (see TickVictory).</summary>
        public const int VictoryCheckTicks = 50;

        // --- Limits ---
        public const int MaxPlayers = 8;
        public const int NeutralPlayer = 15;
        public const int MaxUnits = 1200;
        public const int MaxProjectiles = 256;
    }
}
