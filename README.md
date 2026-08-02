# Craftwar

A faithful remake of **Warcraft II: Tides of Darkness / Beyond the Dark Portal**
in Unity 6, built around a deterministic integer-only simulation so that online
lockstep multiplayer (1v1 through 4v4) is possible rather than bolted on later.

Original balance, modern UX. The simulation reproduces the original's rules from
its own data — unit stats, upgrade tables, the damage roll, the diagonal-speed
quirk, forest retiling, harvest cycles — while the presentation layer is free to
be a modern RTS front end.

> **You need your own copy of Warcraft II to build this from source.**
> No Blizzard assets are included or distributed here — nothing is committed
> to this repository. A built Player, however, needs no install at all: see
> [Getting the game data](#getting-the-game-data).

---

## Status

Playable single-player skirmish against computer opponents on the original melee
maps: full tech tree, economy, naval and air units, Mage Tower/Temple of the
Damned spellcasting (mana, cast range, corpse-based Raise Dead, Blizzard/
Whirlwind/Death and Decay hazards, Runes' five-charge proximity traps), fog of
war, minimap, control groups, replays, and a data-driven utility-based AI. LAN
and online multiplayer both work — host or join a match over LAN discovery, or
through a self-hosted relay server with accounts and room browsing.

Milestones **M0–M14** are complete (scaffold, map pipeline, movement, combat,
economy, tech tree, fog/minimap/sound, naval/air, HUD, AI, LAN lockstep, online
play via a self-hosted relay server, lobby/matchmaking polish, and baking every
Warcraft II asset into the project so Play mode never touches a live install).
Spellcasting has since landed too. The social layer (chat channels,
friends/presence, clans) is the current focus. `PROGRESS.md` is the running
log and is the place to start if you want the detail.

---

## Getting the game data

Art, sound, music, HUD icons, strings and maps are baked **once, at Editor
time**, from an existing Warcraft II installation into real Unity assets —
not streamed from the install at runtime. After that bake, Play mode and any
Player build you make are entirely self-contained; the install is never
touched again and doesn't need to exist on whatever machine runs the build.

Supported source: **Warcraft II Remastered** (everything ships loose and
uncompressed under `x86/Data/`, which is what the importer targets).

To bake the assets:

1. Point `LocalAssetPaths.json` (project root, gitignored, create it if
   missing) at your install's `Data` folder:
   ```json
   { "dataRoot": "C:\\Program Files (x86)\\Warcraft II Remastered\\x86\\Data" }
   ```
2. Run **`Craftwar/Setup/Import Warcraft II Assets`** from the Editor menu.
   It decodes tilesets, unit/building sprites (+ a team-colour mask, recoloured
   at draw time — see the `Craftwar/UnitTeamColor` shader), sound effects,
   music, HUD icons, strings and every shipped map into
   `Assets/GameData/Extracted/`.

That folder is gitignored — nothing it produces is ever committed or
distributed — but it's a normal part of your local Unity project from then on,
so builds include it exactly like any other asset. The sprite bake covers a
few hundred (unit × era) combinations and can take several minutes; run it
directly from the Editor menu rather than scripting it, and watch the console
for its periodic progress lines.

Original `.pud` maps, `.war`/`.mpq` archives and anything decoded out of them
are gitignored as a hard rule. If you are contributing, do not commit Blizzard
data.

---

## Building and running

Unity **6000.5.4f1**. Open the project, then:

1. `Craftwar/Setup/Ensure 2D Renderer` — creates and assigns the 2D renderer and
   the Game scene if they are missing.
2. `Craftwar/Setup/Import Warcraft II Assets` — bakes art/sound/maps from your
   own install; see [Getting the game data](#getting-the-game-data). Skip this
   if `Assets/GameData/Extracted/` is already populated.
3. Open `Assets/Scenes/Menu.unity` and press Play.

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
| `Craftwar.Net` | Lockstep driver; turn relay/exchange, LAN (Unity Transport) and online (relay-server) sockets |
| `Craftwar.Import` | GRP/PPL/WAV/PUD decode core, used by the Editor-time bake pipeline (not at runtime) |
| `Craftwar.App` | Bootstrap, match setup, scene flow, menu/lobby UI |
| `Craftwar.EditorTools` | Editor-only: import window, data codegen, project setup |
| `Craftwar.Sim.Tests` | EditMode determinism suite |

`Server/Craftwar.NetServer` is a separate, plain-.NET console project — the
self-hosted relay server for online play (TLS control plane, accounts, room
browsing, and now a social layer). It has its own test project
(`Craftwar.NetServer.Tests`) and its own runbook, `Server/README.md`. Run it
locally with `dotnet run` from `Server/Craftwar.NetServer`; the game's online
menu points at it by host:port (`127.0.0.1:27015` by default).

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
