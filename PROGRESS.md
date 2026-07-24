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

- **M7** sea/air/oil: **the SQM word is the original's `SQ_*` bit field, not an
  enumeration** — `TerrainMap.FromPud` now decodes it bitwise (utype.h masks),
  which finally gives us a shore concept. `MoveDomain` gained `SeaDock` (the
  original's `CLASS_WATER_CANDOCK`): coast is passable to tankers/transports but
  not to warships or land units, so islands are genuinely separated and a
  transport must *dock* before troops can board. `GameState.DomainOf` is the one
  place the UDTA 0/1/2 byte maps to a domain. Oil cycle reuses the gold path
  exactly as the original does (HARVEST.C routes tankers through `harvest_gold`):
  tanker → platform → shipyard/refinery, `InOilTicks` 10 vs 50, +25% per-player
  refinery bonus (`gwRefineryTbl`, not per-depot). `BuildSite` is the single
  placement rule shared by sim and ghost, covering land / shore (`!SQ_LAND`) /
  oil-platform (open water + a patch to consume). Transports: cargo is a
  back-pointer (`Unit.Transport` + `CargoCount`, both hashed), capacity 6,
  shore-gated unload in slot order, cargo drowns with the hull, and a
  `BoardStuckTicks` timeout so troops don't mill at an undockable ship. Full
  naval/air tech branch wired (oil platform is on the *tanker's* card, not the
  peasant's). Submarines are invisible and untargetable outside a per-player
  `Detected` grid — the first time fog gates gameplay rather than rendering.
  Laden-tanker art found at entries 126/127 by silhouette comparison against
  59/60 (100% hull containment + ~7% new pixels; cross-pairings don't match).
  152/152 EditMode.

## In flight
**M9.5 — Scriptable, tiered AI (rework of M9).** Plan:
`C:\Users\mattc\.claude\plans\enumerated-beaming-meteor.md`. Decisions (settled
with the user): declarative strategy DATA (not a bytecode VM / not a rule engine),
difficulty = skill scaling + optional handicap cheats, executor scope = base
layout + focus-fire + active defense + scouting/expansion, and evolve M9 in place
(its script becomes the first shipped data strategy). The original WC2 "ICE" AI is
the same split we already have — a tiny desired-state script driving native
engines — so this externalizes the script layer and strengthens the executor.

- **Phase A DONE (green: 265/265 EditMode; 195/195 standalone).** `AiScript`'s
  hardcoded `AiPhase[]` is replaced by an integer-only `AiStrategy` (Sim) parsed
  from a readable text file. New Sim files: `Ai/AiStrategy.cs` (data + `ByteWriter`/
  `ByteReader` canonical binary + FNV `Hash()`), `Ai/AiStrategyParser.cs`
  (line-oriented `key=value` format; lives INSIDE Sim — integer-only, SimPurity-safe
  — so the standalone harness can use it; the plan's "parse outside the fence" note
  was unnecessary), `Ai/AiTier.cs` (enum `Dumb/Normal/Smart/God`, behavior in Phase
  B), `Ai/BuiltinAiStrategies.cs` (land-attack embedded as text + parsed default).
  Player-facing copy at `Assets/StreamingAssets/Ai/land-attack.ai.txt`;
  `AiStrategyDriftTests` (EditMode-only) guards the two in sync. `AiPlayer` now takes
  an optional `AiStrategy` (defaults to the built-in land-attack), so **all existing
  M9 tests pass unchanged against the data-driven strategy** — the migration-fidelity
  proof. Replay bumped to **v2** (per-slot `AiStrategyHash` in the header, provenance
  only; v1 still reads). No editor codegen tool: the built-in text is a hand-authored
  `const` guarded by the drift test (there is no external ground truth to codegen
  from, unlike DefaultData). Phases C–F still to do.

- **Phase B DONE (green: 274/274 EditMode; 204/204 standalone).** Difficulty tiers
  `Dumb/Normal/Smart/God` (`Ai/AiTier.cs`): `AiTierParams` bundles SKILL (think
  cadence + competence toggles) and optional HANDICAP knobs; `AiTierTable.For(tier)`
  is the gradient — **Normal == the M9 baseline exactly** (cadence 25, no
  competences, no handicaps), so the migration stays byte-identical. `AiPlayer` now
  takes a tier; only cadence bites this phase (Dumb 50 / Normal 25 / Smart 18 / God
  12), the competence bools are dormant until C–E read them. Handicaps
  (`HarvestBonusTenths`, `SightBonus` on hashed `PlayerState`; start-gold/lumber
  bonus at Setup) flow lobby → `SlotSetup` → `GameSim.Setup`, applied in `Deposit`
  and `EffectiveSight` — **never by the out-of-sim AiPlayer**, so cheats stay
  deterministic and hashed. Every handicap default is integer identity, proven by
  the whole prior suite passing unchanged. Build-speed handicap deferred (touches
  more sites, least essential of the three). Only God cheats by default; Smart is
  pure skill. Phases D–F still to do.

- **Phase C DONE (green: 277/277 EditMode; 207/207 standalone).** `Ai/AiBasePlan.cs`
  replaces the naive first-valid spiral with a clustered, non-self-boxing placement
  for `PlannedLayout` tiers (Smart/God); Dumb/Normal keep the spiral, so the M9
  baseline is untouched. It reuses `AiSiteSearch`'s validity (BuildSite +
  mine-lane + ≥3 open perimeter) and scores valid plots: hug existing friendly
  buildings (compact base, shorter worker paths), keep the plot's own perimeter
  open, prefer small rings, and **reject any plot that would brick in a neighbour**
  (leave it <2 open perimeter). `AiPlayer.Build.FindSiteAvoidingBlacklist` calls it
  when `_tier.PlannedLayout`, falling back to the spiral if it finds no clustered
  plot. Deterministic (fixed ring/scan order, integer scores, first-wins ties).
  A Smart AI builds out a full base and marches its army out (not boxed); it even
  banks more gold than the spiral thanks to the shorter harvest paths. Phases D–F
  still to do.

- **Phase D DONE (green: 282/282 EditMode; 212/212 standalone).**
  `Ai/AiPlayer.Army.cs` adds the tactical layer the original did in native ICE.C —
  all gated on tier competences, so Dumb/Normal never call it (M9 baseline).
  **Active defense** (`TryDefendBase`): an enemy attacker within 12 tiles of any
  of our buildings recalls the whole army onto it, preempting the muster.
  **Focus-fire** (`ManageFocusFire`): once the army is within 8 tiles of the foe,
  concentrate Attack on the lowest-HP enemy in range (lower index breaks ties).
  **Reinforcement** (`ReinforceFront`): idle combat units are AttackMoved to the
  stored wave target instead of trickling out. `ThinkMilitary` runs these before
  the post-wave cooldown gate so an ongoing fight is fought well; the wave target
  is recorded on every launch. Tests prove focus-fire hits the weakest, defense
  recalls to the intruder, Normal does neither, and a Smart-vs-Normal match still
  resolves (tactics don't deadlock). Phases E–F still to do.

**M9 — scripted AI opponent, code complete.** Plan:
`C:\Users\mattc\.claude\plans\abundant-launching-hammock.md`.

- **Architecture: the AI runs OUTSIDE `GameSim.Advance`** as a command-emitting
  player (`Assets/Scripts/Sim/Ai/`, namespace `Craftwar.Sim.Ai`). `AiPlayer.Think`
  reads sim state and fills a buffer with `GameCommand`s stamped with its slot;
  `GameLoopRunner` submits them through the lockstep driver at a fixed point per
  tick (immediately before `TryGetTickCommands`), so replays record AI commands
  like anyone's and **AI tuning never invalidates a replay**. Playback constructs
  no AIs. The code lives inside `Craftwar.Sim` so `SimPurityTests` enforces
  integer-only determinism. It uses NO randomness at all (and must never touch
  `GameState.Rng` — that would desync replay verification).
- **Behavior** transcribed from the original VLAND/COMMON AI script data (facts
  only): linear phase script (`AiScript`) — build order Hall→Mill→Barracks→
  Smith→upgrades→2nd Barracks→Keep→Stables→Towers→Castle→Church→MageTower,
  worker targets 9→12→15→19→22→25, wave sizes 3,5,6,7,7,9…, endgame loop at 11,
  500-tick post-wave sleep; economy thresholds MIN_GOLD 500 / LOW_GOLD 1000 /
  LOW_TREE 500 / PLENTY_TREE 2000; rebuild-only rule (gold<200 && lumber<100);
  suicide all-in when buildings drop below 3 (gated on having ever had 3).
  Race-neutral roles resolve per race via `AiRaceMap` (no such pairing table
  existed anywhere). Fog: the AI cheats for targeting (like the original) but
  pays and harvests honestly.
- **Key mechanisms:** pending-build ledger (Build deducts cost on builder
  ARRIVAL — the AI reserves in-flight costs or it double-spends; tested by a
  zero-resource-denies invariant), per-think claim list (a unit is never tasked
  twice in one think), deterministic ring-spiral site search (`AiSiteSearch`,
  mine-lane keep-out + ≥3-open-perimeter rules), strict-order script walk with
  stall relaxation (NoSite → wider radius → skip entry), TrainSubstitute always
  applied (ranger/berserker/paladin/ogre-mage), farm-on-food-pressure ahead of
  the script.
- **Two deliberate liveness deviations from the original:** (1) oil-costing
  goals are skipped permanently when unaffordable — the land script never
  builds an oil economy, so waiting is provably futile (maps without SOIL would
  otherwise dead-stall the build order at the Blacksmith); (2) when no gold
  mine remains and no wave has launched for 1500 ticks, the AI attacks with
  whatever it has (`AiScript.DryWaveTicks`) — a mined-out map must still
  resolve to a victor.
- **Sim bug fixes shaken out by AI matches (both were match-blockers):**
  a worker inside a gold mine when it collapsed was never unhidden — invisible,
  untargetable, unkillable, so victory could never resolve (`GameSim.Economy`
  InMine now surfaces it empty-handed); a combat-razed construction site never
  released the builder hidden inside it (`ApplyDamage` now calls
  `ReleaseBuilder` first).
- **Driver:** `LocalLockstepDriver.TryGetTickCommands` now sorts canonically
  (stable insertion sort by player; intra-player order preserved) — the shape
  the interface always promised and M10's net driver needs.
- **Menu:** the skirmish panel parses the selected PUD and shows one row per
  playable slot (slot 0 locked to "You" — the view hard-codes LocalPlayer=0;
  others cycle Computer↔Off and Human↔Orc; AIPL passive shown). Start always
  populates `MatchConfig.slots`; `SlotConfig` gained `aiType` (app-side only).
  With no rows (unparseable map) it falls back to PUD OWNR/SIDE exactly as
  before — and that fall-through path ALSO gets AIs, so pressing Play directly
  on Game.unity with a melee map fights back.
- **Tests:** `AiTestHarness` (TwoBaseMap + RunAiMatch/Playback),
  `AiDeterminismTests` (same-seed hash equality; replay playback with NO AIs
  reproduces the live hash; byte round-trip), `AiEconomyTests` (farm-first,
  worker ramp, harvest split, no-resource-denies, ledger holds back the second
  build), `AiBuildTests` (site search valid/deterministic/keep-out/boxed-in,
  race map total), `AiMilitaryTests` (wave at muster size then 500-tick sleep,
  targets enemy hall, rangers-never-archers, passive emits nothing),
  `AiMatchTests` (**the M9 criterion**: AI-vs-AI reaches exactly one
  Victorious + one Defeated within budget — resolves ~t58k; idle human loses),
  `LockstepDriverTests` (canonical sort stability). 185/185 in the standalone
  harness.

Still to do for M9: run the batch EditMode gate (editor must be closed), play a
real 1v1 vs the AI in the editor (win and lose), and the M8 playtest checklist
below remains outstanding.

**M8 — all phases landed, needs a playtest pass.** 234/234 EditMode. Plan:
`C:\Users\mattc\.claude\plans\synchronous-booping-bengio.md`.

- **Phase 0** — repaired `UIAssetCatalog` (three UXML refs had been null since
  the UI-framework commit; the guard now checks fields, not just existence);
  split `UIManager.Init` into `Init`/`SetRoot` so a scene with no sim can host
  the stack; added a UnityEngine-free JSON reader; corrected stale docs.
- **Phase 1 — victory.** `IVictoryEvaluator` + `MeleeVictoryEvaluator`;
  `PlayerState` gained hashed `Controller`/`Team`/`Outcome`; `MatchSetup.FromPud`
  preserves the human-vs-computer distinction `Setup` used to discard.
  **Victory keys off `Controller`, never `InGame`** — passive/rescue slots are
  in-game (their units spawn) but are not opponents, and an InGame-keyed check
  never resolves on those maps. `TickVictory` full-scans once a second rather
  than maintaining counters, same desync reasoning as fog.
- **Phase 2 — scene flow.** `MatchConfig` + `MatchSession`, `Menu.unity` at
  build index 0, main menu, map picker, victory screen, Surrender (a
  `GameCommand`, so it travels lockstep), timestamped replays.
- **Phase 3/4 — asset seam and migration.** `IAssetSource` +
  `LooseFileAssetSource` + `Wc2InstallLocator`; sprites and tilesets now read
  named files from the install. `FileForUnit` was *generated* by composing
  `EntryForUnit` with a pixel-exact reverse lookup of all 267 loose `.grp`s.
- **Phase 5 — audio.** `RiffWav` (~150 lines) plus `Wc2SoundCatalog`, which
  scans rather than constructs filenames. `IAudioProvider` gained a per-unit
  axis; variant draws use `UnityEngine.Random`, never `GameState.Rng`.
- **Phase 6 — names and icons.** Real names from `Strings/<locale>.json`;
  command-card icons from the HUD atlas, resolved in the view from
  `(Kind, Param)` so the model stays presentation-free.
- **Phase 7 — music.** `MusicDirector` (crossfade, shuffled bag,
  `DontDestroyOnLoad`) over `MusicLibrary`, which prefers the converted Ogg
  cache and falls back to streaming the install's WAVs.
- **Phase 8 — first-run import.** `ImportWizardScreen`; writes a pointer,
  copies nothing.

**Known incomplete:** `UnitIconTable` is hand-authored (no icon field exists
anywhere in UDTA) and covers only unambiguous art — everything else still shows
the initials box. It is **unverified in play**; check entries against the real
game before extending it. Portraits, the WC2 HUD skin (`ThemeWc2.tss`) and the
Options screen are not started. `PauseMenuScreen`'s Save button is still
disabled — there is no `SimSerializer`, and that is M10 reconnect work.

## The asset situation (supersedes several older notes below)
**Everything ships loose and decoder-free in the Remastered install** under
`x86\Data\`: `Gamesfx/` (399 named PCM WAVs + `gamesfx.lst`), `Sfx/`, `Speech/`,
`Music/`, `Art/unit/**/*.grp` (94 named sprite banks), `Art/bgs/<era>/*.{ppl,vr4,vx4,cv4}`,
`Art/classic/HUD/` (196 icons × 4 eras at 46×38 + team masks + HUD chrome),
`Strings/enUS.json` (`unit_<typeId>` keys map onto `UnitTypeId`).

There is **no `maindat.war` in the install** — `LocalAssetPaths.maindatWar`
points at a war2tools *sample* on the Desktop, which is why the pre-M8 pipeline
could not bootstrap anywhere but this machine. `dataRoot` is now the primary
source and `maindatWar` an optional legacy fallback. The only archive present is
`x86\Data\Files\War2Dat.mpq` (MPQ v1, 1192 files).

Phase 0d proved the loose files decode identically: tilesets byte-identical with
a clean folder↔era diagonal, and 253 of 267 `.grp` files matching a maindat entry
pixel-for-pixel (the rest are simply absent from that older archive — loose is a
strict superset). **Era trap:** `bgs/Swamp` is the *Wasteland* era, `Iceland` is
Winter; GRP prefixes are none=Forest, `s_`=Winter, `l_`=Wasteland, `x_`=Swamp.
The folder also disambiguates race — `Human/x_sub.grp` is the submarine (526),
`Orc/x_sub.grp` the turtle (527).

## Known gaps / decisions
- Sim system order: commands → production → movement → combat → harvest →
  transport → construction(stub) → fog → victory. Tick = 50Hz, turn = 4.
  `TickConstruction` is the only remaining stub (real construction lives in
  `TickProduction` + `TickBuilderWalk`); `TickVictory` runs a full scan once a
  second, deliberately not incremental counters — same desync reasoning as fog.
- BNE quirk: Town Hall UDTA lacks wood-storage bit; FindDepot accepts
  GoldDepot|LumberDepot for wood (original hardcoded behavior).
- AttackMove engages only chargers/adjacent while marching (chases once
  engaged); head-on swap deadlocks resolved by strict repath.
- Anim block layout is heuristic (walk 0-4, attack 5+, death last 3);
  refine per-unit against original .seq data later.
- The IMGUI HUD is gone (83b1228) — the UI Toolkit HUD is the only one. The sole
  remaining OnGUI is `DebugOverlay.cs` (F3), which stays IMGUI on purpose.
  Construction sites render translucent (no stage frames yet); walls unsupported.
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
  - Audio is placeholder tones behind `IAudioProvider`; M8 swaps in the real
    sound. **CORRECTED (M8): there is no sound decoder to write.** The earlier
    note here — that maindat.war entry ids had to be found empirically — was a
    dead end twice over. A sweep of all 528 entries found only 5 WAVs and 51 XMI
    tracks; the SFX corpus was never in that archive. The real sounds ship loose
    and uncompressed as named PCM WAVs (see "The asset situation" above), so all
    that is needed is a RIFF reader. Do not restart the `DebugDump` hunt.

- M7 decisions to revisit:
  - **Coast is no longer land-passable.** This is faithful (utype.h: shore is
    "unpassable unless CLASS_WATER_CANDOCK") and it severs land routes on the
    island maps — Forsaken Isles, Frosty Fjords, Isolation, Dark Peninsula and
    the narrow pinches on Ant Trails / Murky River / Dark Peninsula. That is the
    point: the old value-switch decode let footmen walk between islands along the
    coast. Verified across all 26 BNE maps; the water-free maps are byte-identical.
  - Submarine detection is read by combat one tick stale (`TickFog` still runs
    after `TickCombat`). Reordering would re-tune M3-M5 fighting; not worth it.
  - Air units path with the same no-corner-cutting rule as ground. Harmless
    (clearance is uniform for air) but slightly conservative around obstacles.
  - `TryFindSpawnTileNear` rings out to 3 tiles. A transport unloading onto a
    one-tile beach with 6 aboard will leave the surplus on board rather than
    scatter them; the original is similarly stingy. Revisit if it annoys.
  - Transport boarding needs the ship docked on coast. That matches the original
    but there is no UI affordance telling the player so — M8 HUD work.
  - Anim blocks for ships/flyers are forced to pose 0 (no gait). The per-unit
    `.seq` pass is still the standing backlog item.

## Next milestones (per plan)
- M9 code-complete (see "In flight"); playtest it plus the M8 checklist.
  M10 LAN lockstep. M11 online + team vision. (Details in plan file.)

## Playtest checklist for M8
Nothing below is covered by the test gate — it all needs eyes on the running game.
1. Launch to `Menu.unity`: main menu appears, menu music plays.
2. Skirmish → pick a map → match loads, in-game music starts per race.
3. Select units: correct voice lines, one bark per selection, not twelve.
4. Command card: icons where mapped, initials elsewhere, and **check the mapped
   icons are the right units** — `UnitIconTable` was derived by eye.
5. Play to victory *and* to defeat: screen appears, sting plays, Restart and
   Main Menu both work and leave nothing behind.
6. Surrender from the pause menu resolves the match.
7. Replays: each finished match writes its own timestamped `.cwrp`.
8. **Fresh-machine simulation** — move `LocalAssetPaths.json` out of the repo
   root, clear `persistentDataPath`, and confirm the wizard finds the install
   and hands over to a playable menu.
