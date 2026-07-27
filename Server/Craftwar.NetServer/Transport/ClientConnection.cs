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
        readonly Action<string> _log;
        readonly SemaphoreSlim _writeLock = new(1, 1);

        SslStream _ssl;

        public string ConnectionId { get; } = Guid.NewGuid().ToString("N");

        public ClientConnection(TcpClient tcp, System.Security.Cryptography.X509Certificates.X509Certificate2 cert,
            AccountService accounts, RatingService ratings, RoomManager rooms, ConnectionRegistry registry,
            Action<string> log)
        {
            _tcp = tcp;
            _cert = cert;
            _accounts = accounts;
            _ratings = ratings;
            _rooms = rooms;
            _registry = registry;
            _log = log;
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
                    var result = _accounts.Login(username, password, out string token, out _);
                    ControlProtocol.WriteLoginResult(ref w, result, token);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.ResumeSession:
                {
                    string token = ControlProtocol.ReadResumeSession(ref r);
                    var result = _accounts.ResumeSession(token, out _, out string username);
                    ControlProtocol.WriteResumeSessionResult(ref w, result, username);
                    return (w.ToArray(), null);
                }

                case ControlMessageKind.CreateRoom:
                {
                    ControlProtocol.ReadCreateRoom(ref r, out string mapName, out string hostName, out int maxPlayers);
                    var room = _rooms.CreateRoom(ConnectionId, mapName, hostName, maxPlayers);
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

        static RoomSummary[] ToSummaries(System.Collections.Generic.IReadOnlyCollection<Room> rooms)
        {
            var summaries = new RoomSummary[rooms.Count];
            int i = 0;
            foreach (var room in rooms)
                summaries[i++] = new RoomSummary
                {
                    RoomId = room.Id,
                    MapName = room.MapName,
                    HostName = room.HostName,
                    PlayerCount = room.PlayerCount,
                    MaxPlayers = room.MaxPlayers,
                };
            return summaries;
        }
    }
}
