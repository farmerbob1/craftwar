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

## In flight (uncommitted fixes from playtest feedback)
1. Forest auto-tiling on chop: corner-based boundary recompute
   (`RetileForestAround`, GameSim.Economy.cs) — shape table from PUD spec.
   **Stump tile id unverified**: `SimConstants.ChoppedTileId = 0x0052`
   (plain ground). To find the real stump id: run
   `Craftwar > Debug > Dump Sprite Candidates + Forest Tileset` (editor
   menu; DebugDump.cs) → `%TEMP%\craftwar-dump\forest_tiles.png` + `.txt`,
   visually locate stumps, set the id.
2. Worker carry sprites: entries 56/57 (peasant gold/wood) and 67/68
   (peon gold/wood) are EDUCATED GUESSES wired in
   `UnitSpriteBank.CarryEntryOverride` with LooksLikeUnitBank sniffing as
   fallback. Confirm via the same dump (`entry_56.png` etc.).
3. Harvest entry/exit: walk to NEAREST footprint edge (WalkToBuilding),
   exit toward next destination (TryFindSpawnTileNear).
4. Movement feel: block-recovery thresholds tightened to 4/12/20/32.
NEXT STEP: close editor → run test suite → fix fallout → commit.

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

## Next milestones (per plan)
- **M5**: full land tech tree both races, research/upgrades applying
  (gbUpgradeStepsTbl magnitudes), repair, ALOW gating.
- **M6**: fog of war (sim visibility counters + shader), minimap, control
  groups, sounds. M7 sea/air/oil. M8 real HUD + menus + first-run import.
  M9 AI. M10 LAN lockstep. M11 online. (Details in plan file.)
