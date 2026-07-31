# Craftwar — Progress Log

Continuation guide for any session. Read `CLAUDE.md` (architecture rules,
licensing, conventions) first. **This file is the milestone scope of record** —
the old `~/.claude/plans/linear-stirring-moler.md` M0-M13 roadmap was overwritten
by a session summary and no longer exists. Per-milestone plans still live in
`~/.claude/plans/` (M10 = `delightful-hugging-bee.md`).

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
  `UIManager` + screen stack + four layers, InputRouter owning the action maps,
  sim→UI event channel drained via
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
- **Input rebuilt on the original's keys.** Bindings now live in
  `Assets/Resources/CraftwarControls.inputactions` (three maps: Gameplay,
  Camera, System) instead of being assembled in code; `InputRouter` resolves it
  by name and fails loudly if an action is missing. The command card dropped
  the placeholder QWE/ASD/ZXC grid for WC2's per-command letters — Move M,
  Stop S, Attack A, Build B, Advanced V, Farm F, Barracks B, Peasant P, and so
  on, per `Keybindings.txt`. The letter belongs to the *command*, so
  `CommandHotkeys` is the lookup and one `CommandHotkey` action carrying A-Z
  resolves the pressed key against the live card; a Ctrl/Alt chord is never a
  card press. Also wired: F10 menu, F5 options, +/- speed, Alt+C centre on
  selection, F2-F4 map bookmarks (Shift+Fn saves), Escape driving the card's
  Cancel button. Debug overlay moved F3 → backquote, since F3 is bookmark 2.
  The letters must stay unique per card face; `CommandCardModel` warns in the
  editor if they ever aren't. Two entries are inferred rather than quoted (the
  human Stables, absent from the reference, takes A) and both are flagged in
  `CommandHotkeys`.

- **M8/M9/M9.5 playtested and fixed** (587deec + this pass). The M8 checklist that
  used to sit at the bottom of this file is DONE: menu/music/voice flow, real
  command-card icons verified against the running game, victory *and* defeat
  paths, surrender, per-match replays, fresh-machine import. Playtest fixes that
  landed: pacing, real icons + animations, corpses, critters, and the camera fix
  below. **Do not re-run that checklist — it is closed.**
- **Camera/HUD viewport** (59383be): the rig clamped against the whole render
  target while the HUD paints a 208px sidebar + 34px resource bar over it, so the
  map's left columns and top rows were unreachable. `CameraRig` now polls
  `HudScreen.ChromeInsetsPixels()` and does every clamp, centring and half-extent
  against the unobscured sub-rect (minimap viewport box included). Edge scroll had
  been disabled outright under `UNITY_EDITOR`; re-enabled, gated on window focus
  and `InputRouter.CameraInputActive` instead.

## In flight
**M10 — LAN lockstep.** Plan: `C:\Users\mattc\.claude\plans\delightful-hugging-bee.md`
(phases 0-6; decisions settled with the user are listed there). Standalone
harness baseline was 246/246; now **257/257**.

- **Phase 0 DONE — driver seam hardened.** `GameLoopRunner.Update` no longer
  banks wall-clock debt: the accumulator is clamped to `MaxTickDebt` ticks, and a
  driver that withholds a tick clamps it to one tick, so a 10 s network stall
  resumes at real speed instead of fast-forwarding ~500 ticks. `Starving` is
  exposed and freezes `Alpha` rather than interpolating into an unconfirmed tick.
  AI `Think` is now guarded by `_lastAiThinkTick` — a stalled driver used to
  re-think the same tick every rendered frame and resubmit the same orders.
  **The guard is per-TICK on purpose:** quantizing `Think` to the 4-tick command
  turn would break the AI outright, since `AiPlayer` gates on
  `(Tick + Slot*7) % ThinkPeriod` with periods {22, 25, 50} — Normal/Smart would
  lose 3 of 4 thinks and the odd slots at Dumb/God would never think at all.
  `ByteWriter` grew `Ensure`/`ToArray`/`WriteBytes` (variable-size payloads no
  longer need size estimation); **this fixed a live latent bug** — `Replay.ToBytes`
  pre-sized its header at 50 bytes while writing 54, so a replay with 0 entries,
  or 1 entry carrying a full 18-unit selection, threw `IndexOutOfRange`. Only the
  app's early-return on an empty log hid it. Both existing pre-size callers
  (`Replay`, `AiProfile`) copied out of their *local* buffer and had to move to
  `ToArray()` or growth would have silently truncated them. New
  `ReplayLockstepDriver` (with a `StarveFor` hook, so the starvation path is
  testable without a socket) and `DelayedLockstepDriver` — the app finally has a
  replay *playback* path, and the swap-the-driver seam is proven before any
  networking exists.

- **Phase 1 DONE — rolling hash.** `ComputeHash` walked ~555 KB per call (32 KB
  tiles, ~125 KB units, ~393 KB fog); it is now per-entity walks plus running
  checksums, cheap enough to run every command turn. `Visible`/`Detected` are
  **dropped** from the hash: `TickFog` clears and rebuilds both every tick, so
  each is exactly `F(hashed state at end of tick)` — memoryless. Combat does read
  `Detected` one tick stale, but a divergence there implies a divergence in its
  already-hashed inputs, caught on the earlier tick before it can reach
  `AttackTarget`. `Explored` accumulates so it stays hashed, via a per-player
  checksum stamped **only on the unexplored→explored transition** —
  `GameSim.Fog.cs` writes `explored[t] = 1` unconditionally every tick, so an
  unguarded commutative add could never match a recompute. `Tiles` is now private
  behind `Tile()`/`SetTile()`/`InstallTiles()` with an invertible checksum (two
  write sites); `TerrainMap` gained `TerrainChecksum` over `_wood` + `_passable`.
  **Occupancy is now hashed, which it never was** — it gates pathing, target
  acquisition and build placement, and because `Occupy` overwrites while `Vacate`
  only clears cells matching its own id, it is a function of write *history*, not
  of current unit positions. It was the one grid that could genuinely diverge on
  its own. `VerifyChecksums()` recomputes every maintained value from scratch and
  is asserted in tests; **it caught a real bug on its first run** (a fresh
  all-zero grid's true checksum was non-zero while the running total started at
  0 — `CellMix` now maps 0 to 0). `SimPurityTests` gained two guards: `Net`
  sources must stay UnityEngine-free (this is what protects the standalone
  harness), and no file outside `GameState.cs` may write `Tiles[...]` directly.

- **Phase 2 DONE — turn scheduling and the host-relay protocol, on loopback.**
  274/274. `Craftwar.Net` is now `noEngineReferences: true`, enforced by
  `SimPurityTests`, so the whole protocol runs headless (the UTP socket lands in
  a separate `Craftwar.Net.Unity` assembly in Phase 4, created with its first
  file — an empty asmdef only earns a console warning).
  `TurnLockstepDriver(ticksPerTurn, inputDelayTurns, slot, exchange)` owns all
  the turn/tick/delay arithmetic; `ITurnExchange` decides who agrees a bundle,
  with `LocalTurnExchange` for single player and `LoopbackTurnExchange` for
  tests. **`(1, 1)` reproduces `LocalLockstepDriver` exactly** — a pinned test —
  so the scheduling is measured against behaviour already known good. A turn's
  bundle executes on its FIRST tick, the rest run empty, and a mid-turn tick may
  not outrun its own turn. **Delay semantics: a command issued while turn X
  executes runs at turn X+L**; the buffer filled during turn X is published at
  the start of turn X+1 as the input for turn X+L, and turns `0..L-2` are
  bootstrapped empty. Minimum L is 1 — a command cannot execute in the turn it
  was issued, because peers must agree first. `TurnRelay` is the host's arbiter:
  one input per slot per turn, frozen only when every participant is in, emitted
  **in ascending slot order** (`SortCanonically` is stable on player alone, so
  the bundle is only well defined if each player's commands form one contiguous
  run). It drops commands a peer submits for a slot it does not own, and
  compares per-turn hashes **only between peers that hashed the same turn** —
  paused peers execute different tick counts, so hashes from different turns are
  incomparable rather than a desync. Wire format in `NetMessages` (round-tripped
  in tests) plus `BuildIdentity`: protocol version, new `SimConstants.SimVersion`,
  map hash, new `RuleSet.Hash()` taken *after* map overrides, and the AI profile
  hash — turning "your stat table differs by one value" from a mystery desync 300
  turns in into a refused join naming the field. `CommandOp.Pause`/`Resume`
  appended (17/18); **`GameSim` ignores both** — a tick carrying them is proven
  indistinguishable from an empty one — because pausing must not touch sim state
  or the tick a replay resumes on would depend on when someone paused. The driver
  acts on them instead, holding a *set* of pausing slots so two simultaneous
  pausers cannot cancel out. `AiPlayer` gained `OrderInFlightGraceTicks` so the
  pending-build ledger keeps its reservation while an order is in flight; today's
  think periods {22,25,50} already exceed the largest delay (16 ticks), so this
  is pre-emptive rather than a fix for an observed break.
  **The headline test:** two independent `GameSim`s, each with its own driver
  over one relay, stay bit-identical for 1200 ticks at 2 turns of delay while
  both players issue orders — executing identical bundles at identical ticks.

- **Phase 3 DONE (needs a playtest) — the local seat is no longer 0.**
  `HudScreen.LocalPlayer` went from `const byte = 0` to a settable static, and
  the four private `const byte LocalPlayer = 0` copies in `WorldInputController`,
  `UnitViewPool`, `BuildPlacementGhost` and `DebugOverlay` now forward to it, so
  all ~35 call sites follow automatically. `GameLoopRunner.Start` sets it from
  `MatchConfig.localSlot` before any view is built. The skirmish lobby can now
  move the "You" seat: the controller button cycles You -> Computer -> Off on
  every row (it used to be disabled on row 0), exactly one row may be You, and
  the seat you pick becomes `localSlot`. Touches no Sim or Net code, so the
  harness stays at 274/274; all seven assemblies compile.
  **Playtest ask: run a skirmish as a seat other than slot 1 in the list** and
  check that selection, orders, fog, the resource strip and the green selection
  ring all follow the seat you chose rather than snapping back to seat 0.

- **Phase 4 PART DONE — transport and peer protocol land; the lobby does not.**
  280/280. `HostTurnExchange`/`ClientTurnExchange` implement the star topology
  over `IPacketPeer`, with `LoopbackNetwork`/`LoopbackPacketPeer` letting several
  peers run in one process. **`HostClientTests` is the strongest proof so far**:
  two independent sims, real serialization, staying bit-identical for 1200 ticks
  while both players issue orders; a client order provably lands in the *host's*
  world; a command forged for another slot is rejected; a corrupted client state
  is detected and halts **both** peers; and losing a peer is reported by SEAT,
  not by socket. The host is a player, not a referee — its own hash is one of the
  compared values, which a pure relay could not provide.
  New `Craftwar.Net.Unity` assembly: `UtpPeerSocket` (one Fragmentation ->
  ReliableSequenced pipeline, `windowSize: 64`, `disconnectTimeoutMS: 5000`,
  every `BeginSend`/`EndSend` failure logged — a silently dropped turn packet
  presents as an unexplained permanent stall), and `LanDiscovery` on a raw
  `UdpClient` because **UTP cannot broadcast at all**, using per-interface
  SUBNET-DIRECTED addresses since limited broadcast picks one interface and on a
  dev box that is usually a virtual adapter. **API gotcha:**
  `WithReliableStageParameters`/`WithFragmentationStageParameters` are extensions
  in `Unity.Networking.Transport.Utilities`, not members of `NetworkSettings`.
  `NetSession` carries the live socket across the scene load, mirroring
  `MatchSession`. `GameLoopRunner` wires it up: `Poll()` above the `Paused`
  early-out, per-turn `RecordTurnHash` taken BEFORE `Advance`, host-owned speed
  multiplier, **only the host constructs AIs** (their commands travel the wire
  inside the host's input block rather than being re-derived per peer), and a
  desync halts the match and writes a `desync-<stamp>.txt` next to the replay
  containing the hash ring, so the dump says *which turn* diverged.
  Verified in the editor: all eight assemblies compile and a real UDP socket
  binds, polls and disposes.

- **Phase 4 (part 2) DONE — the lobby.** 289/289. `LobbyPayload` (map, host-picked
  seed, turn params, per-seat controller/race/team/tier/name) is the wire shape;
  `LobbyHost`/`LobbyClient` run the negotiation, both pure and loopback-tested.
  Menu: `panel-lan` (browser + join-by-IP + firewall hint) and `panel-lobby`
  (roster) in `MainMenu.uxml`, driven by `MainMenuController.Lan.cs` — the class
  is now `partial` since the LAN half is a different job and owns a socket.
  **The host finally picks a seed** (`Random.Range`); every skirmish had run 42.
  Empty seats stay computer players, so an unfilled lobby is still a full match.

  **The handshake is deliberately TWO-PHASE, and this was a real bug found while
  wiring it:** a joiner cannot hash the host's map before being told which map it
  is, so a single-shot `BuildIdentity` check rejected *every* client with
  `MapMismatch`. Now `JoinRequest` carries only protocol + `SimVersion`
  (`CompareVersionsTo`), the host seats the client and sends the roster, the
  client hashes its own copy of that map and sends `IdentityConfirm`, and a
  mismatch there takes the seat back with a reason. Both paths are tested.

- **Phase 4 (part 3) DONE — pause, observers, net readout.** 293/293.
  **The pause design problem is fixed properly.** `TurnLockstepDriver` no longer
  derives the turn number from the sim tick: it owns `_turn` and `_tickInTurn`.
  While paused the turn clock keeps running and the tick clock does not, so the
  two genuinely diverge — and that is exactly what lets a `Resume` travel, which
  the tick-derived design could never have done. A paused turn is consumed, its
  non-pause commands dropped (identically on every peer, from the same committed
  bundle), and no sim tick executes. Pausing slots are a SET, so two players
  pausing at once cannot cancel each other; `ReleasePause(slot)` exists so a peer
  vanishing mid-pause cannot freeze the match forever. Four tests cover it.
  `GameLoopRunner.SetPaused` now emits a `Pause`/`Resume` command in a networked
  match instead of stopping its own clock. New `ISimHost.CanPauseLocally` — the
  **victory screen no longer pauses in MP**, so a defeated player keeps
  simulating as an observer and keeps feeding the turn schedule. New
  `ISimHost.NetStatusLine` renders seat/turn/confirmed-turn/delay/state on the
  debug overlay (a string, so the view still knows nothing about the net layer).
  **Note:** `TryGetTickCommands`'s `tick` argument is now advisory for this
  driver — it tracks its own turn position. The local and replay drivers still
  use it.

  **Not done:** never played on real hardware. Everything is proven in-process;
  the socket is proven only as far as binding, polling and disposing. Two builds
  on one box is the next real test, and the first place the firewall prompt and
  the discovery beacon either work or don't.

- **Phase 5 DONE — `SimSerializer`, and the Save button finally works.** 301/301.
  `Sim/Core/SimSerializer.cs`, magic `"CWSV"` v1, self-contained: rules, terrain
  planes and the tile layer all travel with it, so a save survives the player
  moving or uninstalling Warcraft II (most map paths resolve into that install).
  RLE'd grids keep a 2000-tick save well under 400 KB.
  Written because they are authoritative, not derivable: **all** unit slots up to
  `HighestUnitIndex` (a dead slot's `Gen` decides the next spawn's `UnitId`), the
  free-slot recycle stack, **both occupancy layers** (a rebuild cannot reproduce
  them — `Occupy` overwrites while `Vacate` only clears its own id), path
  contents, and `TerrainMap._shore` (gates `IsBeachable`, not derivable from
  passability). Rebuilt on load: clearance/regions, `Visible`/`Detected` via one
  `TickFog`, the pathfinder, and the running checksums via new
  `GameState.ReseedChecksums()` — a bulk load bypasses the funnels, so without it
  the first desync compare after a load would fail against peers who built the
  same state incrementally. The RNG stream is restored, never re-derived:
  `NextUInt(bound)` uses rejection sampling so draw counts are data-dependent.
  A save from a different `SimVersion` is refused rather than loaded.
  **The strong test is `ALoadedSnapshot_KeepsMatchingAsBothRunOn`**: live and
  loaded sims advance independently for 1200 ticks and stay bit-identical — which
  is what reconnect actually needs. Others compare occupancy, paths and terrain
  planes *directly*, since a hash-only check would pass while they were wrong.
  Wiring: `ISimHost.SaveGame`, `MatchConfig.savePath` (set → `BuildSim` restores
  instead of calling `Setup`), and `PauseMenuScreen`'s Save button enabled after
  two milestones disabled. Disabled in multiplayer: a save is one peer's private
  copy and reloading it would drop them out of the shared turn schedule.
  **Gotcha worth remembering:** the serializer's accessors are `internal`, and
  the standalone harness compiles Sim + tests into ONE assembly, so it never
  needed `InternalsVisibleTo` — the editor did. Added `Sim/AssemblyInfo.cs`.
  **Still to do:** a Load Game entry in the main menu (`savePath` works; nothing
  sets it yet).

- **Phase 6 STARTED — a drop no longer freezes the match.** 303/303.
  `TurnRelay.SubstituteSlot(slot)` lets the host speak for a seat whose peer
  vanished, and immediately releases any turn that was blocked on it alone —
  which is what actually un-sticks the match. `HostTurnExchange` calls it the
  moment the transport reports a disconnect. Without this a lockstep turn can
  never complete and **every** remaining player is frozen indefinitely. The
  abandoned player is not deleted: their units stay on the map, ownable and
  targetable (tested); only the source of their input changes.
  `SubmitSubstituteInput` is the hook the AI takeover will feed.

  **STILL TO DO in phase 6:**
  1. **AI takeover.** `SubstituteSlot` currently supplies *empty* input, so a
     dropped player goes idle rather than fighting on. Needs an `AiPlayer`
     constructed for the seat, plus **`AiPlayer.ReconcileFromState()`** — a cold
     AI starts with an empty pending-build ledger, so `EffectiveGold` reports the
     full treasury while that player's peasants are mid-walk with `Order == Build`
     (cost is deducted on arrival), and it over-orders for 10-25 s. Rebuild
     `_pending` by scanning own units with `Order == Build && BuildType != 0 &&
     !Hidden`. Also `_waveActive`, `_sleepUntilTick`, `_maxBuildingsSeen`,
     `_blacklistedSites`, `_skippedGoals`. This pays twice — a loaded save needs
     exactly the same reconciliation.
  2. **The ~10 s grace and "Waiting for <player>…" overlay.** Substitution is
     currently immediate on transport disconnect; the decision should come from
     our own turn-starvation timer, since UTP's own timeout is 30 s.
  3. **Reconnect.** `SimSerializer` (phase 5) provides the snapshot; what remains
     is the chunked transfer (`SnapshotChunk`, ~1200 B payloads, deflated, paced
     against `NetworkSendQueueFull`) and a handshake carrying the **driver** state
     a snapshot does not hold: current turn, input delay, the pause set, the
     substituted-slot mask and the hash ring.

  **LAN is no longer the target — these three items are now M11 phase 0.**
  See "M11 — online relay server" below: LAN is dropped in favor of online play
  through a self-hosted relay, but items 1-3 above are needed by online play
  just the same (a dropped seat online still needs AI takeover, a grace
  overlay, and reconnect), so they're being finished under the M11 umbrella
  rather than as a standalone M10 close-out. Load Game menu entry is *not*
  pulled forward (single-player only, orthogonal) — still just backlog.

## Done, continued
**M9.5 — Scriptable, tiered AI (rework of M9). COMPLETE, playtested.** Plan:
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
  resolves (tactics don't deadlock). Phase F still to do.

- **Phase E DONE (green: 286/286 EditMode; 216/216 standalone).**
  `Ai/AiPlayer.Expand.cs` adds scouting and expansion, gated (Smart scouts; God
  scouts + expands), so Dumb/Normal are untouched. **Expansion**: when workers
  saturate one mine (≥12) or the home mine is nearly tapped, build a 2nd Town Hall
  by the nearest *untapped* mine (site-searched around the mine, so the mine-lane
  keep-out sets it a few tiles clear). It stops at "second hall by a fresh mine"
  rather than a full multi-base worker rebalance: the economy manager keys its
  mine off the anchor, so when the home mine dries up it already routes workers to
  the next-nearest live mine — the one the new hall sits beside — which the sim's
  depot search picks as their drop-off. That kills the mined-out-map stalemate
  without the risky per-base split (the fuller rebalance is noted for later).
  **Scouting**: one early worker Move toward the enemy, gated on a worker surplus
  (≥5) so the (cosmetic — the AI cheats fog) scout never robs a thin economy; with
  the gate a Smart AI still first-musters ~t15k, barely behind Normal's ~t14k.

- **Phase F DONE (green: 290/290 EditMode; standalone incl. tier soak).**
  Skill ordering PROVEN by `AiDifficultyTests` (tier + handicaps composed exactly
  as the app wires them): Normal out-develops Dumb at a checkpoint (cadence-only
  pair, so a unit-count proxy is fair), and **Smart beats Dumb, God beats Normal,
  God beats Dumb** to an outright win (the competence tiers trade units, so they
  must be scored by winning, not a snapshot). Near-equal Normal-vs-Dumb stalemates
  past 80k — the known symmetric-mirror slowness — hence the checkpoint there.
  Wiring: `SlotConfig` gained `aiStrategy`/`aiTier`; `MatchConfig.ToMatchSetup`
  bakes the tier's handicap knobs into the Computer slot's hashed `PlayerState`
  (0 for Dumb/Normal/Smart, so default configs are unchanged); `GameLoopRunner.
  CreateAis` resolves the strategy (`AiStrategyLibrary`) and tier and hands both to
  the `AiPlayer`. `AiStrategyLibrary` (App) lists built-ins + player `*.ai.txt`
  from `persistentDataPath/Ai/` and parses them (a broken mod falls back to the
  default, never bricks the lobby). The skirmish rows gained per-Computer-slot
  strategy and difficulty cycle-buttons (`MainMenuController`; rows are built in
  C#, so no UXML change). **Manual playtest done.**

- **Post-phase rework** (feec72f): the linear phase-script executor was replaced
  by a data-driven utility + influence stack. See the `ai-utility-architecture`
  note for the cadence/tier tuning gotchas.

**M9 — scripted AI opponent, superseded by M9.5 above.** Plan:
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

**M8 — all phases landed and playtested.** 234/234 EditMode. Plan:
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
anywhere in UDTA); the mapped entries were verified in play during the playtest
pass, but unmapped types still fall back to the initials box. Portraits and the
WC2 HUD skin (`ThemeWc2.tss`) are not started. `PauseMenuScreen`'s Save button is
still disabled — there is no `SimSerializer`, and that is M10 reconnect work.

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
- Sim system order: commands → production → critters → movement → combat → harvest →
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
**M13 — lobby/matchmaking polish, all 5 phases landed** (2026-07-31). Plan:
`C:\Users\mattc\.claude\plans\snoopy-purring-stream.md`. Closes six gaps the
game-creation flow never had: a host-side map picker, a minimap thumbnail
preview, a Game Type (FFA/Teams) selector, AI auto-assign + manual override
for Computer seats, and a new rating-read path (Glicko-2 was write-only since
M11) feeding a ladder-rank display and a click-to-inspect player popup.

- **Phase 1 — map picker.** `MainMenuController.Lan.cs`/`.Online.cs` gained a
  shared `StepHostMap`/`RefreshHostMapLabel` (mirrors the skirmish panel's
  `Step()`), wired to new prev/next buttons in `panel-lan`/`panel-online`.
  `BuildHostPayload` already read `_lanMapSel` — it just had no UI to change
  it before. Zero wire changes (`LobbyPayload.MapPath` already flows through).
- **Phase 2 — minimap thumbnail.** New `MapThumbnail.Bake(PudFile,
  IMinimapPalette, maxDimension)` (`Assets/Scripts/App/MapThumbnail.cs`):
  reads straight from `PudFile.Tiles`/`Width`/`Height`, no running `GameSim`
  needed, reusing `RuntimeTileCatalog`'s existing `IMinimapPalette` and
  `MinimapView.BakeTerrain`'s row-flip convention. A per-`PudEra`
  `RuntimeTileCatalog` cache avoids re-decoding a tileset on every arrow-key
  step. Shown in the map picker, `panel-lobby`'s `lobby-map` area, and
  (best-effort, filename-matched — `RoomSummary` carries no map hash) each
  online room-browser row. `MapThumbnailTests.cs` (3 tests, EditMode) pins
  the row-flip pixel math with a fake palette.
- **Phase 3 — Game Type (FFA/Teams).** New `LobbyGameType` enum +
  `LobbyPayload.GameType` byte (wire choke point: `Write`/`Read`), new
  `LobbyHost.SetGameType` — Ffa forces every non-Closed seat to a unique
  team (a real reset, not just a label), Teams leaves assignments alone. A
  host-only dropdown in the lobby roster; the existing per-seat Team
  dropdown is now only shown once the host has switched to Teams. `Craftwar.
  Sim`'s team handling is untouched — this is a UX affordance over grouping
  that already fully worked.
- **Phase 4 — AI auto-assign + manual override.** `LobbySlot` gained
  `Strategy` (string; `""` is the same "use default land-attack profile"
  sentinel `AiProfileLibrary.Resolve` already treated identically).
  `LobbyHost.SetSeatStatus` now auto-picks a random tier + the default
  strategy the moment a seat is freshly flipped to Computer — never leaves
  it unset — and new `CycleSeatTier`/`SetSeatStrategy` let the host cycle
  either afterward, mirroring skirmish's `StratBtn`/`DiffBtn`. **Gotcha
  hit immediately**: `SimPurityTests.NetSources_StayEngineFreeAndDeterministic`
  bans `float`/`double`/`new Random()`/wall-clock time across the ENTIRE
  `Assets/Scripts/Net` folder, not just `Craftwar.Sim` — `LobbySession.cs`'s
  first draft used `System.Random`, and `RelayProtocol.cs`'s first draft
  used `double` for ratings. Fixed: the tier pick uses `Guid.NewGuid().
  GetHashCode()` instead of `System.Random`; ratings travel the wire as a
  rounded `int`, with `double` confined to `Craftwar.NetServer` (a separate,
  unscanned project) and `Craftwar.App` (also unscanned). `ToMatchConfig`'s
  stale "the lobby never offers a strategy picker" comment is gone — it now
  reads `slot.Strategy` for real. **Playtest checkpoint, not yet run**: per
  the M11 playtest-fix note above, whether a solo-hosted match's AI actually
  ticks through the real lockstep driver (not just `CreateAis()` running) was
  flagged unverified; this phase is exactly the scenario that would surface
  it — host alone online/LAN, close every seat but one Computer, Start, watch
  `NetStatusLine` (F3/backquote) for real progress.
- **Phase 5 — ladder rank + inspect popup.** New `RatingService.
  TryGetRating` (server, read-only counterpart to `ReportResult`), new
  `ControlMessageKind.GetRating`/`GetRatingResult` wire pair, and
  `RoomSummary` gained `HostRating`/`HostGamesPlayed`/`HostRatingKnown` —
  batched into `ListRoomsResult` server-side rather than one `GetRating`
  round trip per visible room (would have multiplied `OnlineAccountClient`'s
  already-accepted "synchronous, blocking" gap). `GetRating` itself is
  reserved for the lobby roster and the click-to-inspect popup, both riding
  the already-open, non-blocking `RelayPeerSocket`. New `Assets/Scripts/App/
  LadderRank.cs`: a race-agnostic 6-tier table (Peasant/Grunt/Knight/
  Champion/Warlord/Grand Marshal, ~200-point Glicko-2 bands from 1200) plus
  an "Unranked" gate below 5 rated games (RD is still wide at that point) —
  deliberately separate from `VictoryScreen`'s unrelated cosmetic per-race
  `HumanRanks`/`OrcRanks`/`RankScore` end-of-match title, to avoid confusing
  the two concepts. Breakpoints/wording are invented, not sourced from any
  real Battle.net document — cheap to retune. Inspect popup is a simple
  centered modal built in code (`MainMenuController.ShowInspectPopup`), not
  an anchored tooltip. `ControlProtocol.CurrentVersion` bumped 1→2 alongside
  `BuildIdentity.CurrentProtocolVersion` (also bumped, for phases 3-4's
  `LobbyPayload`/`LobbySlot` changes) — both wire-format bumps landed
  together this session, so one bump each covers the whole diff rather than
  churning the constant twice.
  **Verified for real, not just compiled**: `Craftwar.NetServer.Tests`
  81/81 (was 52; new `RatingServiceTests` TryGetRating cases +
  `RelayIntegrationTests` GetRating-over-a-real-socket and
  ListRoomsResult-carries-a-real-rating cases, all against a real
  `RelayServerHost`). Standalone Sim/Net harness 318/318 (includes the full
  AI-match suite, confirming the wire/purity changes didn't regress
  anything). Unity Editor EditMode, run for real via the connected MCP
  bridge (not just compile-checked): `LobbyTests` 19/19, `MapThumbnailTests`
  3/3, `LadderRankTests` 5/5 (caught one real test bug — 1500 rating lands
  in the Knight band, not Peasant — fixed in the test, not the code),
  `LobbyAiPathTests` 2/2, `GameSimSetupTests` 3/3, `NetMessageTests` 8/8,
  `HostClientTests` 10/10.
  **Not yet done**: the phase-4 playtest checkpoint above (solo-hosted AI
  actually ticking through the real driver), and a manual click-through of
  the new UI (map picker, thumbnails, Game Type toggle, Strategy/Tier
  buttons, rating display, inspect popup) — compile- and unit-verified only,
  same honesty M11/M12 used for their own UI phases.

**M13 two real bugs found by actual playtesting** (same day):

- **MOTD didn't survive a logout.** `ChatChannel` (and everything about it)
  is genuinely ephemeral by original M12 design — no DB table, destroyed the
  moment its last member leaves (`ChannelManager.LeaveInternal`). MOTD had
  been built as just a field on that object, so it vanished the instant
  everyone (including the setter) disconnected and got recreated blank on
  the next join. Fixed with a real `channel_motd` table (`Database.cs`) +
  new `ChannelMotdRepository`, injected into `ChannelManager` as an
  **optional** constructor param (`= null`) so the existing in-memory-only
  unit tests (`new ChannelManager()`) keep working untouched — production
  (`RelayServerHost`) always supplies a real one. `Join` loads the persisted
  MOTD when creating a fresh `ChatChannel`; `TrySetMotd` saves it. Verified
  both as a unit test (`ChannelManagerTests.Motd_Survives...`, destroy +
  recreate the in-memory object, same repo) AND as a real-socket integration
  test reproducing the exact user report (`SocialIntegrationTests.
  Motd_SurvivesEveryoneLoggingOutAndTheDefaultChannelBeingRecreated`: set
  MOTD, dispose the only connection, wait for the server to actually process
  the disconnect, reconnect fresh, confirm the MOTD is still there).
- **Returning from a match dropped the whole online session, not just the
  room.** `MainMenuController` (and everything on it — `_onlineSessionToken`,
  the live `SocialClient` chat connection) is destroyed and recreated on
  every Menu&lt;-&gt;Game scene transition; nothing ever re-established it
  except manually clicking Log In again, so coming back from ANY match
  (not just leaving a room) silently logged the player out of chat/friends
  — "it just shows the window" (the blank login form, credentials
  pre-filled but not submitted). Root-caused by tracing the actual scene-
  load path (`StartMatch`/`GameLoopRunner.QuitToMenu`) rather than guessing.
  Fixed the way `NetSession` already solves the identical problem for the
  game socket: new `OnlineSession` static (survives scene loads by
  definition) carrying the session token + username + the live
  `SocialClient` itself. `ShowOnline()` now adopts an active `OnlineSession`
  on a fresh `MainMenuController` instead of showing the login form;
  since the new instance's cached roster/MOTD/member state starts empty
  even though the connection never actually dropped, it forces a resync by
  rejoining the last-known channel (`OnlineSession.CurrentChannel`, reusing
  `SocialClient.JoinChannel`'s existing "leave whatever channel you're in,
  join this one" behavior rather than a new wire message — a harmless
  leave+immediately-rejoin blip to other members). `OnDestroy()` no longer
  calls `CloseSocialConnection()` (that ran on every scene reload, which was
  the actual bug) — only the online panel's Back button now means "log out"
  for real, and it explicitly clears `_onlineSessionToken`/
  `_onlineLoggedInUsername` too, which — turns out — nothing had ever reset
  before either.
  **Verified**: `Craftwar.NetServer.Tests` 105/105 (103 → 105, the two new
  cases above). Unity Editor EditMode compiles clean (`LobbyTests` 19/19).
  **Not yet done**: an actual play-a-match-and-return click-through — I
  can't drive play mode myself; this needs a real two-scene round trip to
  confirm chat/friends are still live on return.

**M13 lobby UI cleanup + M12 completion** (same day, from further playtest
feedback): two fixes to the M13 UI itself, plus the friends/whispers slice of
M12 that was scoped in the original M12 plan but never built (only chat
channels shipped — see M12 phase 1 above), now added at the user's explicit
request, along with a per-channel MOTD (which M12 never scoped at all).

- **Lobby seat-closing fix, narrowed.** A first pass made ANY Closed seat
  vanish from the roster for everyone including the host — too broad: it
  also killed the host's ability to reopen a seat they close manually
  in-lobby (the original M11 design's whole reason for showing Closed seats
  to the host at all). Fixed with `MainMenuController`'s new
  `_cappedClosedSeats` (local-only, never on the wire): `BuildHostPayload`
  records exactly which seat indices it Closed for the player-count cap;
  `RebuildLobby` hides only those from the host, while a seat the host
  closes afterward via the dropdown still shows and can be reopened, same as
  before this feature existed.
- **"Ugliest UI I've seen" fix**: the lobby seat row and room-browser row
  each had a separate "Info" button bolted on next to the name, plus
  "Seat N: name (You) [rating]" label clutter. Removed both — a player's
  name is now itself the clickable control (`Label.pickingMode = Position` +
  `RegisterCallback<ClickEvent>`, no separate button), and the label text is
  now just `name (You) — rating`. Room browser: the row is thumbnail + name
  (clickable for host info) + map/count + a single "Join" button, not two
  buttons stacked.
- **Friends + presence + whispers** (M12's original scope, now built):
  - **Server**: new `friend_requests`/`friendships` tables (`Database.cs`,
    the latter stored as two rows per friendship — one per direction — so
    "list my friends" is a single indexed lookup). New `FriendsRepository`
    (Db) + `FriendsService` (Protocol, business logic only, no socket
    knowledge — same split as `RatingService`/`AccountService`, takes
    `AccountRepository` directly for username resolution like `RatingService`
    does). **A mutual request completes the friendship immediately**: if B
    already requested A, A requesting B doesn't create a second pending row,
    it just accepts — proven by
    `FriendsServiceTests.SendRequest_WhenTheOtherSideAlreadyAsked_...`.
    Presence is **polled, not pushed** (`PresenceDirectory.IsOnline`, already
    anticipating exactly this in its own doc comment from M12 phase 1) —
    matches the M12 plan's settled presence model; request/accept/remove
    events DO push (`FriendRequestReceived`/`FriendRequestAnswered`/
    `FriendRemoved`), online/offline status itself only updates on the next
    `FriendListRequest` poll (client polls every 5s while connected, plus
    immediately after any push event lands).
  - **Whispers**: not restricted to friends — matches the real Battle.net
    `/w name message` model, where you can whisper anyone by username.
    `Whisper`/`WhisperResult`/`WhisperReceived`; the recipient AND the
    sender both get `WhisperReceived` (one ordering source builds both
    logs, same reasoning as every other chat echo in this file), told apart
    by comparing `fromUsername` to your own.
  - **Client**: `SocialClient.cs` gained `SendFriendRequest`/
    `RespondToFriendRequest`/`RemoveFriend`/`RequestFriendList`/
    `SendWhisper` + matching `TryReceiveXxx` queues, same pattern as the
    existing channel methods. `MainMenuController.Social.cs`: a Friends
    section (add-by-name, incoming requests with Accept/Decline, friends
    with online dot + Whisper/Remove, outgoing shown as "(pending)") using
    the same small-inline-button convention the existing channel Kick
    button already established — deliberately NOT another big styled
    button per the "not a mobile game" UI feedback. Whisper composition
    reuses the existing chat input via `/w name message` (clicking a
    friend's "Whisper" button just pre-fills that prefix) rather than a
    separate compose box.
- **Channel MOTD, per-channel, op-only**: `ChatChannel.Motd` (resets
  naturally when a channel is recreated — no DB table, channels are still
  ephemeral) + `ChannelManager.TrySetMotd` (refuses non-ops).
  `ChannelJoinResult` now carries the channel's current MOTD so a fresh join
  sees it immediately; `ChannelMotdChanged` pushes to members already in the
  channel when the op changes it. Client: a MOTD label always visible, an
  edit row (textfield + "Set MOTD" button) shown only when
  `AmChannelOp` is true.
- **Wire bump**: `ControlProtocol.CurrentVersion` 3→4 (this whole batch:
  MOTD + friends + whispers + `ChannelJoinResult`'s new `motd` field).
  `BuildIdentity.CurrentProtocolVersion` untouched — none of this touches
  `LobbyPayload`/the peer-to-peer lockstep handshake.
  **Verified for real, over real sockets, not just unit-tested**:
  `Craftwar.NetServer.Tests` 103/103 (was 81 before this session's earlier
  M13 work, 96 after the MOTD/friends unit tests, 103 after 7 new real-socket
  `SocialIntegrationTests` covering MOTD set+broadcast+non-op-refusal, the
  full request→receive→accept→presence flow, the mutual-request-is-instant-
  friendship path, remove-notifies-the-other-side, and whisper delivery to
  both parties + the unknown-user failure path). Unity Editor EditMode
  (compile-proof via `LobbyTests`/`NetMessageTests`, both green) confirms the
  whole `MainMenuController.Social.cs` rewrite compiles; the actual UI has
  NOT been manually clicked through yet.

**M13 UI follow-up fixes** (same day, from playtest feedback): the first
pass jammed the host map-picker into `panel-lan`/`panel-online` right next
to the room browser and chat — wrong, hosting needed to be its own screen.
New `panel-host-setup` (shared by LAN/Online, entered via "Host a Game",
exited via Create or Back) now owns the whole pre-creation decision: map,
game name, player count, and Game Type — the FFA/Teams toggle from phase 3
was real but had been buried inside the post-creation lobby roster instead
of shown up front where a host actually expects it (still adjustable in the
lobby afterward too). Minimap previews enlarged 3-4x (host setup 256px,
lobby 220px, room-browser rows 96px, up from 64/80/32).

Two features never existed before this pass, both added properly rather
than bolted on:
- **Room name.** New `LobbyPayload.RoomName` (wire choke point) and
  `RelayProtocol`'s `RoomSummary.RoomName`/`CreateRoom`'s `roomName` param
  (server `Room.RoomName`, `RoomManager.CreateRoom`) — shown in both the LAN
  beacon (`LanGameInfo.GameName`, its own UDP wire addition) and the online
  room browser, falling back to "{host}'s Game" when left blank. Also now
  the lobby's own title once inside.
- **Player count.** A "Players: N" stepper in Host Setup, bounded [2, the
  map's own slot count] (recomputed on every map change via
  `MatchSetup.ControllerFor` over the parsed PUD — same check
  `BuildHostPayload` already used). Reducing it doesn't just hide a control:
  `BuildHostPayload` now Closes the seats beyond the cap outright, and
  online hosting passes that same capped count (`payload.PlayableCount()`)
  as the room's real `RelayPeerSocket.Host` maxPlayers instead of a flat
  `SimConstants.MaxPlayers` — the server's join-cap now matches what the
  host actually chose, not always 8.

**A real latent bug fixed as a side effect**: `BuildHostPayload`'s own
seat-naming always read the LAN name field (`PlayerName()`) even when
hosting ONLINE, so an online host's own roster entry showed whatever was
last typed in the LAN panel instead of their logged-in username. New
`HostDisplayName()` picks the right source per `_hostSetupIsOnline`.

Wire-format bumps for this round: `BuildIdentity.CurrentProtocolVersion`
2→3 (`LobbyPayload.RoomName`), `ControlProtocol.CurrentVersion` 2→3
(`CreateRoom`/`RoomSummary.RoomName`).
**Verified**: `Craftwar.NetServer.Tests` 81/81 (`RoomManagerTests`/
`RelayIntegrationTests` call sites updated for the new `roomName` param).
Standalone Sim/Net harness rebuilt clean; full run in progress. Unity
Editor EditMode via the connected MCP bridge: asset refresh clean (no
compile errors), `LobbyTests` 19/19 (extended with a `RoomName` round-trip
assertion), `NetMessageTests` 8/8, `HostClientTests` 10/10.
**Not yet done**: manual click-through of the reshuffled host-setup flow
(Game Type shown up front, player-count capping, room name display in both
browsers) — UI-only churn, compile- and unit-verified only.

**M12 — Battle.net-style social layer** is the current milestone (started
2026-07-27): chat channels, whispers, friends with presence, and clans/guilds
with tags, built on top of the M11 relay server's accounts/sessions. Purely
social/meta-game — no `Craftwar.Sim` changes, no lockstep interaction, and by
deliberate design zero changes to the game-traffic relay path itself
(`TurnRelay`/`HostTurnExchange`/`ClientTurnExchange`/`TurnLockstepDriver`,
`RelayPeerSocket`/`RoomManager`). Plan: `C:\Users\mattc\.claude\plans\
witty-wibbling-pixel.md` (scope settled with the user via `AskUserQuestion`,
an adversarial review against the real M11 code, and the phase breakdown are
all there). Scope, settled with the user before any code was written: all
four features in v1 (nothing deferred), ephemeral channels (Battle.net model,
no DB table), clans invite-only with 3 ranks (Leader/Officer/Member), presence
is poll-on-demand not proactively pushed, clan tags are a display-only
decoration (never rename the player in lobby/room/match identity), and
channel-scoped kick (no server-wide bans) is in v1.

**The headline mechanism finding, from the adversarial review**: no
connection was ever bound to an account before this milestone.
`AccountService.Login`/`ResumeSession` issued/resolved a session token, but
`ClientConnection` discarded the `accountId` every time, and the two
client-side auth flows ran on disjoint, short-lived connections
(`OnlineAccountClient`) separate from the long-lived room connection
(`RelayPeerSocket`, which never authenticates at all — a room's `hostName` is
just a self-reported string). A social layer needs the opposite: a live
directory of "this open socket belongs to account X", independent of room
membership. Fixed in phase 1 (below) rather than treated as a per-feature
workaround.

- **Phase 1 DONE (server fully verified over real sockets; client-side
  compile-checked, not yet playtested)** — foundation + chat channels.
  - **`ClientConnection` identity binding**: `Login`/`ResumeSession` now
    store `AccountId`/`Username` on the connection instance that receives
    them (previously discarded) and register the account in a new
    `Transport/PresenceDirectory.cs` (`accountId -> connectionId`, alongside
    the existing connectionId-keyed `ConnectionRegistry`). `Remove` is
    conditioned on the connectionId still matching what's registered — a
    short-lived one-shot login connection and a long-lived social connection
    can briefly both hold a registration for the same account, and an
    unconditional remove would let the short-lived one's teardown evict the
    persistent connection's entry out from under it.
  - **`Protocol/ChannelManager.cs`**: ephemeral chat channels (exist from
    first join to last leave, no DB table), one channel per account at a
    time — joining a new one leaves whatever one you were in, matching
    original Battle.net. The operator (kick rights) is whoever has been a
    member longest, tracked via an explicit join-order `List<long>` rather
    than `Dictionary` iteration order, so migration when the op disconnects
    is deterministic.
  - **Wire additions** (`RelayProtocol.cs`, shared by client and server same
    as the rest of the control-plane format): `ChannelJoin`/
    `ChannelJoinResult`, `ChannelMemberEvent` (push), `ChannelChat`/
    `ChannelChatBroadcast`, `ChannelKick`/`ChannelKickResult`,
    `ChannelKicked` (push, distinct from an ordinary departure — only ever
    raised about your own account). Channel chat carries no sender field —
    unlike room chat, this connection is account-bound, so the server fills
    in the sender rather than trusting a self-reported string.
  - **New client-side `SocialClient.cs`** (`Assets/Scripts/Net/`, pure BCL
    like `RelayPeerSocket`, zero Unity dependency): a persistent connection
    entirely separate from `RelayPeerSocket`/`OnlineAccountClient` by
    design. `Connect(host, port, sessionToken)` resumes the session obtained
    from the existing one-shot login, then auto-joins the default channel
    ("Town Hall") — the join result arrives asynchronously through the same
    `TryReceiveChannelJoinResult` queue as any later join, no special
    first-frame case.
  - **Client UI**: new `MainMenuController.Social.cs` partial (same pattern
    as `.Lan.cs`/`.Online.cs`) + an `online-social` section in
    `MainMenu.uxml` (member chips, chat log, input, a Kick button next to
    members when you're the op) — visible whenever the online panel is,
    independent of whether a room is being hosted/browsed/joined. Wired with
    four small, additive edits to already-existing files (one call site
    each): `MainMenuController.cs.Start()` gains `InitSocial(root)`;
    `.Online.cs`'s `Authenticate()` opens the social connection right after
    login, its `Update()` drains it, its `ShowOnlineSection`/`online-back`
    show/tear it down; `.Lan.cs`'s `OnDestroy()` disposes it. `RelayPeerSocket`,
    `OnlineAccountClient`, and the room/lobby code paths were not touched.
  - **Verified**: 75/75 `Craftwar.NetServer.Tests` (was 52) — 17 new
    pure-logic `ChannelManagerTests` (op assignment/migration, one-channel-
    at-a-time, kick permission checks, name validation) and 6 new real-socket
    `SocialIntegrationTests` (real `RelayServerHost`, real `SocialClient`s
    over real TCP+TLS: auto-join, a second joiner sees the roster and the
    first member is notified, chat reaches everyone including the sender,
    switching channels announces the departure to the old one, the operator
    can kick and the kicked account gets the distinct `ChannelKicked` push,
    a non-operator's kick is refused). `dotnet build`/`dotnet test` both run
    clean from a cold process (a stale `Craftwar.NetServer.exe` from an
    earlier session had to be stopped first — killed with the user's
    explicit go-ahead). Unity-side (`SocialClient.cs`'s Unity-agnostic
    compile, `MainMenuController.Social.cs`, `MainMenu.uxml`) compile-checked
    via the connected MCP editor instance (`assets-refresh` + `console-get-
    logs`, no errors) — not yet manually playtested.
  - **Known gaps, explicitly not built yet**: whispers, friends, clans, and
    channel-scoped kick's clan-aware permission story are phases 2-4, not
    this one. `PresenceStatus` (Online vs InGame) does not exist yet — it's
    phase 3's job, the first actual consumer. A manual two-client playtest
    (join, chat, switch channels, kick) has not been run this session.

**M11 — online relay server**, superseded by M12 above for social features;
its accounts/rooms/relay/ladder machinery is exactly what M12 builds on top
of, unmodified. Was the milestone before this one (started 2026-07-26).
LAN was dropped as a target (nobody would use it); M10's transport-agnostic
net stack (turn scheduling, desync detection, lobby negotiation, pause/
observers, `SimSerializer`) carries over almost entirely, sitting behind a
new self-hosted relay transport instead of raw LAN UDP. Plan:
`C:\Users\mattc\.claude\plans\optimized-foraging-hearth.md` (decisions
settled with the user, an adversarial review against the real `Net` code,
and the phase breakdown are all there). Only `UtpPeerSocket.cs`/
`LanDiscovery.cs`/panel-lan are LAN-specific; they stay in place,
unmaintained, as an offline two-instance test path — not deleted.

Headline decisions: one connected player stays the elected "host" (runs
`LobbyHost`/`HostTurnExchange`/`TurnRelay`, is the authoritative `GameSim`)
exactly as LAN does today; the server is a dumb byte-relay for game traffic
plus a real accounts/matchmaking/chat/ladder layer (SQLite, Glicko-2, TCP+TLS,
username/password auth). M10 phase 6's remaining items (AI takeover, drop
grace overlay, reconnect) are M11 phase 0 — pulled forward because online
play needs exactly the same things. Lobby seats also gain host-controlled
Closed/Open/Computer/Human state (empty seats no longer auto-become AI) and
a per-seat team selector (up to 8 teams for 8 players — the Sim side already
supports this, only the lobby UI was missing).

- **Phase 0.1 DONE — `AiPlayer.ReconcileFromState()`.** Rebuilds `_pending`
  (scanning own units with `Order==Build && BuildType!=0 && !Hidden`, mapping
  `OrderX/OrderY` → site, `Tick` as issue time so the in-flight-grace/timeout
  clocks get a full window rather than inheriting an unknown true issue tick)
  plus `_maxBuildingsSeen`/wave-pacing fields, so a cold AI attached to a
  mid-game seat doesn't over-report its treasury. **Turned out the "duplicate
  build order" framing in the original phase-6 note was wrong**: `GameSim`
  creates the under-construction building entity as soon as an order is
  accepted, so `OwnedForRole`'s `CountAlive(includeUnderConstruction:true)`
  already self-heals that from live state regardless of the AI's own ledger —
  the real gap was purely the treasury double-count. New `AiReconcileTests.cs`
  (2 tests) proves the ledger rebuild directly via an `internal PendingCount`
  accessor rather than an indirect gameplay symptom. 71/71 AI tests green
  (standalone harness).
- **Phase 0.2 DONE — starvation-based drop detection + grace timer + AI
  takeover wiring.** New `TurnRelay.TryGetOldestBlockedTurn` (tested) lets the
  app layer notice a stalled slot from turn behaviour alone, instead of
  waiting on the transport's own disconnect callback (UTP's is ~30s). New
  `HostTurnExchange` passthroughs: `TryGetOldestBlockedTurn`,
  `SubstitutePeer`, `SubmitSubstituteInput`. **The existing immediate-
  substitution-on-confirmed-disconnect path is UNCHANGED** (still fires
  instantly, still passes the old `HostClientTests`) — the new starvation
  path is an earlier, additional trigger for the same `SubstituteSlot`, not a
  replacement. Two new `HostClientTests` prove the mechanism: a stall is
  detected before any transport disconnect event, and a substitute's real
  commands (submitted for a turn that hasn't frozen yet) land in the
  committed bundle instead of `TryFreeze`'s auto-filled empty — this is the
  actual race the AI takeover depends on, since `SubstituteSlot(slot,true)`
  immediately empties whatever turn was already stuck; only turns not yet
  pending at that moment can carry real orders.
  `GameLoopRunner.DropGrace.cs` (new partial; `GameLoopRunner` is now
  `partial`): 10s real-time grace per stalled slot (`Time.unscaledDeltaTime`,
  since this is app-layer bookkeeping, not hashed sim state — `Craftwar.Net`
  itself stays UnityEngine-free), constructs+`ReconcileFromState`s an
  `AiPlayer` (Normal tier, default profile — no lobby config exists for a
  seat that used to be human) once grace expires or the transport confirms
  the drop, and feeds its `Think()` output through `SubmitSubstituteInput`
  each tick it produces something (targeting `_net.CurrentTurn + 1` — a
  substituted seat's input has no reason to carry the human input-delay,
  since there's no network round trip for the host's own on-behalf AI).
  `NetStatusLine` (existing debug overlay, F3/backquote) extended to show
  waiting/substituted seats. **Not done: a proper player-facing HUD overlay
  widget** (UXML) — this only reuses the existing debug string; a polished
  always-visible banner is separate UI work.
  **Verification gap:** the App-layer half (`GameLoopRunner.*.cs`) is
  UNVERIFIED — the standalone harness only compiles `Sim`/`Net`/`Tests`
  (UnityEngine-free), so this needs an editor compile check and a real
  two-instance drop/takeover playtest before trusting it in play. The Net-
  layer mechanism it depends on (42/42 tests: `HostClientTests`,
  `AiReconcileTests`, `LobbyTests`, `TurnLockstepTests`, `NetMessageTests`)
  is proven.
- **Phase 0.3 DONE — the rejoin protocol.** New wire messages
  (`RejoinRequest`/`RejoinReject`/`RejoinAccept`/`SnapshotChunk`, all in
  `NetMessages.cs`), `SnapshotTransfer.cs` (deflate + ~1200B chunking +
  an out-of-order `Reassembler`), `HostTurnExchange.AcceptRejoin`/
  `RejectRejoin`/`RejoinRequested`, client-side `ReconnectClient.cs` (the
  mid-match analogue of `LobbyClient` — no `LobbyHost` exists once the match
  has started), and a `TurnLockstepDriver` resume-at-a-turn constructor
  overload (`startTurn`, `pausingSlots` — pause state is driver-only, a
  snapshot can never carry it).
  **The headline test (`ReconnectTests.cs`) caught a real protocol bug, not
  a test artifact.** Input delay means a turn's commit can run up to
  `delay - 1` turns AHEAD of when the sim that produced it has actually
  executed it (the host's own contribution for turn T publishes while still
  EXECUTING turn T-delay+1). So by the time a rejoin is accepted, turns
  between the snapshot's own tick and the relay's true `HighestCommittedTurn`
  may already be decided AND already broadcast — and broadcast is
  fire-and-forget to whoever was connected at the time, so a peer that
  connects after the fact never receives it via the normal path and stalls
  forever. Fix: `AcceptRejoin` unicasts whatever's already committed in that
  gap directly to the rejoining peer; `ReconnectClient` captures those
  (`TurnCommit` arrives while it, not yet a `ClientTurnExchange`, is still
  the one polling that connection) and the caller seeds them into the new
  `ClientTurnExchange` via `SeedCommit`. Proven in one test: warm-up →
  client drops → host runs 60 ticks alone → fresh connection rejoins →
  receives snapshot + backfill → resumes → both peers stay bit-identical for
  400 more ticks, the rejoined peer actually issuing orders, not idling.
  **Scope note:** this is the reusable Net-layer protocol only (transport-
  agnostic, will be reused directly by the relay server) — the App-layer
  "offer to reconnect" UX is deliberately not built here. LAN has no
  discovery mechanism to reconnect *to* after a drop (a fresh UTP connection
  has no way to find the game it left); that only becomes meaningful once
  the relay server tracks room/session identity, which is M11 phase 4's job.
- **Lobby seat control & teams DONE.** `LobbySlot.Controller`(byte)+`.Human`
  (bool) replaced by a single `LobbySeatStatus : byte {Closed,Open,Computer,
  Human}` (`LobbyProtocol.cs`) — a genuine pre-game-only fourth state Sim's
  own `Controller` (None/Human/Computer) can never have, since a running
  match always has every seat resolved. `LobbyPayload` gained
  `FirstOpenSeat()`/`HasOpenSeats()`; `LobbyHost` gained `SetSeatStatus`
  (refuses to override an occupied Human seat — only that player leaving
  changes it), `SetSeatTeam`, and `CanStart()`; `StartMatch()` now returns
  `bool` and refuses while any seat is Open. A seat that empties (leaves, or
  fails IdentityConfirm) now reverts to **Open**, not Computer — AI presence
  is only ever a deliberate `SetSeatStatus` call, never a side effect of
  someone leaving. `MainMenuController.Lan.cs`: `BuildHostPayload` seats the
  host Human and leaves every other playable seat Open (was: auto-Computer);
  the lobby roster grew host-only interactive controls (a status-cycle
  button, a team-cycle button — built as plain UI Toolkit elements in code,
  since `lobby-slots` was already an empty container the whole roster is
  built into at runtime, same mechanism as before this change, no UXML
  edited); Start is disabled while `!_lobbyHost.CanStart()`. Team grouping
  was already fully supported by the Sim (`PlayerState.Team`, honored by
  `GameSim.Combat.cs` and `IVictoryEvaluator.cs` — FFA is just the default of
  one team per seat) — only the lobby ever lacked a way to set it to anything
  else. 12/12 `LobbyTests` (rewritten for the new API) + the rest of the
  standalone suite green.
  **Verification gap:** same as phase 0.2 — the `MainMenuController.Lan.cs`
  half (including the new UI rows/buttons) is UnityEngine-referencing and
  cannot compile-check outside the editor this session; needs a compile
  check and a lobby playtest (cycle a seat through Closed/Open/Computer,
  regroup teams, confirm Start stays disabled with an Open seat present).

- **Phase 1 DONE — `Craftwar.NetServer` skeleton, fully verified (unlike the
  App-layer phases above, this needed no Unity editor at all).** New
  `Server/` folder at the repo root (sibling to `Assets/`, own
  `Craftwar.NetServer.Solution.slnx`) — a plain `dotnet` console app, NOT a
  Unity asset; `.gitignore` grew a targeted exception to the blanket
  `*.csproj`/`*.sln` rule (that rule exists for Unity's auto-generated
  per-assembly projects, this one is hand-written). References
  `Assets/Scripts/Sim/**` + `Assets/Scripts/Net/**` by source (same
  `<Compile Include>` pattern as the standalone test harness) — gets
  `ByteWriter`/`ByteReader`/`BuildIdentity` for free, never calls
  `GameSim.Advance`: this process relays bytes, it does not simulate.
  - **Auth**: `PasswordHasher` (PBKDF2-HMACSHA256, built into .NET — no
    Argon2/BCrypt package dependency, self-describing encoded form so the
    work factor can be raised later without invalidating old hashes).
  - **DB**: SQLite (`Microsoft.Data.Sqlite`, pinned past
    `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11's GHSA-2m69-gcr7-jv3q advisory to
    2.1.12) — accounts/sessions/ratings/match_history tables, the latter two
    unused until phase 5.
  - **Control-plane wire format** (`ControlProtocol.cs`): its own
    `ControlMessageKind`/version constant, deliberately NOT
    `Craftwar.Net.NetMessageKind`/`BuildIdentity.ProtocolVersion` — that
    enum's version field is scoped to the peer-to-peer lobby handshake
    (compared only host-vs-joiner), conflating the two would force unrelated
    wire-format bumps together. Reuses `Craftwar.Net.NetMessages.WriteString`/
    `ReadString` directly rather than redeclaring them.
  - **Transport**: `StreamFraming` (4-byte length prefix — TCP has no message
    boundaries, unlike the relay's packet-oriented `IPacketPeer`) over
    `SslStream`; `CertificateProvider` generates and caches a self-signed
    cert on first run (`CertificateRequest` + `X509CertificateLoader`, no
    extra dependency), or loads a real PFX once `ServerConfig.CertPath`
    points at one — the "running locally for now, maybe a real box later"
    requirement is a config change, not a code change.
  - **Business logic split from I/O on purpose**: `AccountService`
    (Register/Login/ResumeSession) knows nothing about sockets;
    `ClientConnection` is pure glue. Same separation `HostTurnExchange`/
    `IPacketPeer` already use in the relay, for the same reason — the logic
    is unit-testable without a socket.
  - **Verification, both levels**: `Server/Craftwar.NetServer.Tests`
    (NUnit, 22/22) covers `PasswordHasher`, `AccountService` against a real
    temp-file SQLite DB, and `ControlProtocol` wire round-trips. Beyond that,
    actually ran the server (`dotnet run`) and a throwaway TCP+TLS smoke-test
    client against it — real self-signed cert, real handshake, real
    Register→duplicate-refused→Login(wrong password refused)→Login→
    ResumeSession→ResumeSession(garbage token refused), all over the wire.
    This is the one M11 phase so far proven end-to-end for real, not just
    unit-tested — no Unity editor involved at any point.
  - **Not yet built**: matchmaking/rooms/relay passthrough (phase 2), the
    online menu UI (phase 3). `Program.cs` only runs the control-plane
    listener today.

- **Phase 2 DONE — rooms + relay passthrough, and the existing LAN protocol
  proven unmodified over a REAL server.** `RoomManager` (server-only,
  `Server/Craftwar.NetServer/Protocol/`): create/join/list/leave, internally
  locked (connections run on independent async tasks and all call the same
  instance — a real bug class this closes, not a hypothetical one).
  **The room creator is always room-peer-id 0** — a server-enforced
  invariant, not a client-side remap table (the original plan's mechanism
  note #1 called for `RelayPeerSocket` to remap ids; enforcing the invariant
  server-side instead is simpler and gives the same guarantee).
  - **A real architecture mistake caught before it even compiled**: the
    control-plane wire format (`ControlProtocol`, `StreamFraming`) was first
    written under `Server/Craftwar.NetServer/`, but a Unity CLIENT
    (`RelayPeerSocket`) needs to speak it too, and Unity's `Craftwar.Net`
    assembly has no reference to the server project (the dependency only
    runs the other way — the server compiles `Assets/Scripts/Net/**` by
    source). Both moved into `Assets/Scripts/Net/`
    (`RelayProtocol.cs`/`StreamFraming.cs`, shared `AccountResult`/
    `RoomJoinFailure` enums extracted out of their server-only homes) —
    picked up automatically by the server's existing source glob, the
    standalone Sim/Net harness, AND the Unity client, same as every other
    shared file in that folder.
  - **`RelayPeerSocket : IPacketPeer`** (`Assets/Scripts/Net/`): pure BCL
    (`TcpClient`/`SslStream`), zero Unity dependency like the plan called
    for. A background reader task unwraps `RoomRelay`/`RoomPeerEvent` frames
    into thread-safe queues `Poll()` drains; a writer task owns the one
    outbound stream so `Send()` never blocks the caller on the network —
    async I/O under the synchronous `IPacketPeer` surface everything above
    it already expects.
  - **A second real bug, caught by the integration test, not by review**:
    the server's `JoinRoom` handler pushed the "here are your new
    roommates" announcement to the joiner's OWN connection *before*
    `HandleAsync` had returned the `JoinRoomResult` reply — since both
    writes go through the same connection, the client read the announcement
    frame first and decoded pure garbage out of it (peer id `16777216`).
    Fixed by having `HandleAsync` return `(response, after)` — the direct
    reply always lands on the wire before any handler-triggered pushes to
    other connections (or back to this one). A reminder that "the server
    never parses the payload" doesn't mean "the server can't get its own
    framing order wrong."
  - **The headline test** (`RelayIntegrationTests.cs`, real `RelayServerHost`
    on loopback — extracted from `Program.cs` so tests and the real
    entrypoint share the same bring-up, not a re-implementation): two
    `RelayPeerSocket`s, a real room created and joined, `TurnRelay`/
    `HostTurnExchange`/`ClientTurnExchange` **completely unmodified**, stay
    bit-identical for 120 ticks with real `Move` commands over real TCP+TLS.
    Not `LoopbackNetwork` — an actual bound socket, actual TLS handshake,
    actual room handshake. 31/31 `Craftwar.NetServer.Tests` (added
    `RoomManagerTests`, this integration test); standalone Sim/Net harness
    unaffected by the file moves.
  - **Scope note**: room-list live data (map/player-count) and the
    reconnect-vouching hooks noted in the original mechanism review are not
    wired yet — `RoomSummary`/`ListRooms` exist and are tested, but nothing
    calls `LobbyHost.Changed` to keep a room's `MapName`/player count fresh
    after creation, and reconnect-through-the-relay is explicitly phase 4.

- **Phase 3 DONE (App-layer, UNVERIFIED — no editor connection this session) —
  online menu UI.** New `MainMenuController.Online.cs` (`MainMenuController`
  is now `partial` across three files: base, `.Lan.cs`, `.Online.cs`),
  `panel-online` added to `MainMenu.uxml` (login section + room browser,
  same "empty container, rows built in C#" pattern as `lobby-slots`/
  `lan-games` already used — no risk of guessing at unfamiliar UXML
  structure). **Deliberately reuses almost the entire LAN lobby machinery
  unchanged**: `LobbyHost`/`LobbyClient`/`EnterLobby`/`RebuildLobby`/
  `BuildHostPayload`/`BuildIdentityFor`/`ToMatchConfig`/`StartHostedMatch`
  already worked off `IPacketPeer`/`LobbyPayload` abstractions with zero
  transport-specific assumptions, so the online flow is: authenticate → list/
  create/join a room on the server instead of a LAN broadcast → hand the
  resulting `RelayPeerSocket` to the exact same `LobbyHost`/`LobbyClient`
  LAN already used. `panel-lobby` (the in-room screen) is now genuinely
  shared, not duplicated.
  `NetSession.Socket` generalized `UtpPeerSocket` → `IPacketPeer` (the one
  other call site, `MainMenuController.Lan.cs`'s `LaunchFrom`, already only
  needed the interface); `LaunchFrom` now hands off whichever transport is
  live. `HideLanPanels()`/`LeaveNetworking()` extended to also cover
  `panel-online`/`_onlineSocket` — both minimal, additive edits to the LAN
  file (not a behavior change to LAN itself).
  New `OnlineAccountClient` (`Assets/Scripts/Net/`, pure BCL like
  `RelayPeerSocket`): short-lived connections for Register/Login/
  ResumeSession/ListRooms — decoupled from the long-lived room `RelayPeerSocket`
  on purpose, since an account logs in once and can create/join many rooms
  or return later with the session token. **Verified for real**: 34/34
  `Craftwar.NetServer.Tests` (added `OnlineAccountClient_*` and
  `Chat_RelaysToEveryRoomMember_IncludingTheSender`, both against a real
  running server over real TCP+TLS) — the account/room-list/chat wire paths
  this UI drives are proven; the UI code that calls them is not.
  Chat is its own control-plane message pair (`ChatMessage`/`ChatBroadcast`
  in `RelayProtocol.cs`), not sent through `IPacketPeer.Send` — that path
  carries NetMessageKind-tagged game-protocol bytes HostTurnExchange/
  ClientTurnExchange parse, and anything else arriving on it would be
  misread. Server broadcasts a chat message back to the sender too, so
  every client's log is built from one ordering source rather than the
  sender echoing its own line locally.
  **Known limitation, flagged in code**: `OnlineAccountClient`/
  `RelayPeerSocket.Host`/`.Join` are synchronous/blocking (matching
  `UtpPeerSocket`'s existing convention) — fine over loopback, but a real
  remote server will hitch the menu's main thread for the round trip with
  no cancel until the OS connect timeout. Worth a background-thread +
  completion-queue pass before a real deployment (phase 6).
  **Verification gap (the real one):** this is the first M11 phase where
  the actual UI-driving code (`MainMenuController.Online.cs`, `panel-online`
  in the UXML, the `NetSession.cs`/LAN-file edits) could not be compiled or
  run at all this session — no Unity editor connection was available via
  MCP. Everything it CALLS is tested for real; the glue itself needs an
  editor compile check and a manual playtest (register → log in → host →
  see the room in a second client's browser → join → chat both ways → Start)
  before being trusted.

- **Phase 5 DONE (fully server-side, fully verified) — Glicko-2 ladder.**
  `Glicko2.cs`: Mark Glickman's Glicko-2 algorithm (glicko.net/glicko/
  glicko2.pdf), implemented step-for-step against his own reference
  pseudocode specifically so it could be checked against his **published
  worked example** — not just "it runs," an exact numeric match. A player
  rated 1500/200/0.06 who plays three specific games lands at
  1464.06/151.52/0.05999 in the paper; the test asserts exactly that
  (0.01/0.01/0.00001 tolerance) and passes. Chosen over plain Elo per the
  earlier decision: the rating-deviation term handles new/returning players
  properly (a low-confidence rating swings hard, a settled one is sturdy —
  covered by its own test) without a separate placement-match phase.
  **Team formats need no special case**: victory in this game is already
  binary per player (`PlayerOutcome.Victorious`/`Defeated`), so every
  player's single Glicko-2 result for the match is (average rating/RD of
  every OTHER player in the match, 1.0 or 0.0) — well-defined for 1v1, 2v2,
  or an 8-player FFA alike, with no separate pairwise-per-team logic needed.
  `RatingRepository`/`RatingService` (SQLite `ratings`/`match_history`,
  already-seeded-at-registration rows from phase 1): **only registered-vs-
  registered games count toward the ladder** — a match with fewer than 2
  resolvable accounts is still recorded in history but rates nobody, so a
  registered player can't grind rating against always-unregistered guests.
  New wire messages `ReportMatchResult`/`ReportMatchResultAck`
  (`RelayProtocol.cs`) and `RelayPeerSocket.ReportMatchResult(...)` — fire-
  and-forget for v1, matching the plan's "host reports once, trusted, no
  anti-cheat on it" decision; no client-side ack handling yet (the wire
  message exists for a future confirmation UI). **Verified for real**:
  12 new tests (7 `Glicko2Tests` including the worked example, 5
  `RatingServiceTests`) plus a `RelayIntegrationTests` case that reports a
  result over an actual `RelayPeerSocket`→server connection and reads the
  updated rating back out of the real SQLite file. 47/47
  `Craftwar.NetServer.Tests`.
  **Not built**: no ladder/profile VIEW in the client (App-layer, would be
  its own UI surface) — the plan's "simple profile/ladder view" is server-
  side-ready but has no menu screen yet.

- **Phase 4 DONE (fully verified, real sockets) — reconnect through the actual
  relay.** The headline risk going in was whether phase 0.3's rejoin protocol
  (`RejoinRequest`/`AcceptRejoin`/snapshot chunks/`ReconnectClient`, proven
  only over `LoopbackNetwork`) and phase 2's `RelayPeerSocket`/server rooms
  would actually compose, given the rejoin protocol addresses the host as a
  hardcoded `ClientTurnExchange.HostPeerId = 0`. They composed with **zero
  production changes to either layer** — the server's own invariant ("room
  creator is always room-peer 0") already IS the assumption the rejoin
  protocol was built on, so a rejoining `RelayPeerSocket.Join(...)` sending a
  `RejoinRequest` to peer 0 lands on the real host's connection with no
  id-remapping needed anywhere.
  New test `RelayIntegrationTests.ReconnectThroughARealServer_
  RejoinedPeerCatchesUpAndStaysBitIdentical`: host + client run 80 ticks over
  a real `Craftwar.NetServer` instance, the client's `RelayPeerSocket` is
  disposed (a real TCP close, not a synthesized event), the host detects the
  drop via a real `RoomPeerEvent` pushed by the server's `LeaveRoomAsync` and
  substitutes the slot, runs 60 more ticks alone, then a **fresh**
  `RelayPeerSocket.Join` (room-peer-id **2**, never a reused 1 — proving the
  protocol's peer-id-continuity independence isn't just untested theory) runs
  the full `ReconnectClient` handshake, and both sims stay bit-identical for
  200 more ticks including the rejoined peer actually issuing new orders.
  **Found and fixed one real concurrency bug this surfaced**
  (`ClientConnection.WriteFrameAsync`, server-only): `using var ssl = new
  SslStream(...)` disposes at the end of `RunAsync`'s `try` block — which
  runs *before* that same method's `finally` removes the connection from
  `ConnectionRegistry`. A dropped connection is therefore still "live" in the
  registry, with an already-disposed stream, for a real window; a concurrent
  peer's broadcast (here: the host's own `BroadcastNewlyFrozenTurns`, mid-fire
  right as the client dropped) can land in that window and throw
  `ObjectDisposedException`, which was going uncaught up through `RunAsync`
  and killing that connection's read loop outright — turning one peer's
  ordinary disconnect into a second, unrelated connection also dying.
  Never hit by any earlier test because none of them disconnected a live
  peer while traffic was still flowing. Fixed by catching
  `ObjectDisposedException`/`IOException` in `WriteFrameAsync` and dropping
  the frame silently — the target's real disconnect is still reported the
  normal way, moments later, via that connection's own `finally`. 48/48
  `Craftwar.NetServer.Tests` (was 47; the standalone Sim/Net harness is
  unaffected — the fix is server-only, not in the shared `Assets/Scripts/Net`
  files).
  **Not built**: the App-layer trigger (`GameLoopRunner`/`MainMenuController`
  actually calling into `ReconnectClient` after a drop, when playing online)
  — this proves the wire protocol composes end-to-end, not the in-game "try
  to reconnect" UI/flow, which is still only exercised by simulating the app
  layer directly in the test, same as phase 0.3's original loopback test did.

- **Phase 6 DONE — deployment hardening.** The plan's own framing
  ("deliberately last since the user is running locally for now") meant this
  is about making the *later* real-deployment step config-only, not actually
  deploying anything now. `ServerConfig` gained `CRAFTWAR_*` environment
  variable support (`ServerConfigTests.cs`, 4 new tests) alongside the
  existing `--flag` args, args winning on conflict — env vars are what both
  packaging paths below actually set (systemd `EnvironmentFile`, Docker
  `--env-file`/`-e`), neither naturally populates argv. The real-cert config
  path (`CRAFTWAR_CERT_PATH` → a real PFX, vs. auto-generate/cache a
  self-signed one) already existed since phase 1's `CertificateProvider` —
  nothing to add there. `Program.cs` now handles `SIGTERM`/`SIGINT`
  (`PosixSignalRegistration`) by disposing `RelayServerHost` — stop
  listening, let in-flight work end on its own terms — instead of relying on
  Docker/systemd's kill-after-timeout to end the process mid-connection.
  **Not verified against a real signal**: this dev machine is Windows, where
  `kill`/`taskkill` don't exercise real POSIX signal delivery the way
  Docker/systemd (both Linux) do — treat this path as unverified until
  checked on an actual Linux deployment.
  New: `Server/Dockerfile` (+ a repo-root `.dockerignore` — without it,
  `docker build` would hash/upload Unity's multi-GB `Library/` before the
  build even starts; also excludes `Assets/GameData/Extracted/` on principle,
  Blizzard-owned data has no business anywhere near an image layer even
  though the server build never touches it) — built and published
  successfully via `dotnet publish` as a smoke test (not run under an actual
  Docker daemon, which was not available in this environment). `Server/
  deploy/craftwar-netserver.service` (systemd unit, `ProtectSystem=strict`
  hardened) + `.env.example`. **A real bug in this hardening surfaced during
  review**: the `.env.example` originally left `CRAFTWAR_CERT_PATH` blank
  (matching local-dev's "auto-generate next to the working directory"
  default), but under the shipped unit's `ProtectSystem=strict`,
  `WorkingDirectory` (`/opt/craftwar-netserver`) is read-only — the service
  would fail to start on first run instead of caching a cert. Fixed to point
  it at the writable `ReadWritePaths` data directory by default;
  `Server/README.md` was updated to match. `Server/README.md` is the runbook: local run, tests, the config
  table, both deployment paths, cert rotation, and an online-safe SQLite
  backup command (`sqlite3 ... ".backup ..."`, not a raw file copy).
  **Verified**: 52/52 `Craftwar.NetServer.Tests` (was 48); a real `dotnet
  publish -c Release` + run of the published binary, confirmed it starts,
  binds, and generates both the cert and the db file exactly as documented.
  **Not done**: an actual Docker build/run (no Docker daemon in this
  environment — the Dockerfile is unverified beyond `dotnet publish`
  succeeding for the same project it publishes) and provisioning a real
  box/domain/cert (out of scope per the user's own "local for now" framing).

**All six M11 phases are now done.** What remains unbuilt, all previously
called out per-phase above rather than new: the App-layer online-reconnect
UI trigger (phase 4), a ladder/profile view (phase 5), server-side match-
result verification (phase 5), client cert pinning (phase 2/6), and an
actual provisioned deployment (phase 6). None of these were "still to do"
surprises — each was flagged as a known gap in its own phase entry above.

**M10 — LAN lockstep**, superseded by M11 above. M8/M9/M9.5 are done and
playtested; their checklists are closed.

M10 scope, as it stood:
- Unity Transport net driver behind the existing `ILockstepDriver` (the local
  driver's canonical command sort is already the shape this needs).
- Turn scheduling with real input delay (turn = 4 ticks today, zero delay).
- Per-turn desync detection. **`ComputeHash` must become a rolling checksum
  first** — it currently walks 2 fog grids x W*H per in-game player (~262 KB on a
  128x128 8-player map), which is free only because tests are its only caller.
- `SimSerializer` for join/reconnect — also what unblocks the pause menu's
  disabled Save button.

## Playtest fix (post-M11, found starting a lobby match with closed seats + a computer)

Reported bug, two claimed symptoms: starting an online/LAN match with some
seats Closed and one seat Computer spawned every map-defined slot's starting
units (not just the human + the AI), and the Computer seat's units never did
anything.

**Root cause 1 (confirmed, fixed): `GameSim.Setup` spawned units for
lobby-closed seats.** The per-unit spawn loop
(`Assets/Scripts/Sim/Core/GameSim.cs`) only skipped a slot when the *map's own*
OWNR byte marked it not-in-game (`MatchSetup.IsInGame`); it never checked the
*lobby's* resolved `Controller`, which `Setup` deliberately forces to
`Controller.None` for a Closed seat while leaving `InGame` true (so a Closed
seat on an 8-player map still got its Town Hall + starting workers). The fix
can't just skip every `Controller.None` slot, though: passive-computer and
both rescue owner kinds are *also* `Controller.None` by design
(`MatchSetup.ControllerFor`) and must keep spawning as scenery (`VictoryTests.
PassiveAndRescueSlots_DoNotBlockVictory` already covered the victory side of
this InGame-but-not-a-participant distinction). The fix reads the PUD's actual
owner byte for the slot: `Controller.None` skips spawning only when that byte
was Human/Computer (i.e. a real seat the lobby closed); passive-computer/
rescue owner bytes still spawn regardless of `Controller`. New
`GameSimSetupTests.cs`: a closed Human/Computer seat spawns nothing, passive/
rescue slots are unaffected, and a fully-resolved match (nothing closed) is
unaffected.

**Root cause 2 (partially confirmed): `MainMenuController.Lan.ToMatchConfig`
never set `SlotConfig.aiType`, unlike the skirmish path
(`MainMenuController.StartSkirmish`, which reads the PUD's real AIPL byte per
slot).** Fixed — `ToMatchConfig` now parses the map and reads
`pud.AiType[p]`, falling back to 0 if the map can't be read. `aiStrategy` was
left alone: the lobby has no strategy picker (`LobbySlot` carries no such
field), so it stays `""`, which `AiProfileLibrary.Resolve` already treats as
the same built-in land-attack profile the skirmish path defaults to — no
functional gap there, and no strategy selector was added (nothing in the
lobby UI to drive one from).

**However, this does not fully explain "the Computer seat's units never did
anything."** A standalone repro (dotnet+NUnitLite harness, see
`sim-standalone-harness` — this needed reconstructing outside
`Craftwar.App`'s `AiProfileLibrary`/`MatchConfig`, since those need UnityEngine
and the Unity Editor's MCP bridge dropped its connection mid-session and did
not reconnect) built a `MatchSetup` exactly the way `ToMatchConfig` +
`ToMatchSetup` would for a Human+Computer lobby, with `aiType=0`/`aiStrategy=
""`/`aiTier=Normal` — the *unfixed* defaults — and constructed the `AiPlayer`
with `GameLoopRunner.CreateAis()`'s exact formula. The AI built up, expanded,
and defeated an idle human in 29,350 ticks: `AiBehaviorMap.FromAiplByte(0)` is
`LandAttack`, not `Passive` (only byte `0x01` maps to `Passive`), and
`AiProfileLibrary.Resolve("")` is the same default profile the skirmish path
names explicitly — both defaults were already behaviorally identical to what
`AiMatchTests.IdleHuman_LosesToAi` already exercises via the 2-arg
`GameSim.Setup` path. New `LobbyAiPathTests.cs` (Unity EditMode, not yet run —
see below) pins this down permanently via the actual `Craftwar.App` types.

**Compile-checked only, not run in the Unity Editor.** The MCP bridge to the
Unity Editor disconnected during the `LobbyAiPathTests` repro (a >120s
tests-run call) and did not reconnect (`list_engine_instances` kept reporting
no connected instance even though the `Unity.exe` processes were still
running) — batch mode wasn't an option either, since the editor was still up.
Root cause 1's fix and its regression tests were fully verified via the
standalone Sim+Net harness (all 4 new tests green, including a version of the
lobby-AI repro built from pure `Craftwar.Sim` types). Root cause 2's
`MainMenuController.Lan.cs` edit and `LobbyAiPathTests.cs` (which use
`Craftwar.App`) are hand-checked against the surrounding code but **not
compiled or run** — run the Unity Test Runner (or a closed-editor batch run)
before trusting them, and this still leaves the original "AI never acts"
report unexplained. If it reproduces again, it is not the two causes above;
worth checking next: whether `GameLoopRunner.CreateAis()` actually runs at all
for a solo-hosted LAN/online match (`_net == null || NetSession.IsHost`), and
whether AI-submitted commands actually reach `Advance` through the real
`ILockstepDriver`/turn-scheduling path for a single-participant hosted game —
neither is modeled by the sim-only harness test, which drives `Advance`
directly every tick with no driver in between.
