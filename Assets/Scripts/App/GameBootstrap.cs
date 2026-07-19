using System.IO;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// M1 bootstrap: resolve local WC2 data, parse a PUD, decode the era
    /// tileset and hand everything to the view. Grows into full match setup
    /// (lobby, lockstep driver, sim) in later milestones.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] TilemapView tilemapView;
        [SerializeField] CameraRig cameraRig;
        [Tooltip("Optional override; otherwise LocalAssetPaths.defaultMap is used.")]
        [SerializeField] string mapOverridePath = "";

        public PudFile CurrentMap { get; private set; }

        GameLoopRunner _runner;
        ITileResolver _tileResolver;
        UIManager _ui;

        void LateUpdate()
        {
            if (_runner == null)
                return;

            if (_runner.PendingTileChanges.Count > 0)
            {
                foreach (var (x, y, tile) in _runner.PendingTileChanges)
                    tilemapView.SetTile(x, y, tile, _tileResolver);
                _runner.PendingTileChanges.Clear();
            }

            if (_runner.PendingSimEvents.Count > 0)
            {
                if (_ui != null)
                    _ui.HandleSimEvents(_runner.PendingSimEvents);
                _runner.PendingSimEvents.Clear();
            }
        }

        void Start()
        {
            var paths = LocalAssetPaths.Load();
            if (paths == null || string.IsNullOrEmpty(paths.maindatWar) || !File.Exists(paths.maindatWar))
            {
                Debug.LogError(
                    "[Craftwar] LocalAssetPaths.json missing or maindatWar not found. " +
                    $"Create {LocalAssetPaths.ProjectRootPath} pointing at your maindat.war.");
                return;
            }

            string mapPath = !string.IsNullOrEmpty(mapOverridePath)
                ? mapOverridePath
                : Path.Combine(paths.mapsDir ?? "", paths.defaultMap ?? "");
            if (!File.Exists(mapPath))
            {
                Debug.LogError($"[Craftwar] Map not found: {mapPath}");
                return;
            }

            var pud = PudFile.Parse(File.ReadAllBytes(mapPath));
            CurrentMap = pud;

            var archive = new War2Archive(File.ReadAllBytes(paths.maindatWar));
            var catalog = RuntimeTileCatalog.Build(archive, pud.Era);

            tilemapView.LoadMap(pud, catalog);
            cameraRig.SetMapBounds(pud.Width, pud.Height);
#if UNITY_EDITOR
            cameraRig.SetEdgeScroll(false);
#endif

            // --- Simulation ---
            var rules = RuleSet.CreateDefault();
            rules.ApplyMapOverrides(pud);
            var sim = new GameSim(seed: 42); // lobby seed at M10
            sim.Setup(pud, rules);

            var driver = new Craftwar.Net.LocalLockstepDriver();
            var replay = new Replay { Seed = 42, MapHash = Replay.HashMapBytes(File.ReadAllBytes(mapPath)) };
            var runner = gameObject.AddComponent<GameLoopRunner>();
            runner.Init(sim, driver, replay);

            // --- Unit views + input ---
            var spriteBank = new UnitSpriteBank(archive, pud.Era);
            var poolGo = new GameObject("UnitViews");
            var pool = poolGo.AddComponent<UnitViewPool>();
            var uiState = new UIState();
            pool.Init(runner, spriteBank, pud.Height, uiState.Selection);
            var ui = gameObject.AddComponent<UIManager>();
            ui.Init(runner, uiState);

            var input = gameObject.AddComponent<InputRouter>();
            input.Init(uiState, ui);
            var world = gameObject.AddComponent<WorldInputController>();
            world.Init(runner, uiState, cameraRig.GetComponent<Camera>(), pud.Height,
                input, new DragSelectOverlayView(ui.OverlayLayer));
            cameraRig.Init(input);

            var ghost = poolGo.AddComponent<BuildPlacementGhost>();
            ghost.Init(runner, spriteBank, cameraRig.GetComponent<Camera>(), pud.Height, uiState);
            var debugOverlay = gameObject.AddComponent<DebugOverlay>();
            debugOverlay.Init(runner, pool, cameraRig.GetComponent<Camera>(), pud.Height);

            // Card hotkeys and the F3 overlay ride the same router as the world.
            input.OnCardSlot += slot => ui.Hud?.Card?.Activate(slot);
            input.OnToggleDebug += debugOverlay.Toggle;
            _runner = runner;
            _tileResolver = catalog;
            _ui = ui;

            // Center the camera on player 0's start location.
            foreach (var e in pud.Units)
            {
                if ((e.Type == (byte)UnitTypeId.HumanStart || e.Type == (byte)UnitTypeId.OrcStart)
                    && e.Owner == 0)
                {
                    cameraRig.transform.position = new Vector3(e.X, pud.Height - 1 - e.Y,
                        cameraRig.transform.position.z);
                    break;
                }
            }

            Debug.Log($"[Craftwar] Loaded '{Path.GetFileName(mapPath)}' " +
                      $"{pud.Width}x{pud.Height} {pud.Era}, {pud.Units.Count} map units, " +
                      $"{sim.State.HighestUnitIndex} sim units, {catalog.TileCount} tiles.");
        }
    }
}
