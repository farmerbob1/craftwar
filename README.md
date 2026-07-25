# Craftwar

A faithful remake of **Warcraft II: Tides of Darkness / Beyond the Dark Portal**
in Unity 6, built around a deterministic integer-only simulation so that online
lockstep multiplayer (1v1 through 4v4) is possible rather than bolted on later.

Original balance, modern UX. The simulation reproduces the original's rules from
its own data — unit stats, upgrade tables, the damage roll, the diagonal-speed
quirk, forest retiling, harvest cycles — while the presentation layer is free to
be a modern RTS front end.

> **You need your own copy of Warcraft II to play this.**
> No Blizzard assets are included or distributed here. See
> [Getting the game data](#getting-the-game-data).

---

## Status

Playable single-player skirmish against computer opponents on the original melee
maps: full tech tree, economy, naval and air units, fog of war, minimap, control
groups, replays, and a data-driven utility-based AI.

Milestones **M0–M9.5** are complete (scaffold, map pipeline, movement, combat,
economy, tech tree, fog/minimap/sound, naval/air, HUD, AI). LAN lockstep (M10),
online play with team vision (M11) and the campaign track (M13) are still ahead.
`PROGRESS.md` is the running log and is the place to start if you want the
detail.

---

## Getting the game data

Craftwar reads art, sound, music and maps from an existing Warcraft II
installation at runtime. Nothing is copied into the repository and nothing is
redistributed — the files stay where they are.

Supported source: **Warcraft II Remastered** (everything ships loose and
uncompressed under `x86/Data/`, which is what the importer targets).

On first run the menu opens a locator wizard that scans for an install; you can
also point it at a folder by hand. Once found, the path is remembered in
`LocalAssetPaths.json`, which is gitignored precisely so it never travels.

Original `.pud` maps, `.war`/`.mpq` archives and anything decoded out of them are
gitignored as a hard rule. If you are contributing, do not commit Blizzard data.

---

## Building and running

Unity **6000.5.4f1**. Open the project, then:

1. `Craftwar/Setup/Ensure 2D Renderer` — creates and assigns the 2D renderer and
   the Game scene if they are missing.
2. Open `Assets/Scenes/Menu.unity` and press Play.

Pressing Play directly on `Game.unity` also works and loads the map named in the
`GameLoopRunner` inspector — the quicker loop when working on gameplay.

---

## Architecture

The project is split into assemblies with deliberately one-way dependencies. The
simulation is sealed off from Unity entirely, which is what makes determinism
enforceable rather than aspirational.

| Assembly | Role |
|---|---|
| `Craftwar.Sim` | Game logic, PUD loader, pathfinding, PRNG. **No engine references** |
| `Craftwar.View` | Rendering, input, UI, audio. References Sim |
| `Craftwar.Net` | Lockstep driver; Unity Transport later |
| `Craftwar.Import` | Runtime asset extraction from a local install |
| `Craftwar.App` | Bootstrap, match setup, scene flow |
| `Craftwar.EditorTools` | Editor-only: import window, data codegen, project setup |
| `Craftwar.Sim.Tests` | EditMode determinism suite |

### The three rules

1. **`Craftwar.Sim` is deterministic and integer-only.** No `UnityEngine` (the
   asmdef enforces it), no floating point, no `DateTime`, no `System.Random`, no
   iteration over unordered collections. All randomness flows through a single
   PCG32 stream on `GameState`. The world must be reproducible from
   `(map, seed, command log)` — a source-level test suite guards this.
2. **The view is a projection.** MonoBehaviours read simulation state and
   interpolate between ticks; input produces commands, never direct mutation.
3. **Everything runs through the lockstep driver.** Single-player is lockstep
   with zero input delay. The simulation advances on a fixed 50 Hz tick — the
   original game's cycle rate — and command turns are four ticks.

Because of this, every finished match writes a replay that is just the command
log, and replaying it re-derives the match bit for bit.

---

## Tests

The simulation suite must stay green. In the editor, use the Test Runner
(EditMode). Headless, with the editor closed:

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode \
          -testResults results.xml
```

`Craftwar.Sim` and `Craftwar.Net` are pure C# and also compile outside Unity, so
a scratch console project targeting `net10.0` with NUnitLite can run the whole
pure suite in about a second while the editor stays open — much the faster loop
when bisecting a simulation bug.

---

## Accuracy, and where the numbers come from

Behaviour is derived from primary sources rather than tuned by feel:

- **Unit and upgrade stats** are generated from the Battle.net edition's
  `unitdata.dat` / `upgrades.dat` into a committed table, with a round-trip test
  that fails if the table drifts from the source data.
- **Formats and formulas** — the PUD map format, sprite and tileset encodings,
  the damage roll, repair costs, harvest pacing, animation frame layouts, HUD
  icon indices — are taken from format documentation and from the published
  PlayStation-era source tree, which is used strictly as reference for facts and
  mechanics. All code here is written fresh.

Where the original's behaviour and a modern expectation conflict, the original
usually wins, and the departures are written down in `PROGRESS.md` rather than
left as folklore.

---

## Credits and licensing

- **war2tools** (MIT) — the PUD format documentation and decoder logic that the
  C# map/archive readers were ported from. Attribution retained in the source.
- Warcraft II, its data and its artwork are the property of Blizzard
  Entertainment. This project is an unaffiliated, non-commercial reimplementation
  of the *engine* and ships none of that content. You must own the game.
