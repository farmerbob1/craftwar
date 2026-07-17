# Craftwar — Warcraft 2 remake in Unity 6.5

Faithful WC2 remake (original balance + modern UX) targeting online 1v1–4v4
deterministic lockstep. Full plan: `C:\Users\mattc\.claude\plans\linear-stirring-moler.md`.

## The three hard rules

1. **`Craftwar.Sim` is a sealed, deterministic, integer-only plain-C# library.**
   No UnityEngine (asmdef enforces it), no float/double, no DateTime/Stopwatch,
   no System.Random, no unordered-collection iteration. All randomness through
   `GameState.Rng` (Pcg32). The world must be reproducible from
   `(map, seed, command log)`. `SimPurityTests` guards this at source level.
2. **The view is a projection.** MonoBehaviours read sim state and interpolate;
   input produces `GameCommand`s, never direct state mutation.
3. **Everything runs through the lockstep driver.** Single-player = lockstep
   with zero input delay. Sim advances at a fixed 50 Hz (20 ms) tick — the
   original game's cycle rate; command turns are 4 ticks.

## Assemblies (Assets/Scripts/)

| asmdef | Role |
|---|---|
| `Craftwar.Sim` | Game logic, PUD loader, A*, PRNG. `noEngineReferences: true` |
| `Craftwar.View` | Rendering, input, UI, audio. Refs Sim |
| `Craftwar.Net` | Lockstep driver; Unity Transport later |
| `Craftwar.Import` | Runtime-capable asset extraction (.war/MPQ → local cache). Ships in builds (OpenRA model) |
| `Craftwar.App` | Bootstrap, match setup, scene flow |
| `Craftwar.EditorTools` | Editor-only: import window, data codegen, `ProjectBootstrap.Run` |
| `Craftwar.Sim.Tests` | EditMode determinism suite — must stay green |

## Licensing guardrails (critical)

- **Never commit or distribute Blizzard data**: no .war/.mpq/.pud, nothing from
  `Assets/GameData/Extracted/` (all gitignored). Asset cache goes to
  persistentDataPath at runtime.
- **war2tools** (`C:\Users\mattc\Desktop\Warcraft shit\war2tools-master`) is MIT:
  porting to C# is fine, keep attribution. Its `doc/pud_format.txt` is the PUD spec.
- **War25** (`...\war25-main`) is GPLv3: concepts only, NEVER copy/transcribe code.
- **PSX/DOS original source** (`...\Playstation`): reference for stat values,
  formats and formulas (facts/mechanics) only; all code written fresh.
- Stat ground truth: **BNE** `unitdata.dat`/`upgrades.dat` from
  `C:\Program Files (x86)\Warcraft II Remastered\x86\Data\Files\`; PSX
  `UNITDATA.ARR` is the cross-check/documentation.

## Conventions

- Sim: struct-heavy, arrays over collections, explicit little-endian
  serialization (`ByteWriter`/`ByteReader`), `UnitId{index,gen}` handles.
- Coordinates: 32 px tiles, integer pixel coords, 8 facings (CELL.H model).
- Stored costs are ×10 (`SimConstants.CostStepValue`).
- Damage: `max(0, strength+upgrades − armor) + pierce`, applied as
  `half + rng.Next(half+1)` (50–100%).
- Run EditMode tests after any Sim change; a scenario run twice must hash identical.

## Verification

- Tests: Unity Test Runner (EditMode), or batch:
  `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`
  (requires editor closed).
- `Craftwar/Setup/Ensure 2D Renderer` menu (or `-executeMethod
  Craftwar.EditorTools.ProjectBootstrap.Run`) creates/assigns the 2D renderer.
