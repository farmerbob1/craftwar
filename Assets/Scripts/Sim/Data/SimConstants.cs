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
        public const uint SimVersion = 5;

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

        // --- Gryphon Rider / Dragon "fireball" attack: BULLET.C hard-codes
        // O_DRAGON/H_GRIFFON in bullet_create_fireball to keep travelling past
        // the target after arrival, re-running damage_area every few frames
        // instead of stopping at one splash hit — a short chain of explosions
        // trailing the shot rather than a single impact. ---
        public const int FireballChainPulses = 3;  // extra splashes after the first hit
        public const int FireballChainStepPx = 48; // how far the impact point drifts per pulse

        // --- Spellcasting (UNIT.C update_spells / SPELL.C): mana is 0-255,
        // a fresh caster starts at a third of the bar, and it trickles back at
        // a fixed rate whether or not the owner is actively casting. Costs and
        // caps below are gwCastingCost[]/HEAL_MAX/RAGE_TIME verbatim. ---
        public const int MaxMana = 255;
        public const int InitialCasterMana = 255 / 3;   // 0xff/3, unit_new()
        public const int ManaRegenPeriodTicks = 40;      // SPELL_SPEED: +1 mana per
        public const int HealManaCostPerHp = 6;
        public const int HealMaxHpPerCast = 40;          // HEAL_MAX
        public const int ExorcismManaCostPerDamage = 4;
        public const int ExorcismMaxRingDelta = 3;       // SPELL.C action_exorcism's delta=0..3 expanding ring scan
        public const int BloodlustManaCost = 50;
        public const int BloodlustRageTicks = 1000;      // RAGE_TIME: ~20s at 50 Hz

        // --- Runes: SPELL.C action_runes/place_a_rune/update_runes. A flat
        // 200-mana cast tries to arm 5 traps in a plus shape (target + N/E/S/W),
        // refunding 40 mana (200/5) for each tile that failed to arm (off-map,
        // already standing a unit at that instant, or already occupied by
        // another active trap — global pool, not per-player). Each armed trap
        // sits until either RuneTrapLifeTicks passes or a ground unit — any
        // player's, ownership is never checked — steps on its exact tile, for
        // a flat RuneTrapDamage hit (no armor subtraction, only Unholy Armor's
        // total-immunity gate applies, via the same ApplyDamage check every
        // other damage source goes through). ---
        public const int RunesManaCost = 200;
        public const int RunesRefundPerFailedTrap = 40;  // 200 / 5
        public const int MaxRuneTraps = 50;              // MAX_RUNES, whole-match pool
        public const int RuneTrapLifeTicks = 512 * 4;    // RUNE_TIME
        public const int RuneTrapDamage = 50;            // RUNE_DMG
        // update_runes: `(gwRuneDelay[i] & RUNE_DELAY) == 1` with RUNE_DELAY
        // = 256-1 flickers the trap into view once every 256 ticks, 8 times
        // across its RuneTrapLifeTicks (2048-tick) life.
        public const int RuneFlickerPeriodTicks = 256;

        // --- Mage Tower / Temple of the Damned spells. Costs/durations are
        // gwCastingCost[]/SLOW_TIME/HASTE_TIME/INVIS_TIME/ARMOR_TIME/
        // FIRESHIELD_TIME verbatim (unitWarp's sign convention — negative
        // slowed, positive hasted — carries over to Unit.WarpTicks unchanged).
        //
        // Flame Shield: exhaustively grepped for `unitFire` (its status flag)
        // across the whole source tree — it is set, decremented, and used only
        // as a "don't recast" / "can't target a flyer" guard. No damage
        // reflection or other combat effect exists anywhere in the available
        // source, despite that being common Warcraft-lore belief. Implemented
        // here exactly as the source shows: a cosmetic status + a targeting
        // restriction, nothing more. ---
        public const int SlowManaCost = 50;
        public const int HasteManaCost = 50;
        public const int WarpTicks = 1000;               // SLOW_TIME/HASTE_TIME magnitude
        public const int InvisibilityManaCost = 200;
        public const int InvisibilityTicks = 2000;       // INVIS_TIME
        public const int PolymorphManaCost = 200;
        public const int FlameShieldManaCost = 80;
        public const int FlameShieldTicks = 500;         // FIRESHIELD_TIME
        public const int UnholyArmorManaCost = 100;
        public const int UnholyArmorTicks = 500;         // ARMOR_TIME
        public const int RaiseDeadManaCost = 50;         // per skeleton (RANGE_RAISEDEAD scan below)
        public const int RaiseDeadScanRadius = 6;        // SPELL.C action_raisedead's dx*dx+dy*dy <= 6*6
        public const int RaiseDeadMaxSkeletons = 10;     // ROT_TIMES-scale sanity cap on one cast

        // Blizzard/Whirlwind/Death and Decay: BULLET.C's actual shard/rot
        // mechanics (bullet_create_blizzard/rot chain a fixed number of
        // repeat hits via disp_bullet_blizzard/rot recursively re-arming
        // themselves), reusing this sim's existing chain-pulse projectile.
        // Whirlwind and Death and Decay are stationary here — the original's
        // Whirlwind (typhoon) also wanders slowly around its cast point for
        // its whole duration (disp_bullet_typhoon), which this doesn't
        // reproduce. Blizzard is NOT stationary: BULLET.C's blizzard_shards
        // actually flies each shard in from a fixed offset northwest of its
        // landing point (see BlizzardShardOffsetX/Y below) before it hits and
        // respawns the next one, rather than pulsing in place like Rot.
        public const int BlizzardManaCost = 25;          // SPELL_BLIZZARD, paid once for the whole barrage
        public const int BlizzardChains = 5;             // action_blizzard's 5 bullet_create_blizzard calls
        public const int BlizzardHitsPerChain = 10;       // BLIZZARD_TIMES
        public const int BlizzardDamagePerHit = 10;      // BLIZZARD_DMG
        public const int BlizzardScatterTiles = 2;       // bullet_create_blizzard's (rand%5-2) tile jitter per chain
        public const int BlizzardShardOffsetX = 110;     // BLIZZARD_MIN_X magnitude: shard launch point vs. landing point
        public const int BlizzardShardOffsetY = 170;     // BLIZZARD_MIN_Y magnitude
        public const int BlizzardShardJitterPx = 11;      // BLIZZARD_OFF_X magnitude: per-shard launch-point jitter
        public const int DeathAndDecayManaCost = 25;     // SPELL_ROT
        public const int DeathAndDecayChains = 5;        // action_rot's 5 bullet_create_rot calls
        public const int DeathAndDecayHitsPerChain = 10;  // ROT_TIMES
        public const int DeathAndDecayDamagePerHit = 10; // ROT_DMG (== BLIZZARD_DMG)
        public const int DeathAndDecayScatterTiles = 2;  // each of the 5 patches jitters +/-2 tiles
        public const int WhirlwindManaCost = 100;        // SPELL_WHIRLWIND
        // TYPHOON_LIFE=800 ticks at one hit every other tick (400 hits) is
        // reproduced at this sim's chain-pulse cadence of one hit/tick, so
        // the same total hit count lands in 400 ticks (8s) instead of 800 (16s).
        public const int WhirlwindHits = 400;
        public const int WhirlwindDamagePerHit = 4;      // TYPHOON_DMG

        // --- Spell cast/impact visual effects: synthetic ids above the real
        // UDTA missile-weapon range (0x00-0x1d), resolved by MissileSpriteBaker
        // the same way as a real missile. ---
        public const byte EffectSparkle = 0x40;   // generic (BULLET.C bullet_create_on(pTarget,BT_SPARKLE))
        public const byte EffectHeal = 0x41;
        public const byte EffectExorcism = 0x42;
        public const byte EffectRune = 0x43;
        public const byte EffectBlizzard = 0x44;
        public const byte EffectWhirlwind = 0x45;
        public const byte EffectDecay = 0x46;
        public const byte EffectBoom = 0x47;      // SPELL.C update_runes' BT_BOOM_FIRE trigger flash
        /// <summary>How long a cosmetic cast effect lingers on screen.</summary>
        public const int SpellEffectLingerTicks = 25;

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
        public const int MaxCorpses = 64;
        /// <summary>How long a dead Organic unit stays raisable — matches the
        /// view's own corpse decay/fade lifetime (UnitViewPool.CorpseSeconds),
        /// so a corpse stops being raisable only once it's actually gone.</summary>
        public const int CorpseLingerTicks = 1500;
    }
}
