using System;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// The online transport: an <see cref="IPacketPeer"/> whose "peers" are
    /// room-mates on a relay server rather than direct LAN connections.
    /// Everything above this line (LobbyHost/LobbyClient, HostTurnExchange/
    /// ClientTurnExchange, TurnRelay) is unmodified — the server assigns the
    /// room creator peer id 0 by construction (see the server's RoomManager),
    /// so this needs no id-remap table the way a server that handed out
    /// arbitrary ids would have.
    ///
    /// Pure BCL (TcpClient/SslStream) — zero Unity dependency, unlike
    /// UtpPeerSocket, so this is exercised by the standalone Sim/Net test
    /// harness too, not just the editor.
    ///
    /// Async I/O under a synchronous IPacketPeer surface: a reader task
    /// unwraps RoomRelay/RoomPeerEvent frames into thread-safe queues Poll()
    /// just drains; a writer task owns the one outbound stream so Send()
    /// never blocks the caller (typically Unity's main thread) on the
    /// network.
    /// </summary>
    public sealed class RelayPeerSocket : IPacketPeer
    {
        readonly TcpClient _tcp;
        readonly SslStream _ssl;
        readonly CancellationTokenSource _cts = new();
        readonly ConcurrentQueue<(int from, byte[] payload, int length)> _inbox = new();
        readonly ConcurrentQueue<(int peerId, bool connected)> _connectionEvents = new();
        readonly ConcurrentQueue<(string senderName, string text)> _chatInbox = new();
        readonly BlockingCollection<byte[]> _outbox = new();
        readonly Task _readerTask;
        readonly Task _writerTask;

        public int LocalPeerId { get; }
        public bool IsHost => LocalPeerId == 0;
        public string RoomId { get; }

        /// <summary>True once the connection has dropped — Poll() surfaces
        /// this as a disconnect event for peer -1 is not meaningful, so
        /// callers check this directly (mirrors ClientTurnExchange.Status
        /// picking up IPacketPeer-level trouble via TryDequeueConnectionEvent
        /// for a KNOWN peer id instead — this flag is for "the relay itself
        /// is gone", which no room-peer-id represents).</summary>
        public bool Faulted { get; private set; }

        RelayPeerSocket(TcpClient tcp, SslStream ssl, int localPeerId, string roomId)
        {
            _tcp = tcp;
            _ssl = ssl;
            LocalPeerId = localPeerId;
            RoomId = roomId;
            _readerTask = Task.Run(ReadLoopAsync);
            _writerTask = Task.Run(WriteLoopAsync);
        }

        /// <summary>Connect, authenticate, and create a room — the caller
        /// becomes room-peer 0, the elected host.</summary>
        public static RelayPeerSocket Host(string serverHost, int serverPort, string mapName,
            string hostName, int maxPlayers) =>
            Connect(serverHost, serverPort, joinRoomId: null, mapName, hostName, maxPlayers);

        /// <summary>Connect, authenticate, and join an existing room.</summary>
        public static RelayPeerSocket Join(string serverHost, int serverPort, string roomId,
            string playerName) =>
            Connect(serverHost, serverPort, joinRoomId: roomId, mapName: null, playerName, maxPlayers: 0);

        static RelayPeerSocket Connect(string serverHost, int serverPort, string joinRoomId,
            string mapName, string name, int maxPlayers)
        {
            var tcp = new TcpClient();
            tcp.Connect(serverHost, serverPort);
            tcp.NoDelay = true;
            var ssl = new SslStream(tcp.GetStream(), false, ValidateServerCertificate);
            ssl.AuthenticateAsClient(serverHost);

            SendHandshakeFrame(ssl, (ref ByteWriter w) => ControlProtocol.WriteHello(ref w, ControlProtocol.CurrentVersion));
            var ack = ReadHandshakeFrame(ssl);
            ControlProtocol.ReadHelloAck(ref ack, out bool accepted, out string reason);
            if (!accepted)
                throw new InvalidOperationException($"relay server refused the connection: {reason}");

            string roomId;
            int localPeerId;
            if (joinRoomId == null)
            {
                SendHandshakeFrame(ssl, (ref ByteWriter w) => ControlProtocol.WriteCreateRoom(ref w, mapName, name, maxPlayers));
                var result = ReadHandshakeFrame(ssl);
                roomId = ControlProtocol.ReadCreateRoomResult(ref result);
                localPeerId = 0; // the creator is always room-peer 0 — see the server's RoomManager
            }
            else
            {
                SendHandshakeFrame(ssl, (ref ByteWriter w) => ControlProtocol.WriteJoinRoom(ref w, joinRoomId));
                var result = ReadHandshakeFrame(ssl);
                ControlProtocol.ReadJoinRoomResult(ref result, out var failure, out localPeerId);
                if (failure != RoomJoinFailure.None)
                    throw new InvalidOperationException($"could not join room {joinRoomId}: {failure}");
                roomId = joinRoomId;
            }

            return new RelayPeerSocket(tcp, ssl, localPeerId, roomId);
        }

        static void SendHandshakeFrame(SslStream ssl, WriteAction write)
        {
            var w = new ByteWriter(128);
            write(ref w);
            StreamFraming.WriteFrameAsync(ssl, w.Buffer, w.Position).GetAwaiter().GetResult();
        }

        static ByteReader ReadHandshakeFrame(SslStream ssl)
        {
            byte[] frame = StreamFraming.ReadFrameAsync(ssl).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("relay server closed the connection during handshake");
            var r = new ByteReader(frame);
            r.ReadByte(); // message kind — the specific ReadXxx call after this knows which
            return r;
        }

        delegate void WriteAction(ref ByteWriter w);

        static bool ValidateServerCertificate(object sender, X509Certificate certificate,
            X509Chain chain, SslPolicyErrors errors) =>
            // The relay's cert is self-signed for now (see CertificateProvider) —
            // pinning/CA validation is deployment-phase work (M11 plan phase 6),
            // not a protocol concern.
            true;

        public void Send(int peerId, byte[] payload, int length)
        {
            var w = new ByteWriter(length + 16);
            ControlProtocol.WriteRoomRelay(ref w, peerId, payload, length);
            if (!_outbox.IsAddingCompleted)
                _outbox.Add(w.ToArray());
        }

        public bool TryReceive(out int fromPeerId, out byte[] payload, out int length)
        {
            if (_inbox.TryDequeue(out var item))
            {
                fromPeerId = item.from;
                payload = item.payload;
                length = item.length;
                return true;
            }
            fromPeerId = -1;
            payload = null;
            length = 0;
            return false;
        }

        public bool TryDequeueConnectionEvent(out int peerId, out bool connected)
        {
            if (_connectionEvents.TryDequeue(out var evt))
            {
                peerId = evt.peerId;
                connected = evt.connected;
                return true;
            }
            peerId = -1;
            connected = false;
            return false;
        }

        /// <summary>Room-wide text chat — a control-plane message on this
        /// same connection, NOT sent through <see cref="Send"/>: that path
        /// carries the game protocol's own NetMessageKind-tagged bytes, and
        /// HostTurnExchange/ClientTurnExchange would mis-parse anything else
        /// arriving on it.</summary>
        public void SendChat(string senderName, string text)
        {
            var w = new ByteWriter(text.Length + senderName.Length + 16);
            ControlProtocol.WriteChatMessage(ref w, senderName, text);
            if (!_outbox.IsAddingCompleted)
                _outbox.Add(w.ToArray());
        }

        public bool TryReceiveChat(out string senderName, out string text)
        {
            if (_chatInbox.TryDequeue(out var item))
            {
                senderName = item.senderName;
                text = item.text;
                return true;
            }
            senderName = null;
            text = null;
            return false;
        }

        /// <summary>Report a finished match's outcome — the elected host
        /// only (see RatingService: trusted for v1, one report per match).
        /// Fire-and-forget: no client-side handling of the ack yet, the wire
        /// message exists for a future confirmation UI.</summary>
        public void ReportMatchResult(string map, string mode, string[] usernames, bool[] won)
        {
            var w = new ByteWriter(64 + usernames.Length * 24);
            ControlProtocol.WriteReportMatchResult(ref w, map, mode, usernames, won);
            if (!_outbox.IsAddingCompleted)
                _outbox.Add(w.ToArray());
        }

        /// <summary>No-op beyond what the queues already provide — the
        /// reader/writer tasks run continuously in the background, unlike
        /// LoopbackPacketPeer/UtpPeerSocket's synchronous-pump model. Present
        /// for interface parity and because a future version may use it to
        /// surface backpressure.</summary>
        public void Poll() { }

        async Task ReadLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    byte[] frame = await StreamFraming.ReadFrameAsync(_ssl, _cts.Token).ConfigureAwait(false);
                    if (frame == null)
                        break;

                    var r = new ByteReader(frame);
                    var kind = (ControlMessageKind)r.ReadByte();
                    switch (kind)
                    {
                        case ControlMessageKind.RoomRelay:
                            ControlProtocol.ReadRoomRelay(ref r, out int fromPeerId, out byte[] payload);
                            _inbox.Enqueue((fromPeerId, payload, payload.Length));
                            break;

                        case ControlMessageKind.RoomPeerEvent:
                            ControlProtocol.ReadRoomPeerEvent(ref r, out int peerId, out bool connected);
                            _connectionEvents.Enqueue((peerId, connected));
                            break;

                        case ControlMessageKind.ChatBroadcast:
                            ControlProtocol.ReadChatBroadcast(ref r, out string senderName, out string text);
                            _chatInbox.Enqueue((senderName, text));
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Any read failure means the relay connection is gone — the
                // driver above learns this through Status/starvation rather
                // than a synthesized per-peer event, since no single room
                // peer id represents "the relay itself".
            }
            finally
            {
                Faulted = true;
                _outbox.CompleteAdding();
            }
        }

        async Task WriteLoopAsync()
        {
            try
            {
                foreach (byte[] frame in _outbox.GetConsumingEnumerable(_cts.Token))
                    await StreamFraming.WriteFrameAsync(_ssl, frame, frame.Length, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                Faulted = true;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _outbox.CompleteAdding();
            try { _readerTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
            try { _writerTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
            _ssl.Dispose();
            _tcp.Dispose();
            _cts.Dispose();
        }
    }
}
