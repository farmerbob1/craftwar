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
        [Tooltip("Map to load. Empty = LocalAssetPaths.defaultMap. A bare file " +
                 "name resolves against StreamingAssets/Maps; anything containing " +
                 "a separator is used as a literal path.")]
        [SerializeField] string mapOverridePath = "";

        /// <summary>Where the inspector dropdown looks, and where a bare
        /// <c>mapOverridePath</c> is resolved from. Blizzard .pud files here are
        /// gitignored — see .gitignore.</summary>
        public const string MapsFolder = "Maps";

        public static string StreamingMapsDir =>
            Path.Combine(Application.streamingAssetsPath, MapsFolder);

        public PudFile CurrentMap { get; private set; }

        GameLoopRunner _runner;
        ITileResolver _tileResolver;
        UIManager _ui;
        MinimapView _minimap;
        AudioDirector _audio;

        void LateUpdate()
        {
            if (_runner == null)
                return;

            if (_runner.PendingTileChanges.Count > 0)
            {
                foreach (var (x, y, tile) in _runner.PendingTileChanges)
                    tilemapView.SetTile(x, y, tile, _tileResolver);
                _minimap?.ApplyTileChanges(_runner.PendingTileChanges);
                _runner.PendingTileChanges.Clear();
            }

            if (_runner.PendingSimEvents.Count > 0)
            {
                if (_ui != null)
                    _ui.HandleSimEvents(_runner.PendingSimEvents);
                _audio?.HandleSimEvents(_runner.PendingSimEvents);
                _runner.PendingSimEvents.Clear();
            }
        }

        /// <summary>
        /// Empty override falls back to the per-machine LocalAssetPaths default.
        /// A bare file name means "one of the maps shipped in StreamingAssets",
        /// which is what the inspector dropdown stores — that keeps the scene
        /// portable, where an absolute path would bake in one machine's layout.
        /// A value with a separator is honoured verbatim, so existing absolute
        /// overrides keep working.
        /// </summary>
        string ResolveMapPath(LocalAssetPaths paths)
        {
            if (string.IsNullOrEmpty(mapOverridePath))
                return Path.Combine(paths.mapsDir ?? "", paths.defaultMap ?? "");

            bool bareName = mapOverridePath.IndexOf('/') < 0
                && mapOverridePath.IndexOf('\\') < 0;
            return bareName
                ? Path.Combine(StreamingMapsDir, mapOverridePath)
                : mapOverridePath;
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

            string mapPath = ResolveMapPath(paths);
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

            var fogGo = new GameObject("FogOfWar");
            var fog = fogGo.AddComponent<FogOfWarView>();
            fog.Init(runner, HudScreen.LocalPlayer, pud.Width, pud.Height);

            var ghost = poolGo.AddComponent<BuildPlacementGhost>();
            ghost.Init(runner, spriteBank, cameraRig.GetComponent<Camera>(), pud.Height, uiState);
            var debugOverlay = gameObject.AddComponent<DebugOverlay>();
            debugOverlay.Init(runner, pool, cameraRig.GetComponent<Camera>(), pud.Height);

            // The minimap needs the camera, the tile palette and world input,
            // none of which exist when the HUD is built.
            if (ui.Hud?.Minimap?.Content != null)
            {
                _minimap = new MinimapView(ui.Hud.Minimap, runner, cameraRig, world, catalog,
                    HudScreen.LocalPlayer, pud.Width, pud.Height);
                ui.Hud.SetMinimap(_minimap);
            }

            var groups = new ControlGroups(runner, uiState.Selection, cameraRig, pud.Height,
                InputRouter.GroupCount);

            var audio = gameObject.AddComponent<AudioDirector>();
            audio.Init(new PlaceholderAudioBank(), HudScreen.LocalPlayer);
            world.SetAudio(audio);
            _audio = audio;

            // Card hotkeys and the F3 overlay ride the same router as the world.
            input.OnCardSlot += slot => ui.Hud?.Card?.Activate(slot);
            input.OnGroupKey += groups.HandleKey;
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
