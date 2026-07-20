using System;
using System.IO;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        /// <summary>The menu scene, added to build settings in Phase 2. Quitting to
        /// menu degrades to a warning until it exists, so Game.unity stays runnable
        /// on its own.</summary>
        public const string MenuSceneName = "Menu";

        public static string StreamingMapsDir =>
            Path.Combine(Application.streamingAssetsPath, MapsFolder);

        public PudFile CurrentMap { get; private set; }

        GameLoopRunner _runner;
        ITileResolver _tileResolver;
        UIManager _ui;
        MinimapView _minimap;
        AudioDirector _audio;
        MatchConfig _config;

        /// <summary>Set once the match resolves, so the outcome is only announced once.</summary>
        bool _matchOverShown;

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

            CheckMatchOver();
        }

        /// <summary>
        /// Poll the local slot's hashed outcome rather than watching for the
        /// one-frame PlayerDefeated/PlayerVictorious event. A screen that must
        /// appear should not depend on us being alive for a single frame, and
        /// this also picks the result up correctly after a load or a reconnect.
        /// </summary>
        void CheckMatchOver()
        {
            if (_matchOverShown || _ui == null || _runner?.Sim == null)
                return;

            byte local = _config?.localSlot ?? HudScreen.LocalPlayer;
            var outcome = _runner.Sim.State.Players[local].Outcome;
            if (outcome == PlayerOutcome.Playing)
                return;

            _matchOverShown = true;
            SaveReplay();
            _ui.Push(new VictoryScreen(_ui, _runner, outcome, Restart, QuitToMenu));
        }

        /// <summary>Reload the scene with the same config — everything is built in
        /// Start(), so this is a clean reset with no teardown of its own.</summary>
        public void Restart()
        {
            MatchSession.Pending = MatchSession.Current;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void QuitToMenu()
        {
            if (Application.CanStreamedLevelBeLoaded(MenuSceneName))
                SceneManager.LoadScene(MenuSceneName);
            else
                Debug.LogWarning($"[Craftwar] No '{MenuSceneName}' scene in build settings yet.");
        }

        /// <summary>
        /// Timestamped, so returning to the menu no longer overwrites the previous
        /// match. GameLoopRunner.OnDestroy still writes last-session.cwrp as a
        /// safety net for crashes and alt-F4.
        /// </summary>
        void SaveReplay()
        {
            if (_runner == null)
                return;
            string name = $"match-{DateTime.Now:yyyyMMdd-HHmmss}.cwrp";
            _runner.SaveReplay(Path.Combine(GameLoopRunner.ReplayDir, name));
        }

        /// <summary>
        /// Empty override falls back to the per-machine LocalAssetPaths default.
        /// A bare file name means "one of the maps shipped in StreamingAssets",
        /// which is what the inspector dropdown stores — that keeps the scene
        /// portable, where an absolute path would bake in one machine's layout.
        /// A value with a separator is honoured verbatim, so existing absolute
        /// overrides keep working.
        /// </summary>
        public static string ResolveMapPath(LocalAssetPaths paths, string mapPath)
        {
            // paths can be null: the install may have been auto-detected without
            // a LocalAssetPaths.json existing at all.
            if (string.IsNullOrEmpty(mapPath))
                return paths == null
                    ? string.Empty
                    : Path.Combine(paths.mapsDir ?? "", paths.defaultMap ?? "");

            bool bareName = mapPath.IndexOf('/') < 0
                && mapPath.IndexOf('\\') < 0;
            return bareName
                ? Path.Combine(StreamingMapsDir, mapPath)
                : mapPath;
        }

        void Start()
        {
            // A config handed over by the menu scene wins; with none (pressing
            // Play straight on Game.unity, which is the dev loop) fall back to
            // the inspector fields. Both paths must keep working.
            _config = MatchSession.Take();
            if (_config == null)
            {
                _config = MatchConfig.FromMapDefaults(mapOverridePath);
                MatchSession.SetCurrent(_config);
            }

            var paths = LocalAssetPaths.Load();
            var assets = paths?.CreateAssetSource();
            if (assets == null)
            {
                // dataRoot is unset or gone. Try to find an install anyway, so a
                // dev machine with no LocalAssetPaths.json still runs; the Phase 8
                // wizard turns this into a real first-run flow.
                var found = Wc2InstallLocator.Find();
                if (found.Count > 0 && found[0].IsUsable)
                {
                    assets = new LooseFileAssetSource(found[0].DataRoot);
                    Debug.Log($"[Craftwar] Auto-detected install: {found[0].DataRoot}");
                }
            }
            if (assets == null)
            {
                Debug.LogError(
                    "[Craftwar] No Warcraft II data found. Set dataRoot in " +
                    $"{LocalAssetPaths.ProjectRootPath} to your installation's Data folder.");
                return;
            }

            string mapPath = ResolveMapPath(paths, _config.mapPath);
            if (!File.Exists(mapPath))
            {
                Debug.LogError($"[Craftwar] Map not found: {mapPath}");
                return;
            }

            var pud = PudFile.Parse(File.ReadAllBytes(mapPath));
            CurrentMap = pud;

            var catalog = RuntimeTileCatalog.Build(assets, pud.Era);

            tilemapView.LoadMap(pud, catalog);
            cameraRig.SetMapBounds(pud.Width, pud.Height);
#if UNITY_EDITOR
            cameraRig.SetEdgeScroll(false);
#endif

            // --- Simulation ---
            var rules = RuleSet.CreateDefault();
            rules.ApplyMapOverrides(pud);
            var sim = new GameSim(_config.seed);
            var setup = _config.ToMatchSetup();
            if (setup.HasValue)
                sim.Setup(pud, rules, setup.Value);
            else
                sim.Setup(pud, rules); // no lobby: the map's own OWNR/SIDE

            var driver = new Craftwar.Net.LocalLockstepDriver();
            var replay = new Replay { Seed = _config.seed, MapHash = Replay.HashMapBytes(File.ReadAllBytes(mapPath)) };
            var runner = gameObject.AddComponent<GameLoopRunner>();
            runner.Init(sim, driver, replay);

            // --- Unit views + input ---
            var spriteBank = new UnitSpriteBank(assets, pud.Era);
            var poolGo = new GameObject("UnitViews");
            var pool = poolGo.AddComponent<UnitViewPool>();
            var uiState = new UIState();
            pool.Init(runner, spriteBank, pud.Height, uiState.Selection);
            var ui = gameObject.AddComponent<UIManager>();
            ui.InitForMatch(runner, uiState);

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
            audio.Init(new LooseAudioBank(assets), HudScreen.LocalPlayer);
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
