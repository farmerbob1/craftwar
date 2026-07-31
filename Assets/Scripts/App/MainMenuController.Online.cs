using System;
using Craftwar.Net;
using Craftwar.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// The online half of the menu: log in to the relay server, browse or
    /// host rooms, and chat in the lobby. Deliberately reuses as much of the
    /// LAN half as possible — LobbyHost/LobbyClient/EnterLobby/RebuildLobby/
    /// StartHostedMatch/BuildHostPayload/BuildIdentityFor/ToMatchConfig all
    /// work off an IPacketPeer and a LobbyPayload, neither of which cares
    /// whether the transport underneath is UtpPeerSocket or RelayPeerSocket.
    /// The only genuinely new surface here is: authenticate, list/create/join
    /// a room on the server instead of a LAN broadcast, and chat (which rides
    /// its own control-plane messages, not the game protocol IPacketPeer
    /// carries — see RelayPeerSocket.SendChat).
    ///
    /// NOTE: Register/Login/ListRooms/RelayPeerSocket.Host/.Join are all
    /// synchronous, blocking calls (same convention UtpPeerSocket.Host/.Join
    /// already use for LAN). Over loopback that's sub-millisecond; against a
    /// real remote server with real latency this will hitch the menu's main
    /// thread for the round trip, with no cancel until the OS connect
    /// timeout. Acceptable for "running locally for now" (this session's
    /// explicit target); worth moving to a background thread + completion
    /// queue before a real deployment (M11 plan phase 6).
    /// </summary>
    public sealed partial class MainMenuController
    {
        VisualElement _panelOnline, _onlineLoginSection, _onlineBrowserSection, _onlineRoomsList;
        Label _onlineAuthStatus, _onlineWelcome, _onlineStatus;
        TextField _onlineServerField, _onlineUsernameField, _onlinePasswordField;

        ScrollView _lobbyChatLog;
        TextField _lobbyChatInput;
        Button _lobbyChatSend;

        RelayPeerSocket _onlineSocket;
        string _onlineSessionToken;
        string _onlineLoggedInUsername;
        float _nextOnlineRoomRefresh;

        const int DefaultOnlinePort = 27015;
        const int MaxChatLines = 50;

        void InitOnline(VisualElement root)
        {
            _panelOnline = root.Q("panel-online");
            if (_panelOnline == null)
                return; // older scene UXML; online multiplayer simply stays hidden

            _onlineLoginSection = root.Q("online-login");
            _onlineBrowserSection = root.Q("online-browser");
            _onlineRoomsList = root.Q("online-rooms");
            _onlineAuthStatus = root.Q<Label>("online-auth-status");
            _onlineWelcome = root.Q<Label>("online-welcome");
            _onlineStatus = root.Q<Label>("online-status");
            _onlineServerField = root.Q<TextField>("online-server");
            _onlineUsernameField = root.Q<TextField>("online-username");
            _onlinePasswordField = root.Q<TextField>("online-password");

            var savedLogin = OnlineLoginSettings.Current;
            if (_onlineServerField != null && !string.IsNullOrEmpty(savedLogin.server))
                _onlineServerField.value = savedLogin.server;
            if (_onlineUsernameField != null)
                _onlineUsernameField.value = savedLogin.username;
            if (_onlinePasswordField != null)
                _onlinePasswordField.value = savedLogin.password;

            // Shared with the LAN lobby room — see the UXML comment on
            // panel-lobby. Inert (nothing ever calls SendChat/TryReceiveChat)
            // for a LAN session, which has no chat channel.
            _lobbyChatLog = root.Q<ScrollView>("lobby-chat-log");
            _lobbyChatInput = root.Q<TextField>("lobby-chat-input");
            _lobbyChatSend = root.Q<Button>("lobby-chat-send");

            root.Q<Button>("multiplayer-online").clicked += ShowOnline;
            root.Q<Button>("online-back").clicked += () => { LeaveNetworking(); CloseSocialConnection(); ShowMain(); };
            root.Q<Button>("online-login-btn").clicked += () => Authenticate(register: false);
            root.Q<Button>("online-register-btn").clicked += () => Authenticate(register: true);
            root.Q<Button>("online-host").clicked += HostOnlineGame;
            root.Q<Button>("online-refresh").clicked += RefreshOnlineRooms;
            if (_lobbyChatSend != null)
                _lobbyChatSend.clicked += SendLobbyChat;
        }

        void Update()
        {
            if (_onlineSocket != null)
                while (_onlineSocket.TryReceiveChat(out string senderName, out string text))
                    AppendChatLine(senderName, text);

            UpdateSocial();

            bool browsing = _panelOnline != null && _panelOnline.style.display == DisplayStyle.Flex
                && _lobbyHost == null && _lobbyClient == null && _onlineSessionToken != null;
            if (!browsing)
                return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextOnlineRoomRefresh)
                return;
            _nextOnlineRoomRefresh = now + 3f;
            RefreshOnlineRooms();
        }

        // --- Panel flow ------------------------------------------------------------

        void ShowOnline()
        {
            LeaveNetworking();
            _maps = MapList.Find(_paths);
            _lanMapSel = 0;

            Show(_panelMain, false);
            Show(_panelSetup, false);
            Show(_panelLobby, false);
            if (_panelLan != null)
                Show(_panelLan, false);
            Show(_panelOnline, true);
            ShowOnlineSection(loggedIn: _onlineSessionToken != null);
            SetOnlineStatus("");
        }

        void ShowOnlineSection(bool loggedIn)
        {
            Show(_onlineLoginSection, !loggedIn);
            Show(_onlineBrowserSection, loggedIn);
            ShowSocialSection(loggedIn);
            if (loggedIn && _onlineWelcome != null)
                _onlineWelcome.text = $"Logged in as {_onlineLoggedInUsername}";
            if (loggedIn)
                RefreshOnlineRooms();
        }

        // --- Auth --------------------------------------------------------------

        void Authenticate(bool register)
        {
            if (!TryParseServerAddress(out string host, out int port))
                return;
            string username = _onlineUsernameField?.value?.Trim();
            string password = _onlinePasswordField?.value;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                SetAuthStatus("Enter a username and password.");
                return;
            }

            try
            {
                if (register)
                {
                    var result = OnlineAccountClient.Register(host, port, username, password);
                    if (result != AccountResult.Ok)
                    {
                        SetAuthStatus(DescribeAccountResult(result));
                        return;
                    }
                    // Fall through to log in with the same credentials —
                    // one click to a usable session instead of two.
                }

                var login = OnlineAccountClient.Login(host, port, username, password, out string token);
                if (login != AccountResult.Ok)
                {
                    SetAuthStatus(DescribeAccountResult(login));
                    return;
                }

                _onlineSessionToken = token;
                _onlineLoggedInUsername = username;
                ShowOnlineSection(loggedIn: true);
                OpenSocialConnection(host, port);

                var savedLogin = OnlineLoginSettings.Current;
                savedLogin.server = _onlineServerField?.value ?? savedLogin.server;
                savedLogin.username = username;
                savedLogin.password = password;
                OnlineLoginSettings.Save();
            }
            catch (Exception e)
            {
                SetAuthStatus($"Could not reach the server: {e.Message}");
            }
        }

        static string DescribeAccountResult(AccountResult result) => result switch
        {
            AccountResult.UsernameTaken => "That username is already taken.",
            AccountResult.InvalidUsername => "Usernames are 3-24 letters, digits, - or _.",
            AccountResult.WeakPassword => "Password must be at least 8 characters.",
            AccountResult.WrongCredentials => "Wrong username or password.",
            AccountResult.SessionExpired => "Session expired — log in again.",
            _ => "The server refused that.",
        };

        // --- Browser -------------------------------------------------------------

        void RefreshOnlineRooms()
        {
            // Every caller of this — the login-time refresh in
            // ShowOnlineSection AND the recurring poll in Update() — shares
            // one "next refresh due" clock, or the two race: logging in
            // fires this explicitly, then Update()'s own 3-second check
            // (never having been pushed out by that first call) fires again
            // on the very next frame, opening a second short-lived
            // connection right on top of the first.
            _nextOnlineRoomRefresh = Time.realtimeSinceStartup + 3f;

            if (_onlineRoomsList == null || !TryParseServerAddress(out string host, out int port))
                return;

            RoomSummary[] rooms;
            try
            {
                rooms = OnlineAccountClient.ListRooms(host, port);
            }
            catch (Exception e)
            {
                SetOnlineStatus($"Could not list games: {e.Message}");
                return;
            }

            _onlineRoomsList.Clear();
            if (rooms.Length == 0)
            {
                var empty = new Label("No games found. Host one!") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList("text");
                empty.AddToClassList("text--dim");
                _onlineRoomsList.Add(empty);
                SetOnlineStatus("");
                return;
            }

            foreach (var room in rooms)
            {
                string roomId = room.RoomId;
                string label = $"{room.HostName}  —  {System.IO.Path.GetFileNameWithoutExtension(room.MapName)}  " +
                               $"({room.PlayerCount}/{room.MaxPlayers})";
                var button = new Button(() => JoinOnlineRoom(roomId)) { text = label };
                button.AddToClassList("menu__button");
                button.SetEnabled(room.PlayerCount < room.MaxPlayers);
                if (room.PlayerCount >= room.MaxPlayers)
                    button.text = label + "   [full]";
                _onlineRoomsList.Add(button);
            }
            SetOnlineStatus("");
        }

        // --- Hosting -------------------------------------------------------------

        void HostOnlineGame()
        {
            if (!TryParseServerAddress(out string host, out int port))
                return;
            if (_maps == null || _maps.Count == 0)
            {
                SetOnlineStatus("No .pud maps found to host.");
                return;
            }

            LeaveNetworking();

            var payload = BuildHostPayload();
            if (payload == null)
                return; // BuildHostPayload already set a status message via SetLanStatus

            try
            {
                _onlineSocket = RelayPeerSocket.Host(host, port, payload.MapPath, OnlinePlayerName(),
                    SimConstants.MaxPlayers);
            }
            catch (Exception e)
            {
                SetOnlineStatus($"Could not host: {e.Message}");
                return;
            }

            _hostIdentity = BuildIdentityFor(payload.MapPath);
            _lobbyHost = new LobbyHost(_onlineSocket, _hostIdentity, payload, m => Debug.Log(m));
            _lobbyHost.Changed += () => _lobbyDirty = true;

            EnterLobby(isHost: true, online: true);
        }

        // --- Joining -------------------------------------------------------------

        void JoinOnlineRoom(string roomId)
        {
            if (!TryParseServerAddress(out string host, out int port))
                return;

            LeaveNetworking();

            try
            {
                _onlineSocket = RelayPeerSocket.Join(host, port, roomId, OnlinePlayerName());
            }
            catch (Exception e)
            {
                SetOnlineStatus($"Could not join: {e.Message}");
                return;
            }

            _lobbyClient = new LobbyClient(_onlineSocket, BuildIdentityFor(null), OnlinePlayerName());
            _lobbyClient.Changed += OnLobbyChanged;
            _lobbyClient.Started += OnMatchStarted;

            EnterLobby(isHost: false, online: true);
        }

        // --- Chat ------------------------------------------------------------------

        void SendLobbyChat()
        {
            if (_onlineSocket == null || _lobbyChatInput == null)
                return;
            string text = _lobbyChatInput.value?.Trim();
            if (string.IsNullOrEmpty(text))
                return;
            _onlineSocket.SendChat(OnlinePlayerName(), text);
            _lobbyChatInput.value = "";
        }

        void AppendChatLine(string senderName, string text)
        {
            if (_lobbyChatLog == null)
                return;
            var line = new Label($"{senderName}: {text}") { pickingMode = PickingMode.Ignore };
            line.AddToClassList("text");
            line.style.whiteSpace = WhiteSpace.Normal;
            _lobbyChatLog.Add(line);
            while (_lobbyChatLog.childCount > MaxChatLines)
                _lobbyChatLog.RemoveAt(0);
            _lobbyChatLog.scrollOffset = new Vector2(0, float.MaxValue);
        }

        // --- Plumbing ------------------------------------------------------------

        string OnlinePlayerName() =>
            !string.IsNullOrWhiteSpace(_onlineLoggedInUsername) ? _onlineLoggedInUsername
            : !string.IsNullOrWhiteSpace(_onlineUsernameField?.value) ? _onlineUsernameField.value.Trim()
            : "Player";

        bool TryParseServerAddress(out string host, out int port)
        {
            host = null;
            port = DefaultOnlinePort;
            string raw = _onlineServerField?.value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                SetOnlineStatus("Enter a server address like 127.0.0.1:27015.");
                return false;
            }

            raw = raw.Trim();
            int colon = raw.LastIndexOf(':');
            if (colon > 0 && int.TryParse(raw.Substring(colon + 1), out int parsedPort))
            {
                host = raw.Substring(0, colon);
                port = parsedPort;
            }
            else
            {
                host = raw;
            }
            return true;
        }

        void SetOnlineStatus(string text)
        {
            if (_onlineStatus != null)
                _onlineStatus.text = text;
        }

        void SetAuthStatus(string text)
        {
            if (_onlineAuthStatus != null)
                _onlineAuthStatus.text = text;
        }
    }
}
