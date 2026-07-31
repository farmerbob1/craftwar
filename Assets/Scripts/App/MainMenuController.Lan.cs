using System.Collections.Generic;
using System.IO;
using Craftwar.Net;
using Craftwar.Net.Unity;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
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
        VisualElement _panelLan, _panelLobby, _lanGameList, _lobbySlotList;
        Label _lanStatus, _lobbyTitle, _lobbyMap, _lobbyStatus;
        TextField _lanName, _lanAddress;
        Button _lobbyStart;

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

            if (_lanName != null && string.IsNullOrWhiteSpace(_lanName.value))
                _lanName.value = System.Environment.UserName ?? "Player";

            root.Q<Button>("multiplayer").clicked += ShowLan;
            root.Q<Button>("lan-back").clicked += () => { LeaveNetworking(); ShowMain(); };
            root.Q<Button>("lan-host").clicked += HostGame;
            root.Q<Button>("lan-connect").clicked += () => JoinAddress(_lanAddress?.value);
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
                string label = $"{info.HostName}  —  {info.MapName}  " +
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

        // --- Hosting -------------------------------------------------------------

        void HostGame()
        {
            if (_maps == null || _maps.Count == 0)
            {
                SetLanStatus("No .pud maps found to host.");
                return;
            }

            LeaveNetworking();
            try
            {
                _socket = UtpPeerSocket.Host();
            }
            catch (System.Exception e)
            {
                SetLanStatus($"Could not host: {e.Message}");
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
                SetLanStatus($"Could not read {entry.Label}.");
                return null;
            }

            var payload = new LobbyPayload
            {
                MapPath = entry.Value,
                // The host picks the seed and everyone simulates from it. The
                // skirmish menu never set one, so every match ran seed 42.
                Seed = (ulong)Random.Range(1, int.MaxValue),
                TicksPerTurn = (byte)SimConstants.TicksPerCommandTurn,
                InputDelayTurns = 2,
            };

            bool hostSeated = false;
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                if (MatchSetup.ControllerFor(pud.Owner[p]) == Controller.None)
                    continue;
                payload.Slots[p] = new LobbySlot
                {
                    SeatStatus = (byte)(hostSeated ? LobbySeatStatus.Open : LobbySeatStatus.Human),
                    Race = pud.Side[p] == (byte)Race.Orc ? (byte)Race.Orc : (byte)Race.Human,
                    Team = (byte)p,
                    AiTier = (byte)Craftwar.Sim.Ai.AiTier.Normal,
                    Name = hostSeated ? "" : PlayerName(),
                };
                if (!hostSeated)
                {
                    NetSession.LocalSlot = (byte)p;
                    hostSeated = true;
                }
            }

            if (!hostSeated)
            {
                SetLanStatus($"{entry.Label} has no playable seats.");
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

            if (_lobbyMap != null)
                _lobbyMap.text = $"Map: {Path.GetFileNameWithoutExtension(payload.MapPath)}";

            byte mySlot = _lobbyHost != null ? NetSession.LocalSlot : _lobbyClient?.MySlot ?? 0;
            bool isHost = _lobbyHost != null;
            for (int p = 0; p < payload.Slots.Length; p++)
            {
                ref LobbySlot slot = ref payload.Slots[p];
                // Hidden from joiners (irrelevant to them, keeps the roster
                // uncluttered) but always shown to the host — otherwise
                // closing a seat removes the only control that can reopen
                // it, with no way back short of restarting the lobby.
                if (!isHost && slot.SeatStatus == (byte)LobbySeatStatus.Closed)
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
                who += "  (You)";
            var label = new Label($"Seat {p + 1}:  {who}") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("text");
            label.style.width = 180;
            label.style.flexShrink = 0;
            row.Add(label);

            // A human-occupied seat's status only changes by that person
            // leaving; the host can only toggle a seat nobody is sitting in.
            // Race/team stay host-controlled regardless of who is seated —
            // not self-service, matching SetSeatTeam's existing behavior.
            bool statusEditable = isHost && (LobbySeatStatus)slot.SeatStatus != LobbySeatStatus.Human;
            var statusField = new DropdownField
            {
                choices = StatusChoices,
                index = StatusChoices.IndexOf(StatusChoiceFor((LobbySeatStatus)slot.SeatStatus)),
            };
            HidePhantomLabel(statusField);
            statusField.style.width = 130;
            statusField.style.marginRight = 4;
            statusField.SetEnabled(statusEditable);
            if (statusEditable)
                statusField.RegisterValueChangedCallback(e => SetSeatStatusFromChoice(seat, e.newValue));
            row.Add(statusField);

            var raceField = new DropdownField
            {
                choices = RaceChoices,
                index = (Race)slot.Race == Race.Orc ? 1 : 0,
            };
            HidePhantomLabel(raceField);
            raceField.style.width = 130;
            raceField.style.marginRight = 4;
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
            teamField.SetEnabled(isHost);
            if (isHost)
                teamField.RegisterValueChangedCallback(e =>
                    _lobbyHost.SetSeatTeam(seat, (byte)teamChoices.IndexOf(e.newValue)));
            row.Add(teamField);

            return row;
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

        static string StatusChoiceFor(LobbySeatStatus status) => status switch
        {
            LobbySeatStatus.Open => "Open",
            LobbySeatStatus.Computer => "Computer",
            LobbySeatStatus.Human => "Open", // never actually shown editable, see statusEditable
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
            NetSession.SpeedMultiplier = 1f;
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
            // The lobby never offers a strategy picker (LobbySlot carries no
            // such field), so aiStrategy stays "" — SlotConfig's own default,
            // which AiProfileLibrary.Resolve treats as the same built-in
            // land-attack profile StartSkirmish falls back to. aiType is the
            // map's own AIPL byte, same as StartSkirmish reads from _setupPud:
            // a slot the map scripted as passive/sea/air must keep behaving
            // that way even when the host promotes it to Computer.
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
                MapName = Path.GetFileNameWithoutExtension(payload.MapPath),
                PlayersPresent = (byte)payload.HumanCount(),
                PlayersMax = (byte)payload.PlayableCount(),
                Port = UtpPeerSocket.DefaultPort,
                MapHash = _hostIdentity.MapHash,
            });
        }

        void LeaveNetworking()
        {
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
            CloseSocialConnection();
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
