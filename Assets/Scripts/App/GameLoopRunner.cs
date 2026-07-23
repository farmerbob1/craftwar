using System;
using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using Craftwar.Net;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Craftwar.App
{
    /// <summary>
    /// The game scene's one driver. Owns the deterministic sim: advances it at a
    /// fixed 50 Hz from Unity's render loop, snapshots positions for interpolation,
    /// and records the command log as a replay. On <see cref="Start"/> it also
    /// stands the match up — resolves the local data, parses the PUD, builds the
    /// sim, and binds the persistent view subsystems that are wired to it in the
    /// scene — then pumps their per-frame changes and owns the match-over/restart
    /// flow. Everything the match needs lives in the scene as sibling or referenced
    /// components; only the sim and the per-unit views (which are sized from the
    /// map at load time) are created at runtime.
    /// </summary>
    [RequireComponent(typeof(View.UIManager))]
    [RequireComponent(typeof(View.InputRouter))]
    [RequireComponent(typeof(View.WorldInputController))]
    [RequireComponent(typeof(View.AudioDirector))]
    [RequireComponent(typeof(View.DebugOverlay))]
    public sealed class GameLoopRunner : MonoBehaviour, View.ISimHost
    {
        // --- Scene wiring (assigned in the editor) ------------------------------

        [Header("Scene references")]
        [SerializeField] View.TilemapView tilemapView;
        [SerializeField] View.CameraRig cameraRig;
        [SerializeField] View.UnitViewPool unitViewPool;
        [SerializeField] View.BuildPlacementGhost buildGhost;
        [SerializeField] View.FogOfWarView fogOfWar;

        [Tooltip("Map to load when no lobby handed one over (pressing Play on " +
                 "Game.unity). Empty = LocalAssetPaths.defaultMap. A bare file name " +
                 "resolves against StreamingAssets/Maps; a path with a separator is " +
                 "used verbatim.")]
        [SerializeField] string mapOverridePath = "";

        // Siblings on this GameObject, pulled in Start (guaranteed by RequireComponent).
        View.UIManager _ui;
        View.InputRouter _input;
        View.WorldInputController _world;
        View.AudioDirector _audio;
        View.DebugOverlay _debugOverlay;

        // --- Sim driver state ---------------------------------------------------

        public GameSim Sim { get; private set; }
        public ILockstepDriver Driver { get; private set; }

        /// <summary>Interpolation factor between the previous and current tick.</summary>
        public float Alpha { get; private set; }

        public int[] PrevPixX { get; private set; }
        public int[] PrevPixY { get; private set; }

        readonly List<GameCommand> _tickCommands = new List<GameCommand>();
        Replay _replay;
        float _accumulator;
        const float TickSeconds = SimConstants.MsPerTick / 1000f;

        // Computer opponents. Command sources exactly like the human: they read
        // sim state and submit through the driver, so replays capture their
        // commands. Plain owned objects, rebuilt with the scene on Restart.
        readonly List<AiPlayer> _ais = new List<AiPlayer>();
        readonly List<GameCommand> _aiCommands = new List<GameCommand>();

        /// <summary>Tile mutations accumulated across the ticks run this frame; drained by the view.</summary>
        public readonly List<(ushort x, ushort y, ushort tile)> PendingTileChanges
            = new List<(ushort, ushort, ushort)>();

        /// <summary>Presentation events accumulated across the ticks run this frame; drained by the view.</summary>
        public readonly List<SimEvent> PendingSimEvents = new List<SimEvent>();

        // --- Match-level state --------------------------------------------------

        /// <summary>Where the inspector dropdown looks, and where a bare
        /// <c>mapOverridePath</c> is resolved from. Blizzard .pud files here are
        /// gitignored — see .gitignore.</summary>
        public const string MapsFolder = "Maps";

        /// <summary>Quitting to menu degrades to a warning until this scene is in
        /// build settings, so Game.unity stays runnable on its own.</summary>
        public const string MenuSceneName = "Menu";

        public static string StreamingMapsDir =>
            Path.Combine(Application.streamingAssetsPath, MapsFolder);

        public PudFile CurrentMap => _map;

        MatchConfig _config;
        View.ITileResolver _tileResolver;
        View.MinimapView _minimap;
        View.MusicDirector _music;
        PudFile _map;

        /// <summary>Set once the match resolves, so the outcome is only announced once.</summary>
        bool _matchOverShown;

        // --- Startup: stand the match up ---------------------------------------

        void Start()
        {
            _ui = GetComponent<View.UIManager>();
            _input = GetComponent<View.InputRouter>();
            _world = GetComponent<View.WorldInputController>();
            _audio = GetComponent<View.AudioDirector>();
            _debugOverlay = GetComponent<View.DebugOverlay>();

            // A config handed over by the menu scene wins; with none (pressing
            // Play straight on Game.unity, the dev loop) fall back to the
            // inspector map. Both paths must keep working.
            _config = MatchSession.Take();
            if (_config == null)
            {
                _config = MatchConfig.FromMapDefaults(mapOverridePath);
                MatchSession.SetCurrent(_config);
            }

            var paths = LocalAssetPaths.Load();
            var assets = AssetResolution.ResolveAssetSource(paths, out string dataRoot);
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

            var mapBytes = File.ReadAllBytes(mapPath);
            _map = PudFile.Parse(mapBytes);
            var catalog = RuntimeTileCatalog.Build(assets, _map.Era);
            _tileResolver = catalog;

            tilemapView.LoadMap(_map, catalog);
            cameraRig.SetMapBounds(_map.Width, _map.Height);
#if UNITY_EDITOR
            cameraRig.SetEdgeScroll(false);
#endif

            BuildSim(mapBytes);
            BuildView(assets, catalog);
            BuildAudioAndNames(assets, paths, dataRoot);
            WireInput();
            CenterCamera(mapPath, catalog);
        }

        void BuildSim(byte[] mapBytes)
        {
            var rules = RuleSet.CreateDefault();
            rules.ApplyMapOverrides(_map);
            var sim = new GameSim(_config.seed);
            var setup = _config.ToMatchSetup();
            if (setup.HasValue)
                sim.Setup(_map, rules, setup.Value);
            else
                sim.Setup(_map, rules); // no lobby: the map's own OWNR/SIDE

            // Single-player is lockstep with a local, zero-delay driver; a lobby
            // will hand in a net driver here instead (M10).
            var driver = new LocalLockstepDriver();
            var replay = new Replay { Seed = _config.seed, MapHash = Replay.HashMapBytes(mapBytes) };
            Init(sim, driver, replay);
            CreateAis();
        }

        /// <summary>
        /// One AI per Computer slot, on both config paths (explicit lobby slots
        /// and the null-slots PUD fall-through, whose OWNR already marks melee
        /// computer slots). Replay playback must never call this — the recorded
        /// commands already contain everything the AIs did.
        /// </summary>
        void CreateAis()
        {
            _ais.Clear();
            for (byte p = 0; p < SimConstants.MaxPlayers; p++)
            {
                ref PlayerState ps = ref Sim.State.Players[p];
                if (ps.Controller != Controller.Computer || !ps.InGame)
                    continue;
                byte aipl = _config.slots != null && p < _config.slots.Length
                    && _config.slots[p] != null
                    ? _config.slots[p].aiType
                    : _map.AiType[p];
                var ai = new AiPlayer(p, AiBehaviorMap.FromAiplByte(aipl));
                _ais.Add(ai);
                // Provenance for the replay header (v2). The recorded commands
                // already reproduce the match; this only records which strategy
                // each computer slot ran.
                _replay?.SetAiStrategyHash(p, ai.Strategy.Hash());
            }
        }

        void BuildView(IAssetSource assets, RuntimeTileCatalog catalog)
        {
            var spriteBank = new UnitSpriteBank(assets, _map.Era);
            var uiState = new View.UIState();
            unitViewPool.Init(this, spriteBank, _map.Height, uiState.Selection);

            _ui.InitForMatch(this, uiState);

            _input.Init(uiState, _ui);
            _world.Init(this, uiState, cameraRig.GetComponent<Camera>(), _map.Height,
                _input, new View.DragSelectOverlayView(_ui.OverlayLayer));
            cameraRig.Init(_input);

            fogOfWar.Init(this, View.HudScreen.LocalPlayer, _map.Width, _map.Height);
            buildGhost.Init(this, spriteBank, cameraRig.GetComponent<Camera>(), _map.Height, uiState);
            _debugOverlay.Init(this, unitViewPool, cameraRig.GetComponent<Camera>(), _map.Height);

            // The minimap needs the camera, the tile palette and world input,
            // none of which exist when the HUD is built.
            if (_ui.Hud?.Minimap?.Content != null)
            {
                _minimap = new View.MinimapView(_ui.Hud.Minimap, this, cameraRig, _world, catalog,
                    View.HudScreen.LocalPlayer, _map.Width, _map.Height);
                _ui.Hud.SetMinimap(_minimap);
            }

            _groups = new View.ControlGroups(this, uiState.Selection, cameraRig, _map.Height,
                View.InputRouter.GroupCount);
        }

        View.ControlGroups _groups;

        void BuildAudioAndNames(IAssetSource assets, LocalAssetPaths paths, string dataRoot)
        {
            _audio.Init(new LooseAudioBank(assets), View.HudScreen.LocalPlayer);

            // Real names and icons, where the installation provides them. Both are
            // injected rather than looked up, so a machine with no data still
            // renders the reflection-derived names and initials boxes.
            View.UnitNames.SetStringTable(Wc2StringTable.Load(assets, paths?.locale ?? "enUS"));
            var icons = IconAtlas.Load(assets, _map.Era);
            if (icons != null)
                _ui.Hud?.Card?.SetIconProvider(icons);

            var music = MusicLibrary.Create(paths, dataRoot);
            if (music != null)
            {
                _music = View.MusicDirector.Ensure(music);
                _music.Play(View.MusicCue.InGame, Sim.State.Players[_config.localSlot].Race);
            }
            _world.SetAudio(_audio);
        }

        void WireInput()
        {
            // Card hotkeys and the F3 overlay ride the same router as the world.
            _input.OnCardSlot += slot => _ui.Hud?.Card?.Activate(slot);
            _input.OnGroupKey += _groups.HandleKey;
            _input.OnToggleDebug += _debugOverlay.Toggle;
        }

        void CenterCamera(string mapPath, RuntimeTileCatalog catalog)
        {
            // Center the camera on player 0's start location.
            foreach (var e in _map.Units)
            {
                if ((e.Type == (byte)UnitTypeId.HumanStart || e.Type == (byte)UnitTypeId.OrcStart)
                    && e.Owner == 0)
                {
                    cameraRig.transform.position = new Vector3(e.X, _map.Height - 1 - e.Y,
                        cameraRig.transform.position.z);
                    break;
                }
            }

            Debug.Log($"[Craftwar] Loaded '{Path.GetFileName(mapPath)}' " +
                      $"{_map.Width}x{_map.Height} {_map.Era}, {_map.Units.Count} map units, " +
                      $"{Sim.State.HighestUnitIndex} sim units, {catalog.TileCount} tiles.");
        }

        /// <summary>
        /// Empty override falls back to the per-machine LocalAssetPaths default.
        /// A bare file name means "one of the maps shipped in StreamingAssets",
        /// which keeps the scene portable; a value with a separator is honoured
        /// verbatim, so existing absolute overrides keep working.
        /// </summary>
        public static string ResolveMapPath(LocalAssetPaths paths, string mapPath)
        {
            if (string.IsNullOrEmpty(mapPath))
                return paths == null
                    ? string.Empty
                    : Path.Combine(paths.mapsDir ?? "", paths.defaultMap ?? "");

            bool bareName = mapPath.IndexOf('/') < 0 && mapPath.IndexOf('\\') < 0;
            return bareName ? Path.Combine(StreamingMapsDir, mapPath) : mapPath;
        }

        // --- Sim driver ---------------------------------------------------------

        public void Init(GameSim sim, ILockstepDriver driver, Replay replay)
        {
            Sim = sim;
            Driver = driver;
            _replay = replay;
            PrevPixX = new int[SimConstants.MaxUnits];
            PrevPixY = new int[SimConstants.MaxUnits];
            SnapshotPositions();
        }

        public void SubmitCommand(in GameCommand cmd) => Driver.SubmitLocalCommand(cmd);

        /// <summary>
        /// Single-player pause. The sim simply stops being advanced, so the
        /// state hash and the replay are untouched — a replay recorded across a
        /// paused session still verifies. Zeroing the accumulator on pause stops
        /// the wall-clock gap from being spent as catch-up ticks on resume, and
        /// freezing Alpha holds units mid-stride instead of snapping them.
        /// Networked lockstep (M10) cannot pause this way: every peer would
        /// have to agree, so that becomes a driver-level concern.
        /// </summary>
        public bool Paused { get; private set; }

        public void SetPaused(bool paused)
        {
            if (Paused == paused)
                return;
            Paused = paused;
            if (paused)
                _accumulator = 0f;
        }

        void Update()
        {
            if (Sim == null || Paused)
                return;

            // Game speed scales how fast wall-clock feeds the fixed 50 Hz tick —
            // the sim itself never changes, so determinism and replays are
            // untouched (replays record ticks, not seconds).
            _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f)
                * View.GameplaySettings.Current.SpeedMultiplier;
            int safety = 8; // don't spiral after a hitch
            while (_accumulator >= TickSeconds && safety-- > 0)
            {
                // AIs think here, at a fixed point per tick, so their commands
                // land on exactly the tick they observed and are recorded like
                // any player's. Human input arrives earlier in the frame via
                // the same SubmitLocalCommand path.
                for (int a = 0; a < _ais.Count; a++)
                {
                    _aiCommands.Clear();
                    _ais[a].Think(Sim, _aiCommands);
                    for (int c = 0; c < _aiCommands.Count; c++)
                        Driver.SubmitLocalCommand(_aiCommands[c]);
                }

                if (!Driver.TryGetTickCommands(Sim.State.Tick, _tickCommands))
                    break; // waiting on network turn (M10+)

                SnapshotPositions();
                if (_replay != null)
                    foreach (var c in _tickCommands)
                        _replay.Record(Sim.State.Tick, c);
                Sim.Advance(_tickCommands);
                PendingTileChanges.AddRange(Sim.State.TileChanges);
                PendingSimEvents.AddRange(Sim.State.Events);
                ReconcileTeleports();
                _accumulator -= TickSeconds;
            }
            Alpha = Mathf.Clamp01(_accumulator / TickSeconds);
        }

        /// <summary>Drain the runner's per-frame changes into the view, then poll
        /// for the match outcome. Runs after Update so it sees this frame's ticks.</summary>
        void LateUpdate()
        {
            if (Sim == null)
                return;

            if (PendingTileChanges.Count > 0)
            {
                foreach (var (x, y, tile) in PendingTileChanges)
                    tilemapView.SetTile(x, y, tile, _tileResolver);
                _minimap?.ApplyTileChanges(PendingTileChanges);
                PendingTileChanges.Clear();
            }

            if (PendingSimEvents.Count > 0)
            {
                _ui.HandleSimEvents(PendingSimEvents);
                _audio.HandleSimEvents(PendingSimEvents);
                PendingSimEvents.Clear();
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
            if (_matchOverShown)
                return;

            byte local = _config?.localSlot ?? View.HudScreen.LocalPlayer;
            var outcome = Sim.State.Players[local].Outcome;
            if (outcome == PlayerOutcome.Playing)
                return;

            _matchOverShown = true;
            SaveTimestampedReplay();
            _music?.Play(outcome == PlayerOutcome.Victorious ? View.MusicCue.Victory : View.MusicCue.Defeat,
                         Sim.State.Players[local].Race);
            _ui.Push(new View.VictoryScreen(_ui, this, outcome, Restart, QuitToMenu));
        }

        /// <summary>Reload the scene with the same config — the match is rebuilt in
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

        void ReconcileTeleports()
        {
            var units = Sim.State.Units;
            for (int i = 0; i < Sim.State.HighestUnitIndex; i++)
            {
                int dx = units[i].PixX - PrevPixX[i];
                int dy = units[i].PixY - PrevPixY[i];
                if (dx > 4 || dx < -4 || dy > 4 || dy < -4)
                {
                    PrevPixX[i] = units[i].PixX;
                    PrevPixY[i] = units[i].PixY;
                }
            }
        }

        void SnapshotPositions()
        {
            var units = Sim.State.Units;
            for (int i = 0; i < Sim.State.HighestUnitIndex; i++)
            {
                PrevPixX[i] = units[i].PixX;
                PrevPixY[i] = units[i].PixY;
            }
        }

        public static string ReplayDir => Path.Combine(Application.persistentDataPath, "Replays");

        /// <summary>
        /// Timestamped so returning to the menu no longer overwrites the previous
        /// match. <see cref="OnDestroy"/> still writes last-session.cwrp as a
        /// safety net for crashes and alt-F4.
        /// </summary>
        void SaveTimestampedReplay() =>
            SaveReplay(Path.Combine(ReplayDir, $"match-{DateTime.Now:yyyyMMdd-HHmmss}.cwrp"));

        /// <summary>
        /// Write the command log. Explicit saves are timestamped by the caller;
        /// without one, every return to the menu would overwrite the same
        /// last-session file, which only worked while quitting the app was the
        /// sole way a match ended.
        /// </summary>
        public bool SaveReplay(string path)
        {
            if (_replay == null || _replay.Entries.Count == 0)
                return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, _replay.ToBytes());
                Debug.Log($"[Craftwar] Replay saved: {path} ({_replay.Entries.Count} commands)");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Craftwar] Replay save failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Safety net for crashes and alt-F4; a clean end-of-match has
        /// already written its own timestamped copy.</summary>
        void OnDestroy() => SaveReplay(Path.Combine(ReplayDir, "last-session.cwrp"));
    }
}
