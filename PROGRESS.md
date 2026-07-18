# Craftwar — Progress Log

Continuation guide for any session. Read `CLAUDE.md` (architecture rules,
licensing, conventions) first. Full plan:
`C:\Users\mattc\.claude\plans\linear-stirring-moler.md` (M0-M13 roadmap).

## Verify loop
Editor must be CLOSED for batch runs:
```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs\r.xml -logFile Logs\t.log
```
Unity 6000.5.4f1 at `C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe`.
All work is committed per milestone; check `git log --oneline`.

## Done (all tests green at each commit)
- **M0** scaffold: asmdefs (Sim = pure C#, noEngineReferences), Pcg32,
  StateHash, GameCommand wire format, determinism test rig, 2D renderer.
- **M1** map pipeline: PudFile parser (all 27 BNE maps), .war archive +
  LZSS + tileset decoders (ported from war2tools, MIT), RuntimeTileCatalog
  atlas, TilemapView, CameraRig, Game.unity. BNE `unitdata.dat/upgrades.dat`
  = headerless UDTA/UGRD payloads → DataCodegen → `DefaultData.gen.cs`
  (round-trip test guards drift). RuleSet + per-map UDTA/UGRD overrides.
- **M2** movement: TerrainMap (SQM passability + clearance grids),
  occupancy-aware A* (idle units block, movers transparent, strict mode
  for livelock escape), tile-reservation movement w/ diagonal-speed quirk,
  ILockstepDriver + LocalLockstepDriver, Replay (hash-verified), unit
  sprites w/ team-color ramps, drag select, right-click orders.
- **M3** combat: auto-acquisition (staggered occupancy scans), Attack /
  AttackMove (chase + resume goal), WC2 damage formula, projectile pool,
  deaths, full per-era building sprite table, smart right-click,
  A+click attack-move, corpse fade.
- **M4** economy: gold cycle (hidden-in-mine, depot bonuses Keep 10% /
  Castle 20% / Mill 25%), wood chop mutates map (450t/100w), construction
  (HP ramp, hidden builder), training (food gates, ring spawn, rally),
  supply recount, frame-block animations (walk/attack/chop/death), HUD v1
  (OnGUI resource bar + build/train buttons + placement mode).

- **Playtest fixes** (commit 19644ef): carry sprites confirmed via dump
  (peasant gold/wood = entries 124/122, peon = 125/123; loaded-tanker art
  noted for M7), stump tile 0x0057 verified, forest boundary retiling,
  harvest entry/exit via nearest footprint edge, block-recovery 4/12/20/32.
- **M5** tech tree: UpgradeId (52 UGRD slots) + TechTree tables (build/
  train/research prereqs, hall/tower self-upgrades, ALOW bit maps),
  PlayerState.Researched mask + ALOW parsing/gating, Research/Cancel/
  Repair commands, upgrade magnitudes in combat (arrows +1, swords/axes
  +2, shields +2, siege +15, ship +5, longbow/lighter-axes +1 range,
  marksmanship strength→pierce per PSX DAMAGE.C), archer→ranger etc.
  transforms, berserker regen, repair (4 HP/event, 1g+1w per 2 events,
  DISPATCH.C), tech-tree-driven IMGUI command card.

## In flight
Nothing — M5 just landed; next milestone not started.

## Known gaps / decisions
- Sim system order: commands → production → movement → combat → harvest →
  construction(stub) → fog(stub) → victory(stub). Tick = 50Hz, turn = 4.
- BNE quirk: Town Hall UDTA lacks wood-storage bit; FindDepot accepts
  GoldDepot|LumberDepot for wood (original hardcoded behavior).
- AttackMove engages only chargers/adjacent while marching (chases once
  engaged); head-on swap deadlocks resolved by strict repath.
- Anim block layout is heuristic (walk 0-4, attack 5+, death last 3);
  refine per-unit against original .seq data later.
- HUD is IMGUI placeholder until M8; construction sites render translucent
  (no stage frames yet); no sounds yet; walls unsupported.
- Backlog task: PSX UNITDATA.ARR diff report (spot checks matched BNE).
- M5 heuristics to revisit: research time = UGRD Time × 50/6 ticks (same
  6-units-per-second convention as UDTA build time); repair event pacing
  10 ticks (HP/cost per event is exact from DISPATCH.C, the cadence is
  tuned); berserker regen +1 HP/s; cancel refunds 100% everywhere;
  demolition-squad suicide blast NOT implemented (they melee for now);
  spell research is recorded in PlayerState.Researched but casting is a
  later milestone; hall upgrades keep working sprites only if the sprite
  table has Keep/Castle art (it does, per-era building table from M3).

## Next milestones (per plan)
- **M6**: fog of war (sim visibility counters + shader — EffectiveSight
  helper already exists for scouting bonuses), minimap, control groups,
  sounds. M7 sea/air/oil (ship/tanker ALOW+upgrade plumbing already in
  place). M8 real HUD + menus + first-run import. M9 AI. M10 LAN
  lockstep. M11 online. (Details in plan file.)
