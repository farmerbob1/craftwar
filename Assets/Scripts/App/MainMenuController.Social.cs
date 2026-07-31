using System;
using System.Collections.Generic;
using Craftwar.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// The social layer (M12 phase 1: chat channels only so far). Owns a
    /// single <see cref="SocialClient"/> connection, opened right after a
    /// successful login in <see cref="MainMenuController.Online"/>'s
    /// Authenticate() and closed on logout/app teardown — deliberately
    /// independent of whatever <c>panel-lobby</c>/<c>_onlineSocket</c> is
    /// doing (hosting, browsing, in a room, or nothing at all): see the M12
    /// plan's mechanism note 2. Same partial-class pattern as
    /// <c>.Lan.cs</c>/<c>.Online.cs</c>.
    /// </summary>
    public sealed partial class MainMenuController
    {
        VisualElement _onlineSocial;
        ScrollView _socialMembers, _socialChatLog;
        Label _socialChannelLabel;
        TextField _socialChatInput;
        Button _socialChatSendBtn;

        SocialClient _socialClient;
        string _currentChannelName, _currentChannelOp;
        readonly List<string> _currentChannelMembers = new List<string>();

        const int MaxSocialChatLines = 50;

        void InitSocial(VisualElement root)
        {
            _onlineSocial = root.Q("online-social");
            if (_onlineSocial == null)
                return; // older scene UXML; the social layer simply stays hidden

            _socialChannelLabel = root.Q<Label>("social-channel-label");
            _socialMembers = root.Q<ScrollView>("social-members");
            _socialChatLog = root.Q<ScrollView>("social-chat-log");
            _socialChatInput = root.Q<TextField>("social-chat-input");
            _socialChatSendBtn = root.Q<Button>("social-chat-send");
            if (_socialChatSendBtn != null)
                _socialChatSendBtn.clicked += SendSocialChat;

            Show(_onlineSocial, false);
        }

        void ShowSocialSection(bool visible)
        {
            if (_onlineSocial != null)
                Show(_onlineSocial, visible);
        }

        void OpenSocialConnection(string host, int port)
        {
            CloseSocialConnection();
            if (string.IsNullOrEmpty(_onlineSessionToken))
                return;
            try
            {
                _socialClient = SocialClient.Connect(host, port, _onlineSessionToken);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Craftwar] Could not connect to chat: {e.Message}");
                _socialClient = null;
            }
        }

        void CloseSocialConnection()
        {
            _socialClient?.Dispose();
            _socialClient = null;
            _currentChannelName = null;
            _currentChannelOp = null;
            _currentChannelMembers.Clear();
            _socialMembers?.Clear();
            _socialChatLog?.Clear();
            if (_socialChannelLabel != null)
                _socialChannelLabel.text = "";
        }

        void UpdateSocial()
        {
            if (_socialClient == null)
                return;

            while (_socialClient.TryReceiveChannelJoinResult(out bool ok, out string reason, out string channelName,
                       out string[] members, out string opUsername))
            {
                if (!ok)
                {
                    AppendSocialLine($"(could not join {channelName}: {reason})");
                    continue;
                }
                _currentChannelName = channelName;
                _currentChannelOp = opUsername;
                _currentChannelMembers.Clear();
                _currentChannelMembers.AddRange(members);
                RebuildSocialMembers();
                UpdateChannelLabel();
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
            }

            while (_socialClient.TryReceiveChannelChat(out string chatChannel, out string senderUsername,
                       out string text))
                AppendSocialLine($"{senderUsername}: {text}");

            while (_socialClient.TryReceiveChannelKickResult(out bool kickOk, out string kickReason))
                if (!kickOk)
                    AppendSocialLine($"(kick failed: {kickReason})");

            while (_socialClient.TryReceiveChannelKicked(out string kickedChannel, out string byUsername))
                AppendSocialLine($"*** You were kicked from {kickedChannel} by {byUsername}");
        }

        void UpdateChannelLabel()
        {
            if (_socialChannelLabel != null)
                _socialChannelLabel.text = $"Channel: {_currentChannelName} ({_currentChannelMembers.Count})";
        }

        void RebuildSocialMembers()
        {
            if (_socialMembers == null)
                return;
            _socialMembers.Clear();

            bool amOp = _currentChannelOp != null && _currentChannelOp == _onlineLoggedInUsername;
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

        void SendSocialChat()
        {
            if (_socialClient == null || _socialChatInput == null)
                return;
            string text = _socialChatInput.value?.Trim();
            if (string.IsNullOrEmpty(text))
                return;
            _socialClient.SendChannelChat(text);
            _socialChatInput.value = "";
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
