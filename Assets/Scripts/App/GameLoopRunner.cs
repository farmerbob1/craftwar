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
    public sealed partial class GameLoopRunner : MonoBehaviour, View.ISimHost
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

        /// <summary>Most catch-up the accumulator may bank, in ticks. Matches the
        /// per-frame safety cap, so debt never outruns what one frame can spend.</summary>
        const int MaxTickDebt = 8;

        /// <summary>Last tick the AIs were given a chance to think on. Stops a
        /// stalled driver from re-thinking the same tick every rendered frame.</summary>
        int _lastAiThinkTick = int.MinValue;

        /// <summary>True while the driver is withholding the next tick — waiting on
        /// remote input. The view holds still rather than interpolating into a
        /// tick that has not been agreed.</summary>
        public bool Starving { get; private set; }

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

            // Tell the view which seat it is playing before anything is built.
            // Everything downstream — selection, order ownership, fog, the
            // resource strip, the green selection ring — reads this, and it used
            // to be hardcoded to 0 because single player's human is always seat
            // 0. A joining client gets whatever seat the host assigned.
            View.HudScreen.SetLocalPlayer(_config.localSlot);

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

            BuildSim(mapBytes);
            BuildView(assets, catalog);
            BuildAudioAndNames(assets, paths, dataRoot);
            WireInput();
            CenterCamera(mapPath, catalog);
        }

        void BuildSim(byte[] mapBytes)
        {
            GameSim sim;
            if (!string.IsNullOrEmpty(_config.savePath)
                && TryReadSave(_config.savePath, out _, out _, out byte[] snapshot))
            {
                // Restored rather than built: the snapshot carries its own rules
                // and terrain, so Setup must not run over the top of it.
                sim = SimSerializer.Load(snapshot);
            }
            else
            {
                var rules = RuleSet.CreateDefault();
                rules.ApplyMapOverrides(_map);
                sim = new GameSim(_config.seed);
                var setup = _config.ToMatchSetup();
                if (setup.HasValue)
                    sim.Setup(_map, rules, setup.Value);
                else
                    sim.Setup(_map, rules); // no lobby: the map's own OWNR/SIDE
            }

            // A lobby that already negotiated seats hands its live connection
            // over through NetSession; otherwise this is single player, which is
            // still lockstep — just with one participant and no latency.
            ILockstepDriver driver;
            _net = Net.Unity.NetSession.CreateDriver(out var hostExchange, out var clientExchange);
            if (_net != null)
            {
                driver = _net;
                _net.Desynced += OnDesync;
                if (hostExchange != null)
                    hostExchange.Desynced += OnDesync;
                if (clientExchange != null)
                    clientExchange.Desynced += OnDesync;
                InitDropHandling(hostExchange);
            }
            else
            {
                driver = new LocalLockstepDriver();
            }

            var replay = new Replay { Seed = _config.seed, MapHash = Replay.HashMapBytes(mapBytes) };
            Init(sim, driver, replay);

            // Only the host runs the computer players. Their commands travel the
            // wire inside the host's own input block, so every peer executes the
            // identical AI orders rather than each re-deriving them — one
            // divergence in AI state would otherwise desync the match.
            if (_net == null || Net.Unity.NetSession.IsHost)
                CreateAis();
        }

        /// <summary>Non-null only in a networked match.</summary>
        INetLockstepDriver _net;
        bool _desyncHalted;

        /// <summary>
        /// A desync means the peers are already simulating different games, so
        /// the match is over as a contest. Halt and write everything needed to
        /// debug it: the replay reproduces the run, and the hash ring says which
        /// turn the divergence started on rather than merely that it happened.
        /// </summary>
        void OnDesync(DesyncReport report)
        {
            if (_desyncHalted)
                return;
            _desyncHalted = true;
            SetPaused(true);
            Debug.LogError($"[craftwar-net] {report}");
            SaveTimestampedReplay();
            DumpDesyncDiagnostics(report);
        }

        void DumpDesyncDiagnostics(DesyncReport report)
        {
            try
            {
                Directory.CreateDirectory(ReplayDir);
                string path = Path.Combine(ReplayDir,
                    $"desync-{System.DateTime.Now:yyyyMMdd-HHmmss}.txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.ToString());
                sb.AppendLine($"localSlot={_config?.localSlot} tick={Sim?.State.Tick}");
                sb.AppendLine($"checksums={Sim?.State.VerifyChecksums() ?? "(no sim)"}");
                if (Driver is TurnLockstepDriver turnDriver)
                {
                    sb.AppendLine("turn,hash");
                    foreach (var (turn, hash) in turnDriver.HashHistory())
                        sb.AppendLine($"{turn},{hash:X8}");
                }
                File.WriteAllText(path, sb.ToString());
                Debug.LogError($"[craftwar-net] desync dump written to {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[craftwar-net] could not write the desync dump: {e.Message}");
            }
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
                var slot = _config.slots != null && p < _config.slots.Length
                    ? _config.slots[p] : null;
                byte aipl = slot != null ? slot.aiType : _map.AiType[p];
                var profile = AiProfileLibrary.Resolve(slot?.aiStrategy);
                var tier = (AiTier)(slot != null ? slot.aiTier : (byte)AiTier.Normal);
                var ai = new AiPlayer(p, AiBehaviorMap.FromAiplByte(aipl), profile, tier);
                _ais.Add(ai);
                // Provenance for the replay header (v2). The recorded commands
                // already reproduce the match; this only records which profile
                // each computer slot ran.
                _replay?.SetAiStrategyHash(p, ai.Profile.Hash());
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
            // The HUD covers the camera's left and top edges; the rig has to
            // know how much so the map under the chrome stays reachable.
            cameraRig.SetChromeInsetSource(() =>
                _ui.Hud != null ? _ui.Hud.ChromeInsetsPixels() : Vector4.zero);

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
                _ui.Hud?.SetIconProvider(icons);

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
            // Everything keyboard-driven rides the same router as the world.
            _input.OnCommandHotkey += key => _ui.Hud?.Card?.ActivateHotkey(key);
            _input.CardEscapeHandler = () => _ui.Hud?.Card?.ActivateEscape() ?? false;
            _input.OnGroupKey += _groups.HandleKey;
            _input.OnCenterOnSelection += _groups.CenterOnSelection;
            _input.OnViewportKey += cameraRig.HandleViewportKey;
            _input.OnSpeedStep += StepGameSpeed;
            _input.OnToggleDebug += _debugOverlay.Toggle;
        }

        /// <summary>
        /// The original's +/- keys. Persisted immediately so the choice
        /// survives a quit, exactly as the options screen does it.
        /// </summary>
        static void StepGameSpeed(int delta)
        {
            View.GameplaySettings.Current.StepSpeed(delta);
            View.GameplaySettings.Save();
        }

        void CenterCamera(string mapPath, RuntimeTileCatalog catalog)
        {
            // Centre on OUR start location, not seat 0's. This was hardcoded to
            // owner 0 back when the human was always seat 0; a client playing any
            // other seat opened the match looking at somebody else's base.
            byte seat = _config?.localSlot ?? 0;
            bool centred = false;
            for (int pass = 0; pass < 2 && !centred; pass++)
            {
                // Second pass ignores ownership, so a map with no start marker
                // for our seat still puts the camera somewhere sensible.
                foreach (var e in _map.Units)
                {
                    bool isStart = e.Type == (byte)UnitTypeId.HumanStart
                                   || e.Type == (byte)UnitTypeId.OrcStart;
                    if (!isStart || (pass == 0 && e.Owner != seat))
                        continue;
                    cameraRig.CenterOn(e.X, _map.Height - 1 - e.Y);
                    centred = true;
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
            // Networked: a pause is an agreement, not a local toggle. It travels
            // as a command so every peer stops on the same turn, and the driver
            // holds the set of pausing slots. Stopping our own clock here instead
            // would just make this peer stall everyone else.
            if (_net != null)
            {
                SubmitCommand(new GameCommand
                {
                    Op = paused ? CommandOp.Pause : CommandOp.Resume,
                    Player = _config?.localSlot ?? 0,
                });
                return;
            }

            if (Paused == paused)
                return;
            Paused = paused;
            if (paused)
                _accumulator = 0f;
        }

        /// <summary>
        /// Whether this peer may stop the world on its own. False in a networked
        /// match, where screens that pause as a side effect — the victory screen
        /// above all — would otherwise freeze everybody: the first player
        /// eliminated in a 4v4 must keep simulating as an observer, and keep
        /// feeding the turn schedule, or the rest of the match stalls.
        /// </summary>
        public bool CanPauseLocally => _net == null;

        public static string SaveDir => Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>
        /// Write a snapshot of the running match. The sim snapshot is
        /// self-contained, but the view still needs the map file to draw tiles,
        /// so the map path travels alongside it.
        ///
        /// Disabled in multiplayer: a save is one peer's private copy, and
        /// reloading it would drop that peer out of the shared turn schedule.
        /// </summary>
        public bool SaveGame(out string path)
        {
            path = null;
            if (Sim == null || _net != null)
                return false;
            try
            {
                Directory.CreateDirectory(SaveDir);
                path = Path.Combine(SaveDir, $"save-{System.DateTime.Now:yyyyMMdd-HHmmss}.cws");

                byte[] snapshot = SimSerializer.Save(Sim);
                string mapPath = _config?.mapPath ?? "";
                var w = new ByteWriter(snapshot.Length + 256);
                var mapBytes = System.Text.Encoding.UTF8.GetBytes(mapPath);
                w.WriteUShort((ushort)mapBytes.Length);
                for (int i = 0; i < mapBytes.Length; i++)
                    w.WriteByte(mapBytes[i]);
                w.WriteByte(_config?.localSlot ?? 0);
                w.WriteBytes(snapshot, 0, snapshot.Length);
                File.WriteAllBytes(path, w.ToArray());
                Debug.Log($"[craftwar] saved to {path}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[craftwar] save failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Read back what <see cref="SaveGame"/> wrote.</summary>
        public static bool TryReadSave(string path, out string mapPath, out byte snapshotSlot,
            out byte[] snapshot)
        {
            mapPath = null;
            snapshotSlot = 0;
            snapshot = null;
            try
            {
                var r = new ByteReader(File.ReadAllBytes(path));
                int nameLength = r.ReadUShort();
                var nameBytes = new byte[nameLength];
                for (int i = 0; i < nameLength; i++)
                    nameBytes[i] = r.ReadByte();
                mapPath = System.Text.Encoding.UTF8.GetString(nameBytes);
                snapshotSlot = r.ReadByte();
                snapshot = r.ReadBytes();
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[craftwar] could not read {path}: {e.Message}");
                return false;
            }
        }

        public string NetStatusLine
        {
            get
            {
                if (_net == null)
                    return null;
                string state = _desyncHalted ? "DESYNC"
                    : _net.IsPaused ? "PAUSED"
                    : Starving ? "WAITING"
                    : _net.Status.ToString().ToUpperInvariant();
                string line = $"NET {state}  seat {_net.LocalSlot}  turn {_net.CurrentTurn}" +
                       $"/{_net.ConfirmedTurn}  delay {Net.Unity.NetSession.InputDelayTurns}t";

                if (_dropGraceRemaining.Count > 0)
                    foreach (var pair in _dropGraceRemaining)
                        line += $"\nWaiting for seat {pair.Key + 1}… ({pair.Value:F0}s)";
                if (_substituteAis.Count > 0)
                    foreach (byte slot in _substituteAis.Keys)
                        line += $"\nSeat {slot + 1} dropped — AI playing";

                return line;
            }
        }

        void Update()
        {
            if (Sim == null)
                return;

            // ABOVE the Paused early-out, deliberately. A paused peer — or one
            // sitting on the victory screen after being eliminated — must keep
            // pumping the socket, or it stops acknowledging turns and stalls
            // every other player until the drop timeout fires.
            _net?.Poll();
            UpdateDropDetection();

            if (Paused)
                return;

            // Game speed scales how fast wall-clock feeds the fixed 50 Hz tick —
            // the sim itself never changes, so determinism and replays are
            // untouched (replays record ticks, not seconds).
            // In a networked match the speed is the host's, not this machine's:
            // peers feeding the turn clock at different rates would have one
            // starving everyone else and another permanently in the stall clamp.
            float speed = _net != null
                ? Net.Unity.NetSession.SpeedMultiplier
                : View.GameplaySettings.Current.SpeedMultiplier;
            _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f) * speed;
            // Cap banked debt. The per-frame caps below only *defer* catch-up, so
            // without this the accumulator grows without bound whenever the
            // driver withholds a tick; a 10 s network stall would bank ~500 ticks
            // and then fast-forward the match on resume.
            _accumulator = Mathf.Min(_accumulator, MaxTickDebt * TickSeconds);
            int safety = 8; // don't spiral after a hitch
            // Also cap the wall-clock time spent catching up this frame. With the
            // pathfinding fix a tick is ~1-3 ms so this never bites, but it turns a
            // transient tick spike into brief slow-motion instead of a frame-time
            // death spiral (render never gets a look-in) — the failure mode that
            // used to pin the game at ~20 fps.
            const double CatchUpBudgetSeconds = 0.010;
            double frameStart = Time.realtimeSinceStartupAsDouble;
            while (_accumulator >= TickSeconds && safety-- > 0)
            {
                // AIs think here, at a fixed point per tick, so their commands
                // land on exactly the tick they observed and are recorded like
                // any player's. Human input arrives earlier in the frame via
                // the same SubmitLocalCommand path.
                //
                // Guarded on the tick, not the frame: when the driver withholds a
                // tick below, the sim clock stops but Update keeps running, so an
                // unguarded call would re-think the same tick once per rendered
                // frame and submit the same orders again and again. Must stay
                // per-TICK — quantizing it to the 4-tick command turn would break
                // the AI outright, since AiPlayer gates on
                // (Tick + Slot*7) % ThinkPeriod with periods {22, 25, 50}: the
                // even periods share a factor with 4 and the odd slots would
                // never think at all.
                if (_lastAiThinkTick != Sim.State.Tick)
                {
                    _lastAiThinkTick = Sim.State.Tick;
                    for (int a = 0; a < _ais.Count; a++)
                    {
                        _aiCommands.Clear();
                        _ais[a].Think(Sim, _aiCommands);
                        for (int c = 0; c < _aiCommands.Count; c++)
                            Driver.SubmitLocalCommand(_aiCommands[c]);
                    }
                    ThinkSubstituteAis();
                }

                // Hand the driver the state we are about to execute from, at
                // every turn boundary, so it can be compared against the other
                // peers'. Taken BEFORE Advance: "the state entering turn T" is a
                // point in time both peers can agree on, whereas "after turn T"
                // is not available at the moment the input for it is published.
                if (_net != null && Sim.State.Tick % SimConstants.TicksPerCommandTurn == 0)
                    _net.RecordTurnHash(Sim.State.Tick / SimConstants.TicksPerCommandTurn,
                        Sim.State.ComputeHash());

                if (!Driver.TryGetTickCommands(Sim.State.Tick, _tickCommands))
                {
                    // Waiting on a network turn. Never bank the wait: hold at most
                    // one tick of debt so resuming continues at real speed instead
                    // of fast-forwarding through everything we stood still for.
                    Starving = true;
                    if (_accumulator > TickSeconds)
                        _accumulator = TickSeconds;
                    break;
                }
                Starving = false;

                SnapshotPositions();
                if (_replay != null)
                    foreach (var c in _tickCommands)
                        _replay.Record(Sim.State.Tick, c);
                Sim.Advance(_tickCommands);
                PendingTileChanges.AddRange(Sim.State.TileChanges);
                PendingSimEvents.AddRange(Sim.State.Events);
                ReconcileTeleports();
                _accumulator -= TickSeconds;
                if (Time.realtimeSinceStartupAsDouble - frameStart > CatchUpBudgetSeconds)
                    break; // yield to rendering; remaining catch-up rolls to next frame
            }
            // Starving: freeze at the tick we actually agreed on instead of
            // sliding units into an unconfirmed one and snapping them back.
            Alpha = Starving ? 1f : Mathf.Clamp01(_accumulator / TickSeconds);
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
