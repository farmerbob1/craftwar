# Craftwar — Warcraft 2 remake in Unity 6.5

Faithful WC2 remake (original balance + modern UX) targeting online 1v1–4v4
deterministic lockstep. Milestone scope and history: `PROGRESS.md` in the repo
root. (The old `~/.claude/plans/linear-stirring-moler.md` roadmap was overwritten
by a session summary and is gone; `PROGRESS.md` is the surviving record.)

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
| `Craftwar.Import` | GRP/PPL/WAV/PUD decode core, now used only by the Editor-time bake pipeline (`Craftwar/Setup/Import Warcraft II Assets`) and the legacy first-run install-locator wizard in `MainMenuController`. Still compiles into Player builds because of that wizard (no Blizzard *data* ships either way — only the decoder code) |
| `Craftwar.App` | Bootstrap, match setup, scene flow |
| `Craftwar.EditorTools` | Editor-only: import window, data codegen, `ProjectBootstrap.Run` |
| `Craftwar.Sim.Tests` | EditMode determinism suite — must stay green |

## Licensing guardrails (critical)

- **Never commit or distribute Blizzard data**: no .war/.mpq/.pud, nothing from
  `Assets/GameData/Extracted/` (all gitignored).
- **Assets are baked once, at Editor time, from a locally-owned install.**
  `Craftwar/Setup/Import Warcraft II Assets` (`Wc2AssetImporter.Run`) decodes
  every asset class (tilesets, sprites+team-colour masks, sound, music, HUD
  icons, strings, maps) into real Unity assets under
  `Assets/GameData/Extracted/` — gitignored, but a normal part of the local
  Unity project, so Player builds include them like any other asset. Play
  mode and builds never touch a live install or `IAssetSource` again; the
  runtime reads baked `ScriptableObject` tables (`Baked*` classes in
  `Craftwar.App`, e.g. `BakedUnitSpriteBank`, `BakedTileCatalog`) via
  `Resources.Load`. Team colour is applied at draw time by the
  `Craftwar/UnitTeamColor` shader from a baked mask, not pre-baked per player.
  Re-run the importer whenever a bake looks stale or missing.
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
  Craftwar.EditorTools.ProjectBootstrap.Run`) creates/assigns the 2D renderer
  and the Game scene.
- `Craftwar/Setup/Generate Default Stat Tables` (`DataCodegen.Run`) regenerates
  `Sim/Data/Generated/DefaultData.gen.cs` from the BNE dat files; a round-trip
  test fails if the committed table drifts from the source data.
- `Craftwar/Setup/Import Warcraft II Assets` (`Wc2AssetImporter.Run`) needs
  `LocalAssetPaths.json`'s `dataRoot` pointing at a real install's `Data`
  folder. The sprite bake in particular touches ~370 (unit-file × era)
  atlases in one call — run it directly from the Unity Editor menu, not
  scripted over an automation bridge; a single call that size has crashed
  the editor before (no yielding, hundreds of Texture2D allocations).
