using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Craftwar.Net;
using Craftwar.NetServer.Protocol;
using Craftwar.Sim;

namespace Craftwar.NetServer.Transport
{
    /// <summary>
    /// One client's control-plane connection: TLS handshake, then a
    /// length-prefixed read loop dispatching to <see cref="AccountService"/>
    /// and <see cref="RoomManager"/>. Pure glue — the actual logic lives in
    /// those two (both unit-testable without a socket); this class exists to
    /// keep that logic out of I/O code, not the other way round.
    ///
    /// Also the target of OTHER connections' room-relay sends, which is why
    /// writes go through a lock: this connection's own read-loop response
    /// and an unrelated room member's relay push can race for the same
    /// SslStream otherwise.
    /// </summary>
    public sealed class ClientConnection
    {
        readonly TcpClient _tcp;
        readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _cert;
        readonly AccountService _accounts;
        readonly RatingService _ratings;
        readonly RoomManager _rooms;
        readonly ConnectionRegistry _registry;
        readonly PresenceDirectory _presence;
        readonly ChannelManager _channels;
        readonly FriendsService _friends;
        readonly Action<string> _log;
        readonly SemaphoreSlim _writeLock = new(1, 1);

        SslStream _ssl;

        public string ConnectionId { get; } = Guid.NewGuid().ToString("N");

        /// <summary>Set once Login/ResumeSession succeeds ON THIS connection
        /// (see BindIdentity) — null for an anonymous connection (e.g. a
        /// RelayPeerSocket room connection, which never authenticates; see
        /// M12 plan mechanism note 2). Social message handling below checks
        /// this before doing anything account-scoped.</summary>
        public long? AccountId { get; private set; }
        public string Username { get; private set; }

        public ClientConnection(TcpClient tcp, System.Security.Cryptography.X509Certificates.X509Certificate2 cert,
            AccountService accounts, RatingService ratings, RoomManager rooms, ConnectionRegistry registry,
            PresenceDirectory presence, ChannelManager channels, FriendsService friends, Action<string> log)
        {
            _tcp = tcp;
            _cert = cert;
            _accounts = accounts;
            _ratings = ratings;
            _rooms = rooms;
            _registry = registry;
            _presence = presence;
            _channels = channels;
            _friends = friends;
            _log = log;
        }

        /// <summary>Binds this connection to an account after a successful
        /// Login/ResumeSession — see M12 plan mechanism note 1 (no
        /// connection was ever bound to an account before this). Safe to
        /// call again (e.g. a stray re-auth): both AccountId/Username and
        /// the PresenceDirectory entry simply overwrite.</summary>
        void BindIdentity(long accountId, string username)
        {
            AccountId = accountId;
            Username = username;
            _presence.Add(accountId, ConnectionId);
        }

        public async Task RunAsync()
        {
            string remote = _tcp.Client.RemoteEndPoint?.ToString() ?? "?";
            _registry.Add(this);
            try
            {
                using var network = _tcp.GetStream();
                using var ssl = new SslStream(network, leaveInnerStreamOpen: false);
                _ssl = ssl;
                await ssl.AuthenticateAsServerAsync(_cert, clientCertificateRequired: false,
                    checkCertificateRevocation: false).ConfigureAwait(false);

                while (true)
                {
                    byte[] frame = await StreamFraming.ReadFrameAsync(ssl).ConfigureAwait(false);
                    if (frame == null)
                        break; // clean disconnect

                    var (response, after) = await HandleAsync(frame).ConfigureAwait(false);
                    if (response != null)
                        await WriteFrameAsync(response).ConfigureAwait(false);
                    // Order matters: anything a handler pushes to OTHER
                    // connections (or back to this one, e.g. "here is who
                    // else is already in the room you just joined") must
                    // land on the wire AFTER this request's own direct
                    // response — a client always expects the reply to the
                    // message it just sent as the very next frame.
                    if (after != null)
                        await after().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _log($"[conn {remote}] {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _registry.Remove(this);
                await LeaveRoomAsync().ConfigureAwait(false);
                await LeaveChannelAsync().ConfigureAwait(false);
                if (AccountId.HasValue)
                    _presence.Remove(AccountId.Value, ConnectionId);
                _tcp.Dispose();
                _log($"[conn {remote}] closed");
            }
        }

        /// <summary>Called by ANOTHER connection's room-relay handling to
        /// push a frame into this one. Serialized against this connection's
        /// own response writes via <see cref="_writeLock"/>.</summary>
        public Task PushFrameAsync(byte[] payload) => WriteFrameAsync(payload);

        async Task WriteFrameAsync(byte[] payload)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_ssl != null)
                    await StreamFraming.WriteFrameAsync(_ssl, payload, payload.Length).ConfigureAwait(false);
            }
            catch (Exception e) when (e is ObjectDisposedException or IOException)
            {
                // This connection's own read loop already tore its stream
                // down (a real disconnect) but has not yet reached the
                // finally block that removes it from ConnectionRegistry —
                // `using var ssl` disposes at the end of RunAsync's try
                // block, strictly before that finally runs, so there is a
                // real window where a concurrent PushFrameAsync from
                // another connection's relay/broadcast can still find this
                // one registered. Benign: the disconnect itself is still
                // reported the normal way, via that same finally block's
                // RoomPeerEvent broadcast — a dropped frame here changes
                // nothing a moment later would not have dropped anyway.
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>The direct reply to THIS message (written first, if
        /// non-null), and optionally follow-up work to run only after that
        /// reply is on the wire (room announcements, relay fan-out) — see the
        /// ordering note in <see cref="RunAsync"/>.</summary>
        async Task<(byte[] response, Func<Task> after)> HandleAsync(byte[] frame)
        {
            var r = new ByteReader(frame);
            var kind = (ControlMessageKind)r.ReadByte();
            var w = new ByteWriter(256);

            switch (kind)
            {
                case ControlMessageKind.Hello:
                {
                    ushort clientVersion = ControlProtocol.ReadHello(ref r);
                    bool ok = clientVersion == ControlProtocol.CurrentVersion;
                    ControlProtocol.WriteHelloAck(ref w, ok,
                        ok ? null : $"server speaks control v{ControlProtocol.CurrentVersion}, client sent v{clientVersion}");
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.Register:
                {
                    ControlProtocol.ReadRegister(ref r, out string username, out string password);
                    var result = _accounts.Register(username, password, out _);
                    ControlProtocol.WriteRegisterResult(ref w, result);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.Login:
                {
                    ControlProtocol.ReadLogin(ref r, out string username, out string password);
                    var result = _accounts.Login(username, password, out string token, out long accountId);
                    if (result == AccountResult.Ok)
                        BindIdentity(accountId, username);
                    ControlProtocol.WriteLoginResult(ref w, result, token);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.ResumeSession:
                {
                    string token = ControlProtocol.ReadResumeSession(ref r);
                    var result = _accounts.ResumeSession(token, out long accountId, out string username);
                    if (result == AccountResult.Ok)
                        BindIdentity(accountId, username);
                    ControlProtocol.WriteResumeSessionResult(ref w, result, username);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.CreateRoom:
                {
                    ControlProtocol.ReadCreateRoom(ref r, out string mapName, out string hostName,
                        out string roomName, out int maxPlayers);
                    var room = _rooms.CreateRoom(ConnectionId, mapName, hostName, roomName, maxPlayers);
                    ControlProtocol.WriteCreateRoomResult(ref w, room.Id);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.JoinRoom:
                {
                    string roomId = ControlProtocol.ReadJoinRoom(ref r);
                    var failure = _rooms.TryJoinRoom(roomId, ConnectionId, out var room, out int yourPeerId);
                    ControlProtocol.WriteJoinRoomResult(ref w, failure, yourPeerId);
                    Func<Task> after = failure == RoomJoinFailure.None
                        ? () => AnnounceJoinAsync(room, yourPeerId)
                        : null;
                    return (w.ToArray(), after);
                }

                case ControlMessageKind.ListRooms:
                {
                    ControlProtocol.WriteListRoomsResult(ref w, ToSummaries(_rooms.ListRooms()));
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.GetRating:
                {
                    string username = ControlProtocol.ReadGetRating(ref r);
                    bool found = _ratings.TryGetRating(username, out var rating, out int games);
                    ControlProtocol.WriteGetRatingResult(ref w, username, found,
                        found ? (int)Math.Round(rating.Rating) : 0, games);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.RoomRelay:
                {
                    ControlProtocol.ReadRoomRelay(ref r, out int targetPeerId, out byte[] payload);
                    // Fire-and-forget — the relay is not itself acknowledged,
                    // so there is no "reply" to sequence this after; running
                    // it as `after` still keeps it off the read loop's own
                    // await chain for the (rare, null) response case.
                    return (null, () => RelayAsync(targetPeerId, payload));
                }

                case ControlMessageKind.ChatMessage:
                {
                    ControlProtocol.ReadChatMessage(ref r, out string senderName, out string text);
                    return (null, () => BroadcastChatAsync(senderName, text));
                }

                case ControlMessageKind.ReportMatchResult:
                {
                    ControlProtocol.ReadReportMatchResult(ref r, out string map, out string mode,
                        out string[] usernames, out bool[] won);
                    bool ok = true;
                    try
                    {
                        var players = new System.Collections.Generic.List<RatingService.PlayerResult>(usernames.Length);
                        for (int i = 0; i < usernames.Length; i++)
                            players.Add(new RatingService.PlayerResult(usernames[i], won[i]));
                        _ratings.ReportResult(map, mode, players);
                    }
                    catch (Exception e)
                    {
                        ok = false;
                        _log($"[conn {ConnectionId}] match result report failed: {e.Message}");
                    }
                    ControlProtocol.WriteReportMatchResultAck(ref w, ok);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.ChannelJoin:
                {
                    string channelName = ControlProtocol.ReadChannelJoin(ref r);
                    if (AccountId == null)
                        return (null, null); // anonymous connection — see AccountId's doc comment

                    if (!ChannelManager.IsValidName(channelName))
                    {
                        ControlProtocol.WriteChannelJoinResult(ref w, false, "invalid channel name", "",
                            System.Array.Empty<string>(), "", "");
                        return (w.ToArray(), null);
                    }

                    var channel = _channels.Join(AccountId.Value, Username, channelName, out var previousChannel);
                    ControlProtocol.WriteChannelJoinResult(ref w, true, null, channel.Name,
                        channel.MemberUsernamesSnapshot(), channel.OpUsername, channel.Motd);
                    return (w.ToArray(), () => AnnounceChannelJoinAsync(channel, previousChannel));
                }

                case ControlMessageKind.ChannelSetMotd:
                {
                    string motd = ControlProtocol.ReadChannelSetMotd(ref r);
                    if (AccountId == null)
                        return (null, null);

                    string failure = _channels.TrySetMotd(AccountId.Value, motd, out var channel);
                    ControlProtocol.WriteChannelSetMotdResult(ref w, failure == null, failure);
                    Func<Task> after = failure == null
                        ? () => AnnounceChannelMotdChangedAsync(channel)
                        : null;
                    return (w.ToArray(), after);
                }

                case ControlMessageKind.ChannelChat:
                {
                    string text = ControlProtocol.ReadChannelChat(ref r);
                    if (AccountId == null)
                        return (null, null);
                    return (null, () => BroadcastChannelChatAsync(text));
                }

                case ControlMessageKind.ChannelKick:
                {
                    string targetUsername = ControlProtocol.ReadChannelKick(ref r);
                    if (AccountId == null)
                        return (null, null);

                    string failure = _channels.TryKick(AccountId.Value, targetUsername, out var channel,
                        out long targetAccountId);
                    ControlProtocol.WriteChannelKickResult(ref w, failure == null, failure);
                    Func<Task> after = failure == null
                        ? () => AnnounceChannelKickAsync(channel, targetAccountId, targetUsername)
                        : null;
                    return (w.ToArray(), after);
                }

                case ControlMessageKind.FriendRequest:
                {
                    string toUsername = ControlProtocol.ReadFriendRequest(ref r);
                    if (AccountId == null)
                        return (null, null);

                    string failure = _friends.SendRequest(AccountId.Value, toUsername, out bool becameFriends);
                    ControlProtocol.WriteFriendRequestResult(ref w, failure == null, failure, becameFriends);
                    Func<Task> after = failure == null
                        ? () => becameFriends
                            ? SendFriendRequestAnsweredAsync(toUsername, Username, accepted: true)
                            : SendFriendRequestReceivedAsync(toUsername, Username)
                        : null;
                    return (w.ToArray(), after);
                }

                case ControlMessageKind.FriendRespond:
                {
                    ControlProtocol.ReadFriendRespond(ref r, out string requesterUsername, out bool accept);
                    if (AccountId == null)
                        return (null, null);

                    string failure = _friends.Respond(AccountId.Value, requesterUsername, accept);
                    ControlProtocol.WriteFriendRespondResult(ref w, failure == null, failure);
                    Func<Task> after = failure == null
                        ? () => SendFriendRequestAnsweredAsync(requesterUsername, Username, accept)
                        : null;
                    return (w.ToArray(), after);
                }

                case ControlMessageKind.FriendRemove:
                {
                    string friendUsername = ControlProtocol.ReadFriendRemove(ref r);
                    if (AccountId == null)
                        return (null, null);

                    string failure = _friends.RemoveFriend(AccountId.Value, friendUsername);
                    ControlProtocol.WriteFriendRemoveResult(ref w, failure == null, failure);
                    Func<Task> after = failure == null
                        ? () => SendFriendRemovedAsync(friendUsername, Username)
                        : null;
                    return (w.ToArray(), after);
                }

                case ControlMessageKind.FriendListRequest:
                {
                    if (AccountId == null)
                        return (null, null);

                    var friends = _friends.ListFriends(AccountId.Value);
                    var friendUsernames = new string[friends.Count];
                    var friendOnline = new bool[friends.Count];
                    for (int i = 0; i < friends.Count; i++)
                    {
                        friendUsernames[i] = friends[i].username;
                        friendOnline[i] = _presence.IsOnline(friends[i].accountId);
                    }
                    var incoming = _friends.ListIncomingRequests(AccountId.Value);
                    var outgoing = _friends.ListOutgoingRequests(AccountId.Value);
                    ControlProtocol.WriteFriendListResult(ref w, friendUsernames, friendOnline,
                        incoming.ToArray(), outgoing.ToArray());
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.Whisper:
                {
                    ControlProtocol.ReadWhisper(ref r, out string toUsername, out string text);
                    if (AccountId == null)
                        return (null, null);

                    if (!_friends.TryResolveAccount(toUsername, out long toId))
                    {
                        ControlProtocol.WriteWhisperResult(ref w, false, "no such user");
                        return (w.ToArray(), null);
                    }
                    ControlProtocol.WriteWhisperResult(ref w, true, null);
                    return (w.ToArray(), () => DeliverWhisperAsync(Username, toUsername, toId, text));
                }

                default:
                    return (null, null);
            }
        }

        /// <summary>Reciprocal announcement, matching LoopbackNetwork.CreatePeer
        /// exactly: existing members learn about the newcomer, and the
        /// newcomer learns about everyone already there.</summary>
        async Task AnnounceJoinAsync(Room room, int newPeerId)
        {
            var toNewcomer = new ByteWriter(32);
            foreach (var (peerId, connectionId) in room.MembersSnapshot())
            {
                if (peerId == newPeerId)
                    continue;

                toNewcomer.Position = 0;
                ControlProtocol.WriteRoomPeerEvent(ref toNewcomer, peerId, true);
                await WriteFrameAsync(toNewcomer.ToArray()).ConfigureAwait(false);

                if (_registry.TryGet(connectionId, out var existing))
                {
                    var toExisting = new ByteWriter(32);
                    ControlProtocol.WriteRoomPeerEvent(ref toExisting, newPeerId, true);
                    await existing.PushFrameAsync(toExisting.ToArray()).ConfigureAwait(false);
                }
            }
        }

        async Task RelayAsync(int targetPeerId, byte[] payload)
        {
            if (!_rooms.TryGetMembership(ConnectionId, out var room, out int myPeerId))
                return; // not in a room — a stray relay message is simply dropped

            var w = new ByteWriter(payload.Length + 16);
            foreach (var (peerId, connectionId) in room.MembersSnapshot())
            {
                if (peerId == myPeerId)
                    continue;
                if (targetPeerId >= 0 && peerId != targetPeerId)
                    continue;
                if (!_registry.TryGet(connectionId, out var target))
                    continue;

                w.Position = 0;
                ControlProtocol.WriteRoomRelayFrom(ref w, myPeerId, payload, 0, payload.Length);
                await target.PushFrameAsync(w.ToArray()).ConfigureAwait(false);
            }
        }

        /// <summary>Unlike RelayAsync, this echoes back to the sender too —
        /// everyone's chat log (including the sender's own) is built from
        /// the same server broadcast rather than the sender adding its own
        /// line locally, so there is exactly one source of ordering.</summary>
        async Task BroadcastChatAsync(string senderName, string text)
        {
            if (!_rooms.TryGetMembership(ConnectionId, out var room, out _))
                return; // not in a room — a stray chat message is simply dropped

            var w = new ByteWriter(text.Length + senderName.Length + 16);
            ControlProtocol.WriteChatBroadcast(ref w, senderName, text);
            byte[] payload = w.ToArray();
            foreach (var (_, connectionId) in room.MembersSnapshot())
                if (_registry.TryGet(connectionId, out var member))
                    await member.PushFrameAsync(payload).ConfigureAwait(false);
        }

        async Task LeaveRoomAsync()
        {
            if (!_rooms.RemoveMember(ConnectionId, out var room, out int myPeerId))
                return;

            var w = new ByteWriter(16);
            ControlProtocol.WriteRoomPeerEvent(ref w, myPeerId, false);
            byte[] payload = w.ToArray();
            foreach (var (peerId, connectionId) in room.MembersSnapshot())
            {
                if (peerId == myPeerId)
                    continue;
                if (_registry.TryGet(connectionId, out var member))
                    await member.PushFrameAsync(payload).ConfigureAwait(false);
            }
        }

        /// <summary>Announces a channel join both ways: the channel just
        /// left (if any) learns the mover departed, and the new channel's
        /// OTHER members (the mover already knows via ChannelJoinResult)
        /// learn about the newcomer. Mirrors AnnounceJoinAsync's reciprocal
        /// shape for rooms.</summary>
        async Task AnnounceChannelJoinAsync(ChatChannel channel, ChatChannel previousChannel)
        {
            if (previousChannel != null)
                await BroadcastChannelMemberEventAsync(previousChannel, Username, joined: false)
                    .ConfigureAwait(false);

            await BroadcastChannelMemberEventAsync(channel, Username, joined: true, excludeAccountId: AccountId)
                .ConfigureAwait(false);
        }

        /// <summary>Unlike RelayAsync's room chat, this echoes back to the
        /// sender too — same reasoning as BroadcastChatAsync: one ordering
        /// source for everyone's log, not the sender adding its own line
        /// locally.</summary>
        async Task BroadcastChannelChatAsync(string text)
        {
            if (!_channels.TryGetChannelOf(AccountId.Value, out var channel))
                return; // not in a channel — a stray chat message is simply dropped

            var w = new ByteWriter(text.Length + Username.Length + 16);
            ControlProtocol.WriteChannelChatBroadcast(ref w, channel.Name, Username, text);
            byte[] payload = w.ToArray();
            foreach (var (accountId, _) in channel.MembersSnapshot())
                if (_presence.TryGetConnectionId(accountId, out string connId) && _registry.TryGet(connId, out var member))
                    await member.PushFrameAsync(payload).ConfigureAwait(false);
        }

        /// <summary>Tells the kicked account directly (ChannelKicked, so its
        /// client can show "you were kicked" rather than an ordinary
        /// departure) before the remaining members get the ordinary
        /// ChannelMemberEvent departure notice.</summary>
        async Task AnnounceChannelKickAsync(ChatChannel channel, long targetAccountId, string targetUsername)
        {
            if (_presence.TryGetConnectionId(targetAccountId, out string targetConnId)
                && _registry.TryGet(targetConnId, out var target))
            {
                var kw = new ByteWriter(64);
                ControlProtocol.WriteChannelKicked(ref kw, channel.Name, Username);
                await target.PushFrameAsync(kw.ToArray()).ConfigureAwait(false);
            }

            await BroadcastChannelMemberEventAsync(channel, targetUsername, joined: false).ConfigureAwait(false);
        }

        async Task LeaveChannelAsync()
        {
            if (AccountId == null)
                return;
            var channel = _channels.Leave(AccountId.Value);
            if (channel != null)
                await BroadcastChannelMemberEventAsync(channel, Username, joined: false).ConfigureAwait(false);
        }

        /// <summary>Pushes a ChannelMemberEvent to every CURRENT member of
        /// <paramref name="channel"/> (post-mutation state — a departed
        /// subject is already absent from it), optionally skipping one
        /// account (the mover, for a join announcement they already learned
        /// of via their own direct reply).</summary>
        async Task BroadcastChannelMemberEventAsync(ChatChannel channel, string subjectUsername, bool joined,
            long? excludeAccountId = null)
        {
            var w = new ByteWriter(64);
            ControlProtocol.WriteChannelMemberEvent(ref w, channel.Name, subjectUsername, joined, channel.OpUsername);
            byte[] payload = w.ToArray();
            foreach (var (accountId, _) in channel.MembersSnapshot())
            {
                if (excludeAccountId.HasValue && accountId == excludeAccountId.Value)
                    continue;
                if (_presence.TryGetConnectionId(accountId, out string connId) && _registry.TryGet(connId, out var member))
                    await member.PushFrameAsync(payload).ConfigureAwait(false);
            }
        }

        /// <summary>Pushes the new MOTD to every CURRENT member of the
        /// channel, including the op who just set it — ChannelJoinResult
        /// already carries the MOTD for a fresh join, so this is only for
        /// people already in the channel when it changes.</summary>
        async Task AnnounceChannelMotdChangedAsync(ChatChannel channel)
        {
            var w = new ByteWriter(64);
            ControlProtocol.WriteChannelMotdChanged(ref w, channel.Name, channel.Motd);
            byte[] payload = w.ToArray();
            foreach (var (accountId, _) in channel.MembersSnapshot())
                if (_presence.TryGetConnectionId(accountId, out string connId) && _registry.TryGet(connId, out var member))
                    await member.PushFrameAsync(payload).ConfigureAwait(false);
        }

        /// <summary>Best-effort: silently does nothing if the target is
        /// offline — they'll see the pending request next time they poll
        /// FriendListRequest, same "poll-on-demand" presence model the M12
        /// plan already settled on.</summary>
        async Task SendFriendRequestReceivedAsync(string toUsername, string fromUsername)
        {
            if (!_friends.TryResolveAccount(toUsername, out long toId))
                return;
            if (!_presence.TryGetConnectionId(toId, out string connId) || !_registry.TryGet(connId, out var target))
                return;
            var w = new ByteWriter(64);
            ControlProtocol.WriteFriendRequestReceived(ref w, fromUsername);
            await target.PushFrameAsync(w.ToArray()).ConfigureAwait(false);
        }

        async Task SendFriendRequestAnsweredAsync(string toUsername, string byUsername, bool accepted)
        {
            if (!_friends.TryResolveAccount(toUsername, out long toId))
                return;
            if (!_presence.TryGetConnectionId(toId, out string connId) || !_registry.TryGet(connId, out var target))
                return;
            var w = new ByteWriter(64);
            ControlProtocol.WriteFriendRequestAnswered(ref w, byUsername, accepted);
            await target.PushFrameAsync(w.ToArray()).ConfigureAwait(false);
        }

        async Task SendFriendRemovedAsync(string toUsername, string byUsername)
        {
            if (!_friends.TryResolveAccount(toUsername, out long toId))
                return;
            if (!_presence.TryGetConnectionId(toId, out string connId) || !_registry.TryGet(connId, out var target))
                return;
            var w = new ByteWriter(64);
            ControlProtocol.WriteFriendRemoved(ref w, byUsername);
            await target.PushFrameAsync(w.ToArray()).ConfigureAwait(false);
        }

        /// <summary>Delivers to the recipient (if online) AND echoes back to
        /// the sender — same "one ordering source builds both logs"
        /// reasoning as BroadcastChatAsync/BroadcastChannelChatAsync. The
        /// sender already got a WhisperResult ack; this is what actually
        /// puts the line in both parties' logs.</summary>
        async Task DeliverWhisperAsync(string fromUsername, string toUsername, long toAccountId, string text)
        {
            var w = new ByteWriter(text.Length + fromUsername.Length + toUsername.Length + 16);
            ControlProtocol.WriteWhisperReceived(ref w, fromUsername, toUsername, text);
            byte[] payload = w.ToArray();

            if (_presence.TryGetConnectionId(toAccountId, out string toConnId) && _registry.TryGet(toConnId, out var target))
                await target.PushFrameAsync(payload).ConfigureAwait(false);

            await WriteFrameAsync(payload).ConfigureAwait(false); // echo to the sender (this connection)
        }

        RoomSummary[] ToSummaries(System.Collections.Generic.IReadOnlyCollection<Room> rooms)
        {
            var summaries = new RoomSummary[rooms.Count];
            int i = 0;
            foreach (var room in rooms)
            {
                bool found = _ratings.TryGetRating(room.HostName, out var rating, out int games);
                summaries[i++] = new RoomSummary
                {
                    RoomId = room.Id,
                    MapName = room.MapName,
                    HostName = room.HostName,
                    RoomName = room.RoomName,
                    PlayerCount = room.PlayerCount,
                    MaxPlayers = room.MaxPlayers,
                    HostRatingKnown = found,
                    HostRating = found ? (int)Math.Round(rating.Rating) : 0,
                    HostGamesPlayed = games,
                };
            }
            return summaries;
        }
    }
}
