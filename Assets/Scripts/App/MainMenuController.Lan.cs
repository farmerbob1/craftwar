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
            root.Q<Button>("lobby-leave").clicked += () => { LeaveNetworking(); ShowLan(); };
            _lobbyStart.clicked += StartHostedMatch;
        }

        void HideLanPanels()
        {
            if (_panelLan != null)
                Show(_panelLan, false);
            if (_panelLobby != null)
                Show(_panelLobby, false);
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

            EnterLobby(isHost: true);
        }

        /// <summary>The host's opening roster: the map's playable seats, with the
        /// host in the first one and the rest as computer players until people
        /// take them.</summary>
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
                    Controller = (byte)(hostSeated ? Controller.Computer : Controller.Human),
                    Race = pud.Side[p] == (byte)Race.Orc ? (byte)Race.Orc : (byte)Race.Human,
                    Team = (byte)p,
                    AiTier = (byte)Craftwar.Sim.Ai.AiTier.Normal,
                    Human = !hostSeated,
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

            EnterLobby(isHost: false);
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

        void EnterLobby(bool isHost)
        {
            Show(_panelLan, false);
            Show(_panelLobby, true);
            if (_lobbyTitle != null)
                _lobbyTitle.text = isHost ? "Hosting" : "Lobby";
            _lobbyStart.SetEnabled(isHost);
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
            for (int p = 0; p < payload.Slots.Length; p++)
            {
                ref LobbySlot slot = ref payload.Slots[p];
                if (slot.Controller == (byte)Controller.None)
                    continue;

                string who = slot.Human
                    ? (string.IsNullOrEmpty(slot.Name) ? "Player" : slot.Name)
                    : "Computer";
                if (p == mySlot)
                    who += "  (You)";
                var line = new Label($"Seat {p + 1}:  {who}   [{(Race)slot.Race}]")
                {
                    pickingMode = PickingMode.Ignore,
                };
                line.AddToClassList("text");
                _lobbySlotList.Add(line);
            }

            if (_lobbyHost != null)
                SetLobbyStatus($"{_lobbyHost.Payload.HumanCount()} of " +
                               $"{_lobbyHost.Payload.PlayableCount()} seats taken. " +
                               "Empty seats play as the computer.");
            else if (_lobbyClient != null && _lobbyClient.Disconnected)
                SetLobbyStatus("Lost contact with the host.");
            else
                SetLobbyStatus("Waiting for the host to start…");
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
            _lobbyHost.StartMatch();
            LaunchFrom(_lobbyHost.Payload, NetSession.LocalSlot, isHost: true,
                _lobbyHost.SlotByPeer);
        }

        void OnMatchStarted(LobbyPayload payload, byte mySlot) =>
            LaunchFrom(payload, mySlot, isHost: false, null);

        void LaunchFrom(LobbyPayload payload, byte localSlot, bool isHost,
            Dictionary<int, byte> slotByPeer)
        {
            // Hand the live socket to the game scene: reconnecting after the load
            // would throw away the agreement the lobby just reached.
            NetSession.Socket = _socket;
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
            _discovery?.Dispose();
            _discovery = null;

            StartMatch(ToMatchConfig(payload, localSlot));
        }

        static MatchConfig ToMatchConfig(LobbyPayload payload, byte localSlot)
        {
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
                    // A seat nobody took plays as the computer, so an empty seat
                    // is an opponent rather than a hole in the match.
                    controller = (Controller)slot.Controller,
                    race = (Race)slot.Race,
                    team = slot.Team,
                    aiTier = slot.AiTier,
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
            _discovery?.Dispose();
            _discovery = null;
        }

        void OnDestroy() => LeaveNetworking();

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
