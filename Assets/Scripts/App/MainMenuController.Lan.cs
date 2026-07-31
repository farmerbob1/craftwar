using System.Collections.Generic;
using System.IO;
using Craftwar.Net;
using Craftwar.Net.Unity;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// The LAN half of the menu: a browser of games heard on the network, and
    /// the lobby room itself.
    ///
    /// Kept in its own file because it is a different job from the single-player
    /// panels — and it owns a socket, which nothing else in the menu does.
    /// </summary>
    public sealed partial class MainMenuController
    {
        VisualElement _panelLan, _panelLobby, _panelHostSetup, _lanGameList, _lobbySlotList;
        Label _lanStatus, _lobbyTitle, _lobbyMap, _lobbyStatus;
        TextField _lanName, _lanAddress;
        Button _lobbyStart;

        // Host setup: its own panel (not bolted onto panel-lan/panel-online),
        // shared by LAN and Online like panel-lobby is — ShowHostSetup(online)
        // remembers which Host*Game to call and which panel Back returns to.
        Label _hostSetupMapLabel, _hostSetupStatus, _hostSetupPlayersLabel;
        Image _hostSetupThumb, _lobbyMapThumb;
        TextField _hostSetupNameField;
        Button _hostSetupGameTypeBtn, _hostSetupSpeedBtn;
        bool _hostSetupIsOnline;
        int _hostSetupPlayerCount = SimConstants.MaxPlayers;
        int _hostSetupMapMaxSlots = SimConstants.MaxPlayers;
        LobbyGameType _hostSetupGameType = LobbyGameType.Ffa;
        /// <summary>Index into GameplaySettings' six-step Slowest..Fastest
        /// scale — host-owned for a networked match (NetSession.
        /// SpeedMultiplier's own doc comment: a peer feeding the turn clock
        /// at a different rate either starves everyone or free-runs past the
        /// starvation clamp), so this is chosen once at Host Setup, not an
        /// in-lobby toggle.</summary>
        int _hostSetupSpeedIndex = GameplaySettings.NormalSpeedIndex;
        string _lobbyThumbFor;

        /// <summary>Seat indices Closed by the Host Setup player-count cap
        /// (not by the host toggling a seat in the lobby afterward) — local
        /// bookkeeping only, never sent over the wire. These are removed from
        /// the host's own roster view entirely; a seat the host closes later
        /// via the in-lobby dropdown still shows (and can be reopened), same
        /// as before.</summary>
        readonly HashSet<int> _cappedClosedSeats = new HashSet<int>();

        // Ladder rating lookups (online lobbies only — LAN has no server to
        // ask). Fetched once per name via RelayPeerSocket.SendGetRating and
        // drained in LateUpdate; the room browser gets its ratings batched
        // directly into RoomSummary instead (see MainMenuController.Online.cs).
        readonly Dictionary<string, (bool found, int rating, int games)> _ratingCache =
            new Dictionary<string, (bool, int, int)>();
        readonly HashSet<string> _ratingRequested = new HashSet<string>();
        VisualElement _inspectPopup;

        // Lazily built per PudEra (only 4 exist) so stepping the map picker
        // doesn't re-decode a whole tileset atlas on every click.
        readonly Dictionary<PudEra, RuntimeTileCatalog> _tileCatalogCache = new Dictionary<PudEra, RuntimeTileCatalog>();

        LanDiscovery _discovery;
        UtpPeerSocket _socket;
        LobbyHost _lobbyHost;
        LobbyClient _lobbyClient;

        BuildIdentity _hostIdentity;
        float _nextBeaconTime;
        float _nextBrowserRefresh;
        int _lanMapSel;
        bool _lobbyDirty;

        /// <summary>Which browser "Leave" should return to — the lobby panel
        /// is shared by LAN and Online (see the UXML comment on panel-lobby),
        /// so leaving it needs to remember which one it was entered from.</summary>
        bool _lobbyIsOnline;

        void InitLan(VisualElement root)
        {
            _panelLan = root.Q("panel-lan");
            _panelLobby = root.Q("panel-lobby");
            if (_panelLan == null || _panelLobby == null)
                return; // older scene UXML; multiplayer simply stays hidden

            _lanGameList = root.Q("lan-games");
            _lanStatus = root.Q<Label>("lan-status");
            _lanName = root.Q<TextField>("lan-name");
            _lanAddress = root.Q<TextField>("lan-address");
            _lobbySlotList = root.Q("lobby-slots");
            _lobbyTitle = root.Q<Label>("lobby-title");
            _lobbyMap = root.Q<Label>("lobby-map");
            _lobbyStatus = root.Q<Label>("lobby-status");
            _lobbyStart = root.Q<Button>("lobby-start");
            _lobbyMapThumb = root.Q<Image>("lobby-map-thumb");

            _panelHostSetup = root.Q("panel-host-setup");
            _hostSetupMapLabel = root.Q<Label>("host-setup-map-label");
            _hostSetupThumb = root.Q<Image>("host-setup-thumb");
            _hostSetupStatus = root.Q<Label>("host-setup-status");
            _hostSetupNameField = root.Q<TextField>("host-setup-name");
            _hostSetupPlayersLabel = root.Q<Label>("host-setup-players-label");
            _hostSetupGameTypeBtn = root.Q<Button>("host-setup-gametype");
            _hostSetupSpeedBtn = root.Q<Button>("host-setup-speed");

            if (_lanName != null && string.IsNullOrWhiteSpace(_lanName.value))
                _lanName.value = System.Environment.UserName ?? "Player";

            root.Q<Button>("multiplayer").clicked += ShowLan;
            root.Q<Button>("lan-back").clicked += () => { LeaveNetworking(); ShowMain(); };
            root.Q<Button>("lan-host").clicked += () => ShowHostSetup(online: false);
            root.Q<Button>("lan-connect").clicked += () => JoinAddress(_lanAddress?.value);
            root.Q<Button>("host-setup-map-prev").clicked += () => StepHostMap(-1);
            root.Q<Button>("host-setup-map-next").clicked += () => StepHostMap(1);
            root.Q<Button>("host-setup-players-prev").clicked += () => StepHostPlayerCount(-1);
            root.Q<Button>("host-setup-players-next").clicked += () => StepHostPlayerCount(1);
            if (_hostSetupGameTypeBtn != null)
                _hostSetupGameTypeBtn.clicked += CycleHostSetupGameType;
            if (_hostSetupSpeedBtn != null)
                _hostSetupSpeedBtn.clicked += CycleHostSetupSpeed;
            root.Q<Button>("host-setup-create").clicked += CreateHostedGame;
            root.Q<Button>("host-setup-back").clicked += () =>
            {
                if (_hostSetupIsOnline) ShowOnline(); else ShowLan();
            };
            root.Q<Button>("lobby-leave").clicked += () =>
            {
                bool wasOnline = _lobbyIsOnline;
                LeaveNetworking();
                if (wasOnline) ShowOnline(); else ShowLan();
            };
            _lobbyStart.clicked += StartHostedMatch;
        }

        void HideLanPanels()
        {
            if (_panelLan != null)
                Show(_panelLan, false);
            if (_panelLobby != null)
                Show(_panelLobby, false);
            if (_panelOnline != null)
                Show(_panelOnline, false);
            if (_panelHostSetup != null)
                Show(_panelHostSetup, false);
        }

        // --- Browser -------------------------------------------------------------

        void ShowLan()
        {
            LeaveNetworking();
            _maps = MapList.Find(_paths);
            _lanMapSel = 0;

            Show(_panelMain, false);
            Show(_panelSetup, false);
            Show(_panelLobby, false);
            if (_panelOnline != null)
                Show(_panelOnline, false);
            if (_panelHostSetup != null)
                Show(_panelHostSetup, false);
            Show(_panelLan, true);

            try
            {
                _discovery = new LanDiscovery(listen: true);
                SetLanStatus("Listening for games on the local network…");
            }
            catch (System.Exception e)
            {
                // Almost always the firewall or a port already in use. Say so
                // rather than showing an empty list forever.
                SetLanStatus($"Could not listen for games: {e.Message}");
            }
            RebuildGameList();
        }

        void SetLanStatus(string text)
        {
            if (_lanStatus != null)
                _lanStatus.text = text;
        }

        void RebuildGameList()
        {
            if (_lanGameList == null)
                return;
            _lanGameList.Clear();

            var games = _discovery?.Games() ?? new List<LanGameInfo>();
            if (games.Count == 0)
            {
                var empty = new Label("No games found yet.") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList("text");
                empty.AddToClassList("text--dim");
                _lanGameList.Add(empty);
                return;
            }

            foreach (var game in games)
            {
                var info = game;
                string gameName = string.IsNullOrEmpty(info.GameName) ? $"{info.HostName}'s Game" : info.GameName;
                string label = $"{gameName}  ({info.HostName})  —  {info.MapName}  " +
                               $"({info.PlayersPresent}/{info.PlayersMax})  {info.Address}";
                var button = new Button(() => JoinAddress($"{info.Address}:{info.Port}")) { text = label };
                button.AddToClassList("menu__button");
                // A game announced by a different protocol build cannot be
                // joined; showing it greyed is friendlier than hiding it, since
                // "my friend's game isn't listed" is the confusing failure.
                bool compatible = info.ProtocolVersion == BuildIdentity.CurrentProtocolVersion;
                button.SetEnabled(compatible);
                if (!compatible)
                    button.text = label + "   [different version]";
                _lanGameList.Add(button);
            }
        }

        // --- Host setup (its own panel — not bolted onto panel-lan/panel-online) --

        /// <summary>Entered from either browser's "Host a Game" button.
        /// Remembers which flavor to create and which panel Back returns to.</summary>
        void ShowHostSetup(bool online)
        {
            _hostSetupIsOnline = online;
            _maps = MapList.Find(_paths);
            _lanMapSel = 0;
            _hostSetupGameType = LobbyGameType.Ffa;
            // Starts from the host's own saved single-player preference —
            // a sensible default they can still change, not always reset to
            // Normal.
            _hostSetupSpeedIndex = GameplaySettings.Current.speedIndex;
            if (_hostSetupNameField != null)
                _hostSetupNameField.value = $"{HostDisplayName()}'s Game";
            SetHostSetupStatus("");
            RefreshHostSetupGameTypeLabel();
            RefreshHostSetupSpeedLabel();
            RefreshHostMapLabel(); // also resets/recomputes the player-count cap

            Show(_panelMain, false);
            Show(_panelSetup, false);
            Show(_panelLan, false);
            if (_panelOnline != null)
                Show(_panelOnline, false);
            Show(_panelLobby, false);
            Show(_panelHostSetup, true);
        }

        void CreateHostedGame()
        {
            if (_hostSetupIsOnline) HostOnlineGame(); else HostGame();
        }

        void SetHostSetupStatus(string text)
        {
            if (_hostSetupStatus != null)
                _hostSetupStatus.text = text;
        }

        /// <summary>The name shown for the host's own seat and (LAN only) the
        /// beacon — LAN uses the name field on panel-lan, Online uses the
        /// logged-in username, never whatever the LAN field happens to hold.</summary>
        string HostDisplayName() => _hostSetupIsOnline ? OnlinePlayerName() : PlayerName();

        /// <summary>Step which map Host Setup will create. Mirrors the
        /// skirmish panel's Step().</summary>
        void StepHostMap(int delta)
        {
            if (_maps == null || _maps.Count == 0)
                return;
            _lanMapSel = (_lanMapSel + delta + _maps.Count) % _maps.Count;
            RefreshHostMapLabel();
        }

        void RefreshHostMapLabel()
        {
            string label = "";
            Texture2D thumb = null;
            PudFile pud = null;
            if (_maps != null && _maps.Count > 0)
            {
                var entry = _maps[Mathf.Clamp(_lanMapSel, 0, _maps.Count - 1)];
                label = entry.Label;
                pud = LoadPud(entry.Value);
                thumb = BakeThumbnailFromPud(pud, ThumbnailMaxDimension);
            }
            if (_hostSetupMapLabel != null) _hostSetupMapLabel.text = label;
            if (_hostSetupThumb != null) _hostSetupThumb.image = thumb;

            // A fresh map resets the player-count cap to its own full slot
            // count — carrying a stale cap across maps of different sizes
            // would either clamp harmlessly or silently leave seats
            // unusable, neither of which is obvious to the host.
            _hostSetupMapMaxSlots = CountPlayableSlots(pud);
            _hostSetupPlayerCount = _hostSetupMapMaxSlots;
            RefreshHostSetupPlayersLabel();
        }

        static int CountPlayableSlots(PudFile pud)
        {
            if (pud == null)
                return SimConstants.MaxPlayers;
            int n = 0;
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                if (MatchSetup.ControllerFor(pud.Owner[p]) != Controller.None)
                    n++;
            return n > 0 ? n : SimConstants.MaxPlayers;
        }

        /// <summary>How many seats the host wants playable, from 2 up to the
        /// map's own slot count — reducing it Closes the extra seats when the
        /// game is created (see BuildHostPayload).</summary>
        void StepHostPlayerCount(int delta)
        {
            int max = Mathf.Max(2, _hostSetupMapMaxSlots);
            _hostSetupPlayerCount = Mathf.Clamp(_hostSetupPlayerCount + delta, 2, max);
            RefreshHostSetupPlayersLabel();
        }

        void RefreshHostSetupPlayersLabel()
        {
            if (_hostSetupPlayersLabel != null)
                _hostSetupPlayersLabel.text = $"Players: {_hostSetupPlayerCount}";
        }

        void CycleHostSetupGameType()
        {
            _hostSetupGameType = _hostSetupGameType == LobbyGameType.Ffa ? LobbyGameType.Teams : LobbyGameType.Ffa;
            RefreshHostSetupGameTypeLabel();
        }

        void RefreshHostSetupGameTypeLabel()
        {
            if (_hostSetupGameTypeBtn != null)
                _hostSetupGameTypeBtn.text =
                    $"Game Type: {(_hostSetupGameType == LobbyGameType.Teams ? "Teams" : "Free For All")}";
        }

        void CycleHostSetupSpeed()
        {
            _hostSetupSpeedIndex = (_hostSetupSpeedIndex + 1) % GameplaySettings.SpeedLabels.Length;
            RefreshHostSetupSpeedLabel();
        }

        void RefreshHostSetupSpeedLabel()
        {
            if (_hostSetupSpeedBtn != null)
                _hostSetupSpeedBtn.text = $"Game Speed: {GameplaySettings.SpeedLabels[_hostSetupSpeedIndex]}";
        }

        /// <summary>Source resolution baked for the large host-setup/lobby
        /// previews — well above MapThumbnail's own 96px default so the
        /// enlarged on-screen size (~220-256px) doesn't look blocky.</summary>
        const int ThumbnailMaxDimension = 200;

        /// <summary>Terrain-only preview, no running GameSim needed — reads
        /// straight from an already-parsed PudFile, reusing whatever
        /// RuntimeTileCatalog this era already has cached.</summary>
        Texture2D BakeThumbnailFromPud(PudFile pud, int maxDimension)
        {
            if (pud == null)
                return null;
            var catalog = GetTileCatalog(pud.Era);
            return catalog != null ? MapThumbnail.Bake(pud, catalog, maxDimension) : null;
        }

        Texture2D BakeThumbnail(string mapValue, int maxDimension = 96) =>
            BakeThumbnailFromPud(LoadPud(mapValue), maxDimension);

        /// <summary>Best-effort room-browser thumbnail: RoomSummary carries only
        /// a bare map name, not a hash, so this matches by filename against the
        /// locally known map list. A same-named-but-different map would show a
        /// mismatched thumbnail — harmless (cosmetic only), unlike the real
        /// MapHash identity check the join handshake performs.</summary>
        Texture2D BakeThumbnailForRoomMap(string mapName)
        {
            if (_maps == null || string.IsNullOrEmpty(mapName))
                return null;
            string wanted = Path.GetFileNameWithoutExtension(mapName);
            foreach (var entry in _maps)
                if (Path.GetFileNameWithoutExtension(entry.Value) == wanted)
                    return BakeThumbnail(entry.Value, ThumbnailMaxDimension);
            return null;
        }

        RuntimeTileCatalog GetTileCatalog(PudEra era)
        {
            if (_tileCatalogCache.TryGetValue(era, out var cached))
                return cached;
            var source = AssetResolution.ResolveAssetSource(_paths, out _);
            var catalog = source != null ? RuntimeTileCatalog.Build(source, era) : null;
            if (catalog != null)
                _tileCatalogCache[era] = catalog;
            return catalog;
        }

        // --- Hosting ---------------------------------------------------------------

        void HostGame()
        {
            if (_maps == null || _maps.Count == 0)
            {
                SetHostSetupStatus("No .pud maps found to host.");
                return;
            }

            LeaveNetworking();
            try
            {
                _socket = UtpPeerSocket.Host();
            }
            catch (System.Exception e)
            {
                SetHostSetupStatus($"Could not host: {e.Message}");
                return;
            }

            var payload = BuildHostPayload();
            if (payload == null)
                return;

            _hostIdentity = BuildIdentityFor(payload.MapPath);
            _lobbyHost = new LobbyHost(_socket, _hostIdentity, payload, m => Debug.Log(m));
            _lobbyHost.Changed += () => _lobbyDirty = true;

            try
            {
                _discovery?.Dispose();
                _discovery = new LanDiscovery(listen: false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[craftwar-net] beacon unavailable: {e.Message}");
            }

            EnterLobby(isHost: true, online: false);
        }

        /// <summary>The host's opening roster: the map's playable seats, with
        /// the host in the first one and the rest left Open — waiting for
        /// people, not defaulted to the computer. The host resolves any that
        /// stay unclaimed (Closed or Computer) before Start unlocks.</summary>
        LobbyPayload BuildHostPayload()
        {
            var entry = _maps[Mathf.Clamp(_lanMapSel, 0, _maps.Count - 1)];
            var pud = LoadPud(entry.Value);
            if (pud == null)
            {
                SetHostSetupStatus($"Could not read {entry.Label}.");
                return null;
            }

            string roomName = _hostSetupNameField?.value?.Trim();
            if (string.IsNullOrEmpty(roomName))
                roomName = $"{HostDisplayName()}'s Game";

            var payload = new LobbyPayload
            {
                MapPath = entry.Value,
                RoomName = roomName,
                // The host picks the seed and everyone simulates from it. The
                // skirmish menu never set one, so every match ran seed 42.
                Seed = (ulong)Random.Range(1, int.MaxValue),
                TicksPerTurn = (byte)SimConstants.TicksPerCommandTurn,
                InputDelayTurns = 2,
                GameType = (byte)_hostSetupGameType,
                SpeedIndex = (byte)_hostSetupSpeedIndex,
            };

            // Cap at the host's chosen player count: seats beyond it are
            // Closed outright, not just left Open — "Players: 4" on an
            // 8-slot map must actually shrink the game, not just hide a
            // control the host would otherwise have to close by hand.
            // _cappedClosedSeats records exactly which ones, so the roster
            // can drop these entirely while still letting the host close
            // (and reopen) any OTHER seat normally, in-lobby, as before.
            _cappedClosedSeats.Clear();
            int cap = Mathf.Max(1, _hostSetupPlayerCount);
            bool hostSeated = false;
            int seated = 0;
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                if (MatchSetup.ControllerFor(pud.Owner[p]) == Controller.None)
                    continue;

                bool withinCap = seated < cap;
                seated++;

                LobbySeatStatus status;
                string name = "";
                if (!withinCap)
                {
                    status = LobbySeatStatus.Closed;
                    _cappedClosedSeats.Add(p);
                }
                else if (!hostSeated)
                {
                    status = LobbySeatStatus.Human;
                    name = HostDisplayName();
                }
                else
                    status = LobbySeatStatus.Open;

                payload.Slots[p] = new LobbySlot
                {
                    SeatStatus = (byte)status,
                    Race = pud.Side[p] == (byte)Race.Orc ? (byte)Race.Orc : (byte)Race.Human,
                    Team = (byte)p,
                    AiTier = (byte)Craftwar.Sim.Ai.AiTier.Normal,
                    Name = name,
                };
                if (withinCap && !hostSeated)
                {
                    NetSession.LocalSlot = (byte)p;
                    hostSeated = true;
                }
            }

            if (!hostSeated)
            {
                SetHostSetupStatus($"{entry.Label} has no playable seats.");
                return null;
            }
            return payload;
        }

        string PlayerName()
        {
            string name = _lanName?.value;
            return string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
        }

        // --- Joining -------------------------------------------------------------

        void JoinAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                SetLanStatus("Enter an address like 192.168.1.20 first.");
                return;
            }

            string host = address.Trim();
            ushort port = UtpPeerSocket.DefaultPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(host.Substring(colon + 1), out ushort parsed))
            {
                port = parsed;
                host = host.Substring(0, colon);
            }

            LeaveNetworking();
            try
            {
                _socket = UtpPeerSocket.Join(host, port);
            }
            catch (System.Exception e)
            {
                SetLanStatus($"Could not connect: {e.Message}");
                return;
            }

            // A joiner cannot hash the host's map before being told which map it
            // is, so the first request carries only the version fields. Once the
            // roster arrives we hash our own copy and confirm.
            _lobbyClient = new LobbyClient(_socket, BuildIdentityFor(null), PlayerName());
            _lobbyClient.Changed += OnLobbyChanged;
            _lobbyClient.Started += OnMatchStarted;

            EnterLobby(isHost: false, online: false);
        }

        void OnLobbyChanged()
        {
            _lobbyDirty = true;

            // The moment we know the host's map, prove we have the same copy of
            // it. If we do not, the host takes the seat back and tells us why —
            // far better than discovering it as a desync mid-match.
            if (_lobbyClient != null && _lobbyClient.Seated && _lobbyClient.Payload != null)
                _lobbyClient.ConfirmIdentity(BuildIdentityFor(_lobbyClient.Payload.MapPath));
        }

        // --- The room ------------------------------------------------------------

        void EnterLobby(bool isHost, bool online)
        {
            _lobbyIsOnline = online;
            HideLanPanels();
            Show(_panelLobby, true);
            if (_lobbyTitle != null)
                _lobbyTitle.text = isHost ? "Hosting" : "Lobby";
            // Start unlocks once every seat is resolved — RebuildLobby keeps
            // this current as the host toggles seats; set an initial (false)
            // value here so a fresh host lobby does not flash enabled first.
            _lobbyStart.SetEnabled(isHost && _lobbyHost != null && _lobbyHost.CanStart());
            Show(_lobbyStart, isHost);
            _lobbyDirty = true;
            RebuildLobby();
        }

        void RebuildLobby()
        {
            _lobbyDirty = false;
            if (_lobbySlotList == null)
                return;

            var payload = _lobbyHost?.Payload ?? _lobbyClient?.Payload;
            _lobbySlotList.Clear();

            if (payload == null)
            {
                if (_lobbyClient != null && _lobbyClient.Rejection != JoinRejectReason.None)
                    SetLobbyStatus(DescribeRejection(_lobbyClient.Rejection));
                else if (_lobbyClient != null && _lobbyClient.Disconnected)
                    SetLobbyStatus("Lost contact with the host.");
                else
                    SetLobbyStatus("Connecting…");
                return;
            }

            if (_lobbyTitle != null)
                _lobbyTitle.text = !string.IsNullOrEmpty(payload.RoomName)
                    ? payload.RoomName
                    : (_lobbyHost != null ? "Hosting" : "Lobby");

            if (_lobbyMap != null)
                _lobbyMap.text = $"Map: {Path.GetFileNameWithoutExtension(payload.MapPath)}";
            // Baked once per map, not on every roster change (RebuildLobby
            // runs any time a seat/team/status changes) — the map is fixed
            // for the lobby's lifetime.
            if (_lobbyMapThumb != null && _lobbyThumbFor != payload.MapPath)
            {
                _lobbyThumbFor = payload.MapPath;
                _lobbyMapThumb.image = BakeThumbnail(payload.MapPath, ThumbnailMaxDimension);
            }

            byte mySlot = _lobbyHost != null ? NetSession.LocalSlot : _lobbyClient?.MySlot ?? 0;
            bool isHost = _lobbyHost != null;
            _lobbySlotList.Add(BuildGameTypeRow(payload));
            for (int p = 0; p < payload.Slots.Length; p++)
            {
                ref LobbySlot slot = ref payload.Slots[p];
                // A seat closed at Host Setup by the player-count cap is
                // gone from the roster entirely, host included — it was
                // never part of this game. Hidden from joiners regardless of
                // reason (irrelevant to them either way). A seat the HOST
                // closes later, in-lobby, still shows to the host — that is
                // the one control that lets them reopen it, no restart
                // needed, same as before this feature existed.
                bool closed = slot.SeatStatus == (byte)LobbySeatStatus.Closed;
                bool hideFromHost = closed && _cappedClosedSeats.Contains(p);
                if ((closed && !isHost) || hideFromHost)
                    continue;
                _lobbySlotList.Add(BuildSeatRow(p, payload, mySlot, isHost));
            }

            if (_lobbyHost != null)
                SetLobbyStatus(_lobbyHost.CanStart()
                    ? $"{_lobbyHost.Payload.HumanCount()} of {_lobbyHost.Payload.PlayableCount()} seats taken."
                    : "Close or assign every Open seat before starting.");
            else if (_lobbyClient != null && _lobbyClient.Disconnected)
                SetLobbyStatus("Lost contact with the host.");
            else
                SetLobbyStatus("Waiting for the host to start…");

            if (_lobbyHost != null)
                _lobbyStart.SetEnabled(_lobbyHost.CanStart());
        }

        static readonly List<string> StatusChoices = new List<string> { "Closed", "Open", "Computer" };
        static readonly List<string> RaceChoices = new List<string> { "Human", "Orc" };

        /// <summary>Read-only display of the game type chosen at Host Setup
        /// (host-setup-gametype) — not editable here. Changing FFA/Teams
        /// after seats and teams have already been assigned in the lobby
        /// would just invite confusion; Host Setup already owns this
        /// choice, before there's a roster to disturb.</summary>
        static VisualElement BuildGameTypeRow(LobbyPayload payload)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;

            string typeText = (LobbyGameType)payload.GameType == LobbyGameType.Teams ? "Teams" : "Free For All";
            var label = new Label($"Game Type: {typeText}") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("text");
            row.Add(label);

            return row;
        }

        static List<string> TeamChoices()
        {
            var list = new List<string>(SimConstants.MaxPlayers);
            for (int i = 0; i < SimConstants.MaxPlayers; i++)
                list.Add($"Team {i + 1}");
            return list;
        }

        /// <summary>One roster row. Host-only interactive controls (status,
        /// race, team) are DropdownFields built in code, disabled (but still
        /// showing the current value) for anyone who isn't the host — lobby-
        /// slots is an empty container the whole roster is built into at
        /// runtime, same as before this change.</summary>
        VisualElement BuildSeatRow(int p, LobbyPayload payload, byte mySlot, bool isHost)
        {
            ref LobbySlot slot = ref payload.Slots[p];
            int seat = p;
            var row = new VisualElement();
            row.AddToClassList("list-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            string who = (LobbySeatStatus)slot.SeatStatus switch
            {
                LobbySeatStatus.Human => string.IsNullOrEmpty(slot.Name) ? "Player" : slot.Name,
                LobbySeatStatus.Computer => $"Computer ({(Craftwar.Sim.Ai.AiTier)slot.AiTier})",
                LobbySeatStatus.Closed => "Closed",
                _ => "Open",
            };
            if (p == mySlot)
                who += " (You)";

            // Ladder rating, online lobbies only (LAN has no server to ask).
            bool humanSeat = (LobbySeatStatus)slot.SeatStatus == LobbySeatStatus.Human;
            string seatName = slot.Name;
            if (humanSeat && _lobbyIsOnline)
            {
                RequestRatingIfNeeded(seatName);
                string ratingText = RatingLabelFor(seatName);
                if (!string.IsNullOrEmpty(ratingText))
                    who += $" — {ratingText}";
            }

            // Clickable directly — no separate button. A player's name IS
            // the control; clicking it opens the inspect popup. Only
            // meaningful for an online Human seat (LAN has no server to ask,
            // Computer/Open/Closed have no account to inspect).
            bool clickable = humanSeat && _lobbyIsOnline && !string.IsNullOrEmpty(seatName);
            var label = new Label(who) { pickingMode = clickable ? PickingMode.Position : PickingMode.Ignore };
            label.AddToClassList("text");
            // Grows/shrinks with whatever room the fixed-width controls to
            // its right leave behind, instead of a fixed width that — once
            // Strategy/Tier buttons are added for a Computer seat — no
            // longer fits inside the panel at all (the actual bug: the row
            // was simply wider than .menu--wide, so the last control ran
            // off the edge). Long names/ratings truncate instead of pushing
            // the row wider.
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.marginRight = 4;
            if (clickable)
                label.RegisterCallback<ClickEvent>(_ => ShowPlayerInspectPopup(seatName));
            row.Add(label);

            // A human-occupied seat's status only changes by that person
            // leaving, so there is nothing for the host to pick — showing a
            // dropdown here would have to lie (StatusChoices has no "Human"
            // entry to select), so occupied seats skip it entirely instead
            // of displaying a misleading "Open"/"Closed" value next to a
            // name that's clearly seated.
            bool statusEditable = isHost && !humanSeat;
            if (!humanSeat)
            {
                var statusField = new DropdownField
                {
                    choices = StatusChoices,
                    index = StatusChoices.IndexOf(StatusChoiceFor((LobbySeatStatus)slot.SeatStatus)),
                };
                HidePhantomLabel(statusField);
                statusField.style.width = 150;
                statusField.style.marginRight = 6;
                statusField.SetEnabled(statusEditable);
                if (statusEditable)
                    statusField.RegisterValueChangedCallback(e => SetSeatStatusFromChoice(seat, e.newValue));
                row.Add(statusField);
            }

            var raceField = new DropdownField
            {
                choices = RaceChoices,
                index = (Race)slot.Race == Race.Orc ? 1 : 0,
            };
            HidePhantomLabel(raceField);
            raceField.style.width = 130;
            raceField.style.marginRight = 6;
            raceField.SetEnabled(isHost);
            if (isHost)
                raceField.RegisterValueChangedCallback(e =>
                    _lobbyHost.SetSeatRace(seat, e.newValue == "Orc" ? Race.Orc : Race.Human));
            row.Add(raceField);

            var teamChoices = TeamChoices();
            var teamField = new DropdownField
            {
                choices = teamChoices,
                index = Mathf.Clamp(slot.Team, 0, teamChoices.Count - 1),
            };
            HidePhantomLabel(teamField);
            teamField.style.width = 130;
            teamField.style.marginRight = 6;
            teamField.SetEnabled(isHost);
            // Only meaningful once the host has switched to Teams — under Ffa
            // every seat is already forced to a unique team (SetGameType), so
            // the picker would just be redundant clutter.
            Show(teamField, (LobbyGameType)payload.GameType == LobbyGameType.Teams);
            if (isHost)
                teamField.RegisterValueChangedCallback(e =>
                    _lobbyHost.SetSeatTeam(seat, (byte)teamChoices.IndexOf(e.newValue)));
            row.Add(teamField);

            // Strategy/difficulty apply only to a Computer seat, mirroring the
            // skirmish panel's StratBtn/DiffBtn — auto-assigned the moment the
            // seat became Computer (see LobbyHost.SetSeatStatus), cyclable
            // afterward by the host.
            bool computerSeat = (LobbySeatStatus)slot.SeatStatus == LobbySeatStatus.Computer;

            var stratBtn = new Button(() => CycleSeatStrategy(seat))
            {
                text = string.IsNullOrEmpty(slot.Strategy) ? AiProfileLibrary.DefaultName : slot.Strategy,
            };
            stratBtn.AddToClassList("menu__button");
            stratBtn.style.width = 150;
            stratBtn.style.marginRight = 6;
            stratBtn.SetEnabled(isHost);
            Show(stratBtn, computerSeat);
            row.Add(stratBtn);

            var tierBtn = new Button(() => { if (isHost) _lobbyHost.CycleSeatTier(seat); })
            {
                text = ((Craftwar.Sim.Ai.AiTier)slot.AiTier).ToString(),
            };
            tierBtn.AddToClassList("menu__button");
            tierBtn.style.width = 110;
            tierBtn.SetEnabled(isHost);
            Show(tierBtn, computerSeat);
            row.Add(tierBtn);

            return row;
        }

        /// <summary>Next name in AiProfileLibrary.Names(), same rotation
        /// skirmish's CycleStrategy(SlotRow) uses — LobbyHost itself never
        /// needs to know the strategy list exists.</summary>
        void CycleSeatStrategy(int seat)
        {
            if (_lobbyHost == null) return;
            var names = StrategyNames();
            if (names == null || names.Count == 0) return;
            string current = _lobbyHost.Payload.Slots[seat].Strategy;
            if (string.IsNullOrEmpty(current)) current = AiProfileLibrary.DefaultName;
            int i = names.IndexOf(current);
            _lobbyHost.SetSeatStrategy(seat, names[(i + 1) % names.Count]);
        }

        List<string> StrategyNames()
        {
            if (_strategyNames == null || _strategyNames.Count == 0)
                _strategyNames = AiProfileLibrary.Names();
            return _strategyNames;
        }

        // --- Ladder ratings & inspect popup -------------------------------------

        /// <summary>Fire off a lookup at most once per name per lobby
        /// session — LateUpdate drains the result into _ratingCache.</summary>
        void RequestRatingIfNeeded(string username)
        {
            if (_onlineSocket == null || string.IsNullOrEmpty(username))
                return;
            if (_ratingCache.ContainsKey(username) || !_ratingRequested.Add(username))
                return;
            _onlineSocket.SendGetRating(username);
        }

        /// <summary>Label text for a seat/row's name, online lobbies only —
        /// blank while the lookup is still in flight rather than a
        /// misleading "Unranked".</summary>
        string RatingLabelFor(string username)
        {
            if (_onlineSocket == null || string.IsNullOrEmpty(username))
                return "";
            return _ratingCache.TryGetValue(username, out var r)
                ? LadderRank.Label(r.found, r.rating, r.games)
                : "…";
        }

        void DrainRatingResults()
        {
            if (_onlineSocket == null)
                return;
            while (_onlineSocket.TryReceiveGetRatingResult(out string username, out bool found,
                       out int rating, out int games))
                _ratingCache[username] = (found, rating, games);
        }

        /// <summary>A simple centered modal (not an anchored tooltip — no
        /// clamping/scroll-following logic needed for this), built in code
        /// like everything else dynamic in this file. Parented to the root so
        /// it overlays whichever panel is currently showing.</summary>
        void ShowInspectPopup(string title, string body)
        {
            HideInspectPopup();
            if (_root == null)
                return;

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.top = 0;
            overlay.style.right = 0;
            overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0, 0, 0, 0.6f);
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            var card = new VisualElement();
            card.AddToClassList("menu");
            card.style.width = 280;

            var titleLabel = new Label(title) { pickingMode = PickingMode.Ignore };
            titleLabel.AddToClassList("menu__title");
            card.Add(titleLabel);

            var bodyLabel = new Label(body) { pickingMode = PickingMode.Ignore };
            bodyLabel.AddToClassList("text");
            bodyLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(bodyLabel);

            var closeBtn = new Button(HideInspectPopup) { text = "Close" };
            closeBtn.AddToClassList("menu__button");
            card.Add(closeBtn);

            overlay.Add(card);
            _root.Add(overlay);
            _inspectPopup = overlay;
        }

        void HideInspectPopup()
        {
            _inspectPopup?.RemoveFromHierarchy();
            _inspectPopup = null;
        }

        void ShowPlayerInspectPopup(string username)
        {
            var r = _ratingCache.TryGetValue(username, out var cached) ? cached : (found: false, rating: 0, games: 0);
            string body = r.found
                ? $"Rating: {r.rating}\nGames played: {r.games}\nRank: {LadderRank.TitleFor(r.rating, r.games)}"
                : "This player has no online ladder rating.";
            ShowInspectPopup(username, body);
        }

        /// <summary>BaseField<T> always builds a caption Label even when no
        /// label text is ever set; the default theme reserves real width for
        /// it (sized for two-column Inspector rows), which is most of why
        /// these dropdowns rendered as a sliver of visible text. None of our
        /// DropdownFields use that caption, so removing it from layout
        /// entirely is safe here (unlike a blanket CSS rule, which would also
        /// hit TextFields that DO want their label, e.g. Username/Password).</summary>
        static void HidePhantomLabel(DropdownField field) =>
            field.labelElement.style.display = DisplayStyle.None;

        /// <summary>Only ever called for a non-Human seat — BuildSeatRow
        /// skips the status dropdown entirely for an occupied seat, since
        /// StatusChoices has no "Human" entry to honestly select.</summary>
        static string StatusChoiceFor(LobbySeatStatus status) => status switch
        {
            LobbySeatStatus.Open => "Open",
            LobbySeatStatus.Computer => "Computer",
            _ => "Closed",
        };

        void SetSeatStatusFromChoice(int seat, string choice)
        {
            if (_lobbyHost == null) return;
            var status = choice switch
            {
                "Open" => LobbySeatStatus.Open,
                "Computer" => LobbySeatStatus.Computer,
                _ => LobbySeatStatus.Closed,
            };
            _lobbyHost.SetSeatStatus(seat, status);
        }

        void SetLobbyStatus(string text)
        {
            if (_lobbyStatus != null)
                _lobbyStatus.text = text;
        }

        static string DescribeRejection(JoinRejectReason reason) => reason switch
        {
            JoinRejectReason.MapMismatch =>
                "That host is playing a different copy of the map. Map files must match exactly.",
            JoinRejectReason.RulesMismatch =>
                "The host's unit or upgrade data differs from yours.",
            JoinRejectReason.SimVersion or JoinRejectReason.ProtocolVersion =>
                "The host is running a different version of Craftwar.",
            JoinRejectReason.AiProfileMismatch =>
                "The host's computer-player profiles differ from yours.",
            JoinRejectReason.GameFull => "That game is full.",
            JoinRejectReason.AlreadyStarted => "That game has already started.",
            _ => "The host refused the connection.",
        };

        // --- Starting ------------------------------------------------------------

        void StartHostedMatch()
        {
            if (_lobbyHost == null)
                return;
            if (!_lobbyHost.StartMatch())
            {
                SetLobbyStatus("Close or assign every Open seat before starting.");
                return;
            }
            LaunchFrom(_lobbyHost.Payload, NetSession.LocalSlot, isHost: true,
                _lobbyHost.SlotByPeer);
        }

        void OnMatchStarted(LobbyPayload payload, byte mySlot) =>
            LaunchFrom(payload, mySlot, isHost: false, null);

        void LaunchFrom(LobbyPayload payload, byte localSlot, bool isHost,
            Dictionary<int, byte> slotByPeer)
        {
            // Hand the live socket to the game scene: reconnecting after the load
            // would throw away the agreement the lobby just reached. Whichever
            // transport actually negotiated this lobby (LAN or online) is the
            // one that's non-null here.
            NetSession.Socket = _socket != null ? (IPacketPeer)_socket : _onlineSocket;
            NetSession.IsHost = isHost;
            NetSession.LocalSlot = localSlot;
            NetSession.ParticipatingSlots = payload.ParticipatingSlots();
            NetSession.TicksPerTurn = payload.TicksPerTurn;
            NetSession.InputDelayTurns = payload.InputDelayTurns;
            // Host-chosen at Host Setup — was hardcoded to Normal for every
            // hosted match regardless of preference; every peer must use the
            // same value (see NetSession.SpeedMultiplier's own doc comment).
            NetSession.SpeedMultiplier = GameplaySettings.MultiplierForIndex(payload.SpeedIndex);
            NetSession.SlotByPeerId.Clear();
            if (slotByPeer != null)
                foreach (var pair in slotByPeer)
                    NetSession.SlotByPeerId[pair.Key] = pair.Value;

            // The socket now belongs to NetSession, so this menu must not close
            // it on the way out.
            _socket = null;
            _onlineSocket = null;
            _discovery?.Dispose();
            _discovery = null;

            StartMatch(ToMatchConfig(payload, localSlot));
        }

        static MatchConfig ToMatchConfig(LobbyPayload payload, byte localSlot)
        {
            // aiStrategy comes straight from the lobby's own picker now
            // (LobbySlot.Strategy) — "" for an untouched seat is still the
            // same sentinel AiProfileLibrary.Resolve treats as the built-in
            // land-attack profile, so behavior is unchanged wherever nothing
            // was cycled. aiType is the map's own AIPL byte, same as
            // StartSkirmish reads from _setupPud: a slot the map scripted as
            // passive/sea/air must keep behaving that way even when the host
            // promotes it to Computer.
            var pud = TryParse(ReadMapBytes(payload.MapPath));
            var config = new MatchConfig
            {
                mapPath = payload.MapPath,
                seed = payload.Seed,
                localSlot = localSlot,
                slots = new SlotConfig[SimConstants.MaxPlayers],
            };
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                ref LobbySlot slot = ref payload.Slots[p];
                config.slots[p] = new SlotConfig
                {
                    // Open never reaches here — StartMatch refuses while any
                    // seat is still Open, so only Human/Computer/Closed do.
                    controller = (LobbySeatStatus)slot.SeatStatus switch
                    {
                        LobbySeatStatus.Human => Controller.Human,
                        LobbySeatStatus.Computer => Controller.Computer,
                        _ => Controller.None,
                    },
                    race = (Race)slot.Race,
                    team = slot.Team,
                    aiTier = slot.AiTier,
                    aiStrategy = slot.Strategy ?? "",
                    aiType = pud != null ? pud.AiType[p] : (byte)0,
                };
            }
            return config;
        }

        // --- Plumbing ------------------------------------------------------------

        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup;

            if (_lobbyHost != null)
            {
                _lobbyHost.Poll();
                if (now >= _nextBeaconTime)
                {
                    _nextBeaconTime = now + 1f;
                    AnnounceHostedGame();
                }
            }
            _lobbyClient?.Poll();
            DrainRatingResults();

            if (_discovery != null && _lobbyHost == null && _lobbyClient == null)
            {
                _discovery.Poll(now);
                if (now >= _nextBrowserRefresh)
                {
                    _nextBrowserRefresh = now + 1f;
                    RebuildGameList();
                }
            }

            if (_lobbyDirty)
                RebuildLobby();
        }

        void AnnounceHostedGame()
        {
            if (_discovery == null || _lobbyHost == null)
                return;
            var payload = _lobbyHost.Payload;
            _discovery.Announce(new LanGameInfo
            {
                HostName = PlayerName(),
                GameName = payload.RoomName,
                MapName = Path.GetFileNameWithoutExtension(payload.MapPath),
                PlayersPresent = (byte)payload.HumanCount(),
                PlayersMax = (byte)payload.PlayableCount(),
                Port = UtpPeerSocket.DefaultPort,
                MapHash = _hostIdentity.MapHash,
            });
        }

        void LeaveNetworking()
        {
            _lobbyThumbFor = null;
            _cappedClosedSeats.Clear();
            _ratingCache.Clear();
            _ratingRequested.Clear();
            HideInspectPopup();
            _lobbyHost?.Dispose();
            _lobbyHost = null;
            _lobbyClient?.Dispose();
            _lobbyClient = null;
            _socket?.Dispose();
            _socket = null;
            _onlineSocket?.Dispose();
            _onlineSocket = null;
            _discovery?.Dispose();
            _discovery = null;
        }

        void OnDestroy()
        {
            LeaveNetworking();
            // Deliberately NOT CloseSocialConnection() here: OnDestroy fires
            // on every Menu<->Game scene transition (starting OR returning
            // from a match), which is exactly the lifetime OnlineSession
            // exists to survive — see its doc comment. Only the online
            // panel's explicit Back button logs out.
        }

        // --- Identity ------------------------------------------------------------

        /// <summary>
        /// The fingerprint two builds must share. Taken from the LIVE ruleset —
        /// after the map's own UDTA/UGRD overrides — because a custom-balance map
        /// would otherwise pass the handshake and desync later.
        /// </summary>
        BuildIdentity BuildIdentityFor(string mapPath)
        {
            var identity = new BuildIdentity
            {
                ProtocolVersion = BuildIdentity.CurrentProtocolVersion,
                SimVersion = SimConstants.SimVersion,
                // Strategies are named by string and resolved locally, so two
                // peers can resolve one name to different files; hashing the
                // profile itself is what catches that.
                AiProfileHash = Craftwar.Sim.Ai.BuiltinAiProfiles.Default.Hash(),
            };
            if (string.IsNullOrEmpty(mapPath))
                return identity;

            byte[] bytes = ReadMapBytes(mapPath);
            if (bytes == null)
                return identity;
            identity.MapHash = Replay.HashMapBytes(bytes);

            var pud = TryParse(bytes);
            if (pud == null)
                return identity;
            var rules = RuleSet.CreateDefault();
            rules.ApplyMapOverrides(pud);
            identity.RulesHash = rules.Hash();
            return identity;
        }

        static byte[] ReadMapBytes(string value)
        {
            try
            {
                string path = value.IndexOf(Path.DirectorySeparatorChar) >= 0
                              || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0
                    ? value
                    : Path.Combine(Application.streamingAssetsPath,
                        GameLoopRunner.MapsFolder, value);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        static PudFile TryParse(byte[] bytes)
        {
            try
            {
                return PudFile.Parse(bytes);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        PudFile LoadPud(string value)
        {
            byte[] bytes = ReadMapBytes(value);
            return bytes == null ? null : TryParse(bytes);
        }
    }
}
