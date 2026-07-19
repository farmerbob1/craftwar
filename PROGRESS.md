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

Editor-open alternative: the Sim (+Net, +Import/War2) compiles standalone —
a scratch csproj with `<Compile Include="...\Assets\Scripts\Sim\**\*.cs"/>`
(+ Net, Import\War2), net10.0, AllowUnsafeBlocks, NUnitLite 3.14 runs every
pure test and ad-hoc repro harnesses in ~1 s without touching Unity.

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

- **Playtest fixes 2** (post-M5): harvest no longer walks a tile the wrong
  way after commands/mine/depot exits (TickMovement ran before TickHarvest
  picked the new target — orders now parked on issue/unhide); chop only
  starts once fully on a tile (mid-step park dragged the peon backwards);
  hidden/destroyed mid-step units release their reserved step tile (phantom
  blockers); wood retarget searches around the previous tree (saved-location,
  PSX HARVEST.C) instead of the depot, radius 6→15 (tile_find_tree), and a
  75-tick no-path timeout retargets/gives up on walled-off trees (Gold Rush
  mine rows made the nearest forest unreachable → permanent livelock);
  **forest retiling rewritten** — old version punched bare grass squares
  into the forest (bits==15 fell through to grass) and never repainted
  still-wooded neighbors. Now corner-vertex marching squares per
  pud_format.txt Appendix D (vertex = forest iff all 4 sharing tiles hold
  wood, off-map counts as wood; transitions drawn on the forest side like
  the original, PSX TILE.C retiles the same 3x3): neighbors → 0x07S0
  shapes. **The real chopping art has NO MTXM id**: removed-tree stumps =
  megatile 126, one-tree column pieces = megatiles 121-123 (identical in
  every era; found via PSX F_TREE.BIN whose center column maps every tree
  matrix to the stump matrix, cross-confirmed by Wargus "special" slots +
  wartool.cpp raw-megatile layout). RuntimeTileCatalog decodes them via
  War2Tileset.DecodeMegatile under synthetic ids 0xFF79-7B/0xFF7E
  (SimConstants). Remnant rules per the original: 1-wide vertical strips →
  top/mid/bot one-tree pieces (still wood); lone trees / 1-tall rows are
  unrepresentable → wood removed (no lumber), stumps left, removal ripples
  via worklist (PSX table flattens the same way; Stratagus removes
  unrepresentable wood). NOTE: 0x0057 was wrong (it's a rock/debris decor
  tile). Corner model validated against Gold Rush authored art (97.8%
  shape-group match). Original never fades stumps back to grass (no such
  code in PSX source). Walk animation now requires IsMoving — blocked/
  waiting units stand instead of treading air;
  view: teleports
  (exits, spawns) snap interpolation instead of sliding across the map;
  selection is a 9-sliced green rectangle under the sprite, not a tint.

- **UI framework** (83b1228, 4cf70bd): UI Toolkit HUD (UXML/USS in `Assets/UI`),
  `UIManager` + screen stack + four layers, InputRouter owning both action maps
  (built in code, not a .inputactions asset), sim→UI event channel drained via
  `GameLoopRunner.PendingSimEvents`, patrol order, unit action card, WC2-style
  selection panel.
- **M6** fog/minimap/groups/sound: per-player `Visible`/`Explored` byte grids on
  `GameState`, allocated in `Setup` for in-game slots only and hashed in
  `ComputeHash`; `GameSim.Fog.cs` recomputes sight every tick (footprint-aware
  squared-distance disc, no sqrt — `SimPurityTests` bans it), skipping Hidden
  and neutral units. `FogOfWar.shader` (the project's first) + `FogOfWarView`
  upload an RG mask, one texel per tile, bilinear so edges are soft. Enemies
  are hidden while fogged, enemy buildings leave a last-seen ghost, and the
  smart right-click refuses to resolve targets you cannot see (fog probing).
  `MinimapView` bakes terrain from per-tile average colours (`IMinimapPalette`,
  implemented by `RuntimeTileCatalog`), redraws dots + fog at 8 Hz, draws a
  viewport rect, click/drag to pan, right-click to command.
  `ControlGroups` (Ctrl+N assign, N recall, double-tap centre).
  `AudioDirector` + `IAudioProvider` + `PlaceholderAudioBank` (synthesized
  tones, no Blizzard data) wired to sim events and local order acks.

## In flight
Nothing — M6 just landed; M7 not started.

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
- FindNearbyWood has no reachability check (PSX gates on region map
  `rgn_has_access`); the ToWood stuck-timeout compensates. Revisit if
  peons dither between unreachable trees on real maps.
- M5 heuristics to revisit: research time = UGRD Time × 50/6 ticks (same
  6-units-per-second convention as UDTA build time); repair event pacing
  10 ticks (HP/cost per event is exact from DISPATCH.C, the cadence is
  tuned); berserker regen +1 HP/s; cancel refunds 100% everywhere;
  demolition-squad suicide blast NOT implemented (they melee for now);
  spell research is recorded in PlayerState.Researched but casting is a
  later milestone; hall upgrades keep working sprites only if the sprite
  table has Keep/Castle art (it does, per-era building table from M3).

- M6 decisions to revisit:
  - **Fog is recomputed in full every tick, not incrementally.** The plan called
    for reference-counted counters updated on move/spawn/death; a full
    recompute is O(units x r^2) (a few thousand int ops) and, unlike counters,
    cannot desync — counters would have to be adjusted correctly at every
    spawn, death, hide/unhide and mid-step tile swap, where one miss is a
    desync rather than a graphical glitch. Revisit only if profiling says so.
  - **Fog gates rendering only.** It is hashed state, but no sim system reads
    it, so combat acquisition is unchanged and fog's position after TickCombat
    in the fixed order is harmless. Gating targeting on sight would mean moving
    TickFog before TickCombat and re-tuning M3-M5 fight behaviour.
  - `ComputeHash` now walks 2 grids x W*H per in-game player (~262 KB on a
    128x128 8-player map). Free today — it is only called from tests — but M10
    desync checks will run it per turn; hash a rolling checksum then.
  - Attack-move stays on **Ctrl+click**, not the original's A+click: 'a' is
    command-card slot 3. Ctrl+click and Ctrl+number do not collide.
  - No team vision (M11), no sight-blocking terrain (WC2 has none either).
  - Audio is placeholder tones behind `IAudioProvider`; M8 swaps in a real
    War2 sound decoder. No sound decoder exists anywhere yet — war2tools never
    implemented one, so the entry ids must be found empirically (the
    `DebugDump` sweep is the template) or via Wargus `wartool.exe`.

## Next milestones (per plan)
- **M7** sea/air/oil (ship/tanker ALOW+upgrade plumbing already in place;
  fog keys off unit sight only, so air units need no fog rework). M8 real HUD
  + menus + music + first-run import (and the real sound decoder). M9 AI.
  M10 LAN lockstep. M11 online + team vision. (Details in plan file.)
