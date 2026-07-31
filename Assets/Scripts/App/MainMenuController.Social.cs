using System;
using System.Collections.Generic;
using Craftwar.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// The social layer: chat channels (M12 phase 1), plus channel MOTD and
    /// friends/presence/whispers (M13 follow-up, per the user's explicit
    /// request — M12 originally scoped these but only shipped chat
    /// channels). Owns a single <see cref="SocialClient"/> connection,
    /// opened right after a successful login in
    /// <see cref="MainMenuController.Online"/>'s Authenticate() and closed on
    /// logout/app teardown — deliberately independent of whatever
    /// <c>panel-lobby</c>/<c>_onlineSocket</c> is doing (hosting, browsing,
    /// in a room, or nothing at all): see the M12 plan's mechanism note 2.
    /// Same partial-class pattern as <c>.Lan.cs</c>/<c>.Online.cs</c>.
    ///
    /// Friend presence is polled, not pushed (<see cref="_nextFriendListRefresh"/>),
    /// matching the M12 plan's settled "presence is poll-on-demand" decision —
    /// request/accept/remove events DO push (see SocialClient's doc comments),
    /// but online/offline status itself only updates on the next poll.
    /// </summary>
    public sealed partial class MainMenuController
    {
        VisualElement _onlineSocial;
        ScrollView _socialMembers, _socialChatLog;
        Label _socialChannelLabel, _socialMotdLabel;
        TextField _socialChatInput;
        Button _socialChatSendBtn;

        VisualElement _socialMotdEditRow;
        TextField _socialMotdInput;
        Button _socialMotdSetBtn;

        TextField _friendAddInput;
        Button _friendAddBtn;
        Label _friendAddStatus;
        ScrollView _friendsList;

        SocialClient _socialClient;
        string _currentChannelName, _currentChannelOp, _currentChannelMotd = "";
        readonly List<string> _currentChannelMembers = new List<string>();

        readonly List<string> _friendUsernames = new List<string>();
        readonly HashSet<string> _friendsOnline = new HashSet<string>();
        readonly List<string> _incomingRequests = new List<string>();
        readonly List<string> _outgoingRequests = new List<string>();
        float _nextFriendListRefresh;
        const float FriendListRefreshInterval = 5f;

        const int MaxSocialChatLines = 50;

        void InitSocial(VisualElement root)
        {
            _onlineSocial = root.Q("online-social");
            if (_onlineSocial == null)
                return; // older scene UXML; the social layer simply stays hidden

            _socialChannelLabel = root.Q<Label>("social-channel-label");
            _socialMotdLabel = root.Q<Label>("social-motd");
            _socialMotdEditRow = root.Q("social-motd-edit-row");
            _socialMotdInput = root.Q<TextField>("social-motd-input");
            _socialMotdSetBtn = root.Q<Button>("social-motd-set");
            _socialMembers = root.Q<ScrollView>("social-members");
            _socialChatLog = root.Q<ScrollView>("social-chat-log");
            _socialChatInput = root.Q<TextField>("social-chat-input");
            _socialChatSendBtn = root.Q<Button>("social-chat-send");
            if (_socialChatSendBtn != null)
                _socialChatSendBtn.clicked += SendSocialChat;
            if (_socialMotdSetBtn != null)
                _socialMotdSetBtn.clicked += SendSetChannelMotd;
            Show(_socialMotdEditRow, false);

            _friendAddInput = root.Q<TextField>("friend-add-input");
            _friendAddBtn = root.Q<Button>("friend-add-send");
            _friendAddStatus = root.Q<Label>("friend-add-status");
            _friendsList = root.Q<ScrollView>("friends-list");
            if (_friendAddBtn != null)
                _friendAddBtn.clicked += SendFriendRequestFromInput;

            Show(_onlineSocial, false);
        }

        void OpenSocialConnection(string host, int port)
        {
            CloseSocialConnection();
            if (string.IsNullOrEmpty(_onlineSessionToken))
                return;
            try
            {
                _socialClient = SocialClient.Connect(host, port, _onlineSessionToken);
                OnlineSession.Social = _socialClient;
                _nextFriendListRefresh = 0f; // request one on the very next Update
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Craftwar] Could not connect to chat: {e.Message}");
                _socialClient = null;
            }
        }

        /// <summary>Picks up a SocialClient that survived a scene reload
        /// (see OnlineSession) instead of opening a new connection. The
        /// connection never actually left its channel, but THIS instance's
        /// cached roster/MOTD/member state starts empty, so force a fresh
        /// ChannelJoinResult by rejoining the same channel — a harmless
        /// leave+immediately-rejoin blip to other members, not a real
        /// departure, and the cheapest way to resync without a dedicated
        /// "get my current channel state" wire message.</summary>
        void AdoptSocialConnection(SocialClient existing)
        {
            _socialClient = existing;
            _nextFriendListRefresh = 0f;
            existing.JoinChannel(OnlineSession.CurrentChannel);
        }

        /// <summary>Explicit logout only (the online panel's Back button) —
        /// see OnlineSession's doc comment on why this must never be called
        /// from OnDestroy.</summary>
        void CloseSocialConnection()
        {
            OnlineSession.Clear(); // disposes the SocialClient
            _socialClient = null;
            _currentChannelName = null;
            _currentChannelOp = null;
            _currentChannelMotd = "";
            _currentChannelMembers.Clear();
            _friendUsernames.Clear();
            _friendsOnline.Clear();
            _incomingRequests.Clear();
            _outgoingRequests.Clear();
            _socialMembers?.Clear();
            _socialChatLog?.Clear();
            _friendsList?.Clear();
            if (_socialChannelLabel != null)
                _socialChannelLabel.text = "";
            if (_socialMotdLabel != null)
                _socialMotdLabel.text = "";
            Show(_socialMotdEditRow, false);
        }

        void UpdateSocial()
        {
            if (_socialClient == null)
                return;

            while (_socialClient.TryReceiveChannelJoinResult(out bool ok, out string reason, out string channelName,
                       out string[] members, out string opUsername, out string motd))
            {
                if (!ok)
                {
                    AppendSocialLine($"(could not join {channelName}: {reason})");
                    continue;
                }
                _currentChannelName = channelName;
                _currentChannelOp = opUsername;
                _currentChannelMotd = motd ?? "";
                _currentChannelMembers.Clear();
                _currentChannelMembers.AddRange(members);
                OnlineSession.CurrentChannel = channelName; // survives the next scene reload
                RebuildSocialMembers();
                UpdateChannelLabel();
                UpdateMotdDisplay();
            }

            while (_socialClient.TryReceiveChannelMemberEvent(out string eventChannel, out string username,
                       out bool joined, out string opUsername))
            {
                if (eventChannel != _currentChannelName)
                    continue; // an event about a channel we've since left — stale, ignore

                _currentChannelOp = opUsername;
                if (joined)
                {
                    if (!_currentChannelMembers.Contains(username))
                        _currentChannelMembers.Add(username);
                    AppendSocialLine($"* {username} has joined the channel");
                }
                else
                {
                    _currentChannelMembers.Remove(username);
                    AppendSocialLine($"* {username} has left the channel");
                }
                RebuildSocialMembers();
                UpdateChannelLabel();
                UpdateMotdDisplay(); // op may have migrated
            }

            while (_socialClient.TryReceiveChannelChat(out string chatChannel, out string senderUsername,
                       out string text))
                AppendSocialLine($"{senderUsername}: {text}");

            while (_socialClient.TryReceiveChannelKickResult(out bool kickOk, out string kickReason))
                if (!kickOk)
                    AppendSocialLine($"(kick failed: {kickReason})");

            while (_socialClient.TryReceiveChannelKicked(out string kickedChannel, out string byUsername))
                AppendSocialLine($"*** You were kicked from {kickedChannel} by {byUsername}");

            while (_socialClient.TryReceiveChannelSetMotdResult(out bool motdOk, out string motdReason))
                if (!motdOk)
                    AppendSocialLine($"(could not set MOTD: {motdReason})");

            while (_socialClient.TryReceiveChannelMotdChanged(out string motdChannel, out string newMotd))
            {
                if (motdChannel != _currentChannelName)
                    continue;
                _currentChannelMotd = newMotd ?? "";
                UpdateMotdDisplay();
                AppendSocialLine($"* MOTD: {_currentChannelMotd}");
            }

            while (_socialClient.TryReceiveFriendRequestResult(out bool freqOk, out string freqReason,
                       out bool becameFriends))
            {
                if (!freqOk)
                    SetFriendAddStatus(freqReason);
                else
                {
                    SetFriendAddStatus(becameFriends ? "You are now friends." : "Friend request sent.");
                    RequestFriendListNow();
                }
            }

            while (_socialClient.TryReceiveFriendRequestReceived(out string fromUsername))
            {
                AppendSocialLine($"* {fromUsername} sent you a friend request");
                RequestFriendListNow();
            }

            while (_socialClient.TryReceiveFriendRespondResult(out bool frespOk, out string frespReason))
                if (!frespOk)
                    AppendSocialLine($"(could not respond to friend request: {frespReason})");

            while (_socialClient.TryReceiveFriendRequestAnswered(out string answeredBy, out bool accepted))
            {
                AppendSocialLine(accepted
                    ? $"* {answeredBy} accepted your friend request"
                    : $"* {answeredBy} declined your friend request");
                RequestFriendListNow();
            }

            while (_socialClient.TryReceiveFriendRemoveResult(out bool fremOk, out string fremReason))
                if (!fremOk)
                    AppendSocialLine($"(could not remove friend: {fremReason})");

            while (_socialClient.TryReceiveFriendRemoved(out string removedBy))
            {
                AppendSocialLine($"* {removedBy} removed you as a friend");
                RequestFriendListNow();
            }

            while (_socialClient.TryReceiveFriendListResult(out string[] friendUsernames, out bool[] friendOnline,
                       out string[] incoming, out string[] outgoing))
            {
                _friendUsernames.Clear();
                _friendUsernames.AddRange(friendUsernames);
                _friendsOnline.Clear();
                for (int i = 0; i < friendUsernames.Length; i++)
                    if (friendOnline[i])
                        _friendsOnline.Add(friendUsernames[i]);
                _incomingRequests.Clear();
                _incomingRequests.AddRange(incoming);
                _outgoingRequests.Clear();
                _outgoingRequests.AddRange(outgoing);
                RebuildFriendsList();
            }

            while (_socialClient.TryReceiveWhisperResult(out bool whispOk, out string whispReason))
                if (!whispOk)
                    AppendSocialLine($"(whisper failed: {whispReason})");

            while (_socialClient.TryReceiveWhisperReceived(out string whispFrom, out string whispTo,
                       out string whispText))
            {
                AppendSocialLine(whispFrom == _onlineLoggedInUsername
                    ? $"(to {whispTo}) {whispText}"
                    : $"(whisper) {whispFrom}: {whispText}");
            }

            float now = Time.realtimeSinceStartup;
            if (now >= _nextFriendListRefresh)
            {
                _nextFriendListRefresh = now + FriendListRefreshInterval;
                _socialClient.RequestFriendList();
            }
        }

        void RequestFriendListNow()
        {
            _nextFriendListRefresh = 0f; // next UpdateSocial tick fires it immediately
        }

        void UpdateChannelLabel()
        {
            if (_socialChannelLabel != null)
                _socialChannelLabel.text = $"Channel: {_currentChannelName} ({_currentChannelMembers.Count})";
        }

        bool AmChannelOp => _currentChannelOp != null && _currentChannelOp == _onlineLoggedInUsername;

        void UpdateMotdDisplay()
        {
            if (_socialMotdLabel != null)
                _socialMotdLabel.text = string.IsNullOrEmpty(_currentChannelMotd)
                    ? "" : $"MOTD: {_currentChannelMotd}";
            Show(_socialMotdEditRow, AmChannelOp);
            if (AmChannelOp && _socialMotdInput != null && string.IsNullOrEmpty(_socialMotdInput.value))
                _socialMotdInput.value = _currentChannelMotd;
        }

        void SendSetChannelMotd()
        {
            if (_socialClient == null || _socialMotdInput == null)
                return;
            _socialClient.SetChannelMotd(_socialMotdInput.value?.Trim() ?? "");
        }

        void RebuildSocialMembers()
        {
            if (_socialMembers == null)
                return;
            _socialMembers.Clear();

            bool amOp = AmChannelOp;
            foreach (string username in _currentChannelMembers)
            {
                var row = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        justifyContent = Justify.SpaceBetween,
                        marginBottom = 2,
                    },
                };
                bool isOp = username == _currentChannelOp;
                var label = new Label(isOp ? $"★ {username}" : username) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("text");
                row.Add(label);

                if (amOp && username != _onlineLoggedInUsername)
                {
                    string target = username; // capture
                    var kick = new Button(() => _socialClient?.KickFromChannel(target)) { text = "Kick" };
                    kick.AddToClassList("menu__button");
                    kick.style.height = 20;
                    kick.style.marginBottom = 0;
                    kick.style.fontSize = 10;
                    row.Add(kick);
                }
                _socialMembers.Add(row);
            }
        }

        // --- Friends -------------------------------------------------------------

        void SendFriendRequestFromInput()
        {
            if (_socialClient == null || _friendAddInput == null)
                return;
            string username = _friendAddInput.value?.Trim();
            if (string.IsNullOrEmpty(username))
                return;
            _socialClient.SendFriendRequest(username);
            _friendAddInput.value = "";
            SetFriendAddStatus("");
        }

        void SetFriendAddStatus(string text)
        {
            if (_friendAddStatus != null)
                _friendAddStatus.text = text;
        }

        void RebuildFriendsList()
        {
            if (_friendsList == null)
                return;
            _friendsList.Clear();

            if (_incomingRequests.Count == 0 && _friendUsernames.Count == 0 && _outgoingRequests.Count == 0)
            {
                var empty = new Label("No friends yet — add one above.") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList("text");
                empty.AddToClassList("text--dim");
                empty.style.whiteSpace = WhiteSpace.Normal;
                _friendsList.Add(empty);
                return;
            }

            foreach (string username in _incomingRequests)
            {
                var row = NewFriendRow($"{username} wants to be friends");
                string target = username;
                var accept = new Button(() => _socialClient?.RespondToFriendRequest(target, true)) { text = "Accept" };
                accept.AddToClassList("menu__button");
                StyleSmallButton(accept);
                row.Add(accept);
                var decline = new Button(() => _socialClient?.RespondToFriendRequest(target, false)) { text = "Decline" };
                decline.AddToClassList("menu__button");
                StyleSmallButton(decline);
                row.Add(decline);
                _friendsList.Add(row);
            }

            foreach (string username in _friendUsernames)
            {
                bool online = _friendsOnline.Contains(username);
                var row = NewFriendRow(online ? $"● {username}" : $"○ {username}");
                string target = username;

                var whisper = new Button(() => StartWhisperTo(target)) { text = "Whisper" };
                whisper.AddToClassList("menu__button");
                StyleSmallButton(whisper);
                row.Add(whisper);

                var remove = new Button(() => _socialClient?.RemoveFriend(target)) { text = "Remove" };
                remove.AddToClassList("menu__button");
                StyleSmallButton(remove);
                row.Add(remove);

                _friendsList.Add(row);
            }

            foreach (string username in _outgoingRequests)
            {
                var row = NewFriendRow($"{username} (pending)");
                _friendsList.Add(row);
            }
        }

        static VisualElement NewFriendRow(string text)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
                    marginBottom = 2,
                },
            };
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("text");
            row.Add(label);
            return row;
        }

        static void StyleSmallButton(Button b)
        {
            b.style.height = 20;
            b.style.marginBottom = 0;
            b.style.marginLeft = 2;
            b.style.fontSize = 10;
        }

        /// <summary>Pre-fills the chat input with the "/w name " prefix
        /// SendSocialChat parses — matches the original Battle.net /w model
        /// instead of a separate whisper compose box.</summary>
        void StartWhisperTo(string username)
        {
            if (_socialChatInput != null)
                _socialChatInput.value = $"/w {username} ";
        }

        // --- Chat / whisper send ---------------------------------------------------

        void SendSocialChat()
        {
            if (_socialClient == null || _socialChatInput == null)
                return;
            string text = _socialChatInput.value?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            if (TryParseWhisperCommand(text, out string toUsername, out string message))
                _socialClient.SendWhisper(toUsername, message);
            else
                _socialClient.SendChannelChat(text);
            _socialChatInput.value = "";
        }

        /// <summary>"/w name message text" — the original Battle.net syntax.
        /// Also accepts "/whisper".</summary>
        static bool TryParseWhisperCommand(string text, out string toUsername, out string message)
        {
            toUsername = null;
            message = null;
            string rest;
            if (text.StartsWith("/w ", StringComparison.OrdinalIgnoreCase))
                rest = text.Substring(3);
            else if (text.StartsWith("/whisper ", StringComparison.OrdinalIgnoreCase))
                rest = text.Substring(9);
            else
                return false;

            int spaceIndex = rest.IndexOf(' ');
            if (spaceIndex < 0)
                return false;
            toUsername = rest.Substring(0, spaceIndex).Trim();
            message = rest.Substring(spaceIndex + 1).Trim();
            return !string.IsNullOrEmpty(toUsername) && !string.IsNullOrEmpty(message);
        }

        void AppendSocialLine(string line)
        {
            if (_socialChatLog == null)
                return;
            var label = new Label(line) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList("text");
            label.style.whiteSpace = WhiteSpace.Normal;
            _socialChatLog.Add(label);
            while (_socialChatLog.childCount > MaxSocialChatLines)
                _socialChatLog.RemoveAt(0);
            _socialChatLog.scrollOffset = new Vector2(0, float.MaxValue);
        }
    }
}
