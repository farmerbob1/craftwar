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
    /// The social layer's persistent connection: chat channels today,
    /// whispers/friends/clans in later M12 phases. Deliberately a SEPARATE
    /// connection from <see cref="RelayPeerSocket"/> (the room-relay
    /// transport) and <see cref="OnlineAccountClient"/> (one-shot account
    /// calls) — see the M12 plan's mechanism note 2. This means a player is
    /// reachable for chat/whispers/friends whether or not they are currently
    /// hosting/browsing/in a match, and the proven room-relay/game-traffic
    /// path is touched by none of this.
    ///
    /// Structurally a sibling of RelayPeerSocket: pure BCL (TcpClient/
    /// SslStream, zero Unity dependency, exercised by the standalone Sim/Net
    /// test harness), a background reader task unwrapping frames into
    /// thread-safe queues that TryReceiveXxx methods drain, and a writer
    /// task owning the one outbound stream so every Xxx() call is
    /// non-blocking on the caller.
    ///
    /// <see cref="Connect"/> authenticates via ResumeSession — the session
    /// token a prior <see cref="OnlineAccountClient.Login"/>/Register call
    /// already obtained. That's exactly what session tokens are for (see
    /// AccountService's own doc comment): resuming a session on a NEW
    /// connection, which is exactly what opening this persistent connection
    /// after that one-shot login is doing. Connecting also auto-joins the
    /// default channel ("Town Hall"), so a connected SocialClient is always
    /// in a channel, mirroring RelayPeerSocket.Host's "connect implies
    /// you're in a room" ergonomics.
    /// </summary>
    public sealed class SocialClient : IDisposable
    {
        public const string DefaultChannelName = "Town Hall";

        readonly TcpClient _tcp;
        readonly SslStream _ssl;
        readonly CancellationTokenSource _cts = new();
        readonly BlockingCollection<byte[]> _outbox = new();
        readonly Task _readerTask;
        readonly Task _writerTask;

        readonly ConcurrentQueue<(bool ok, string reason, string channelName, string[] members, string opUsername)>
            _channelJoinResults = new();
        readonly ConcurrentQueue<(string channelName, string username, bool joined, string opUsername)>
            _channelMemberEvents = new();
        readonly ConcurrentQueue<(string channelName, string senderUsername, string text)> _channelChatInbox = new();
        readonly ConcurrentQueue<(bool ok, string reason)> _channelKickResults = new();
        readonly ConcurrentQueue<(string channelName, string byUsername)> _channelKickedInbox = new();

        public string Username { get; }

        /// <summary>True once the connection has dropped — same meaning as
        /// RelayPeerSocket.Faulted.</summary>
        public bool Faulted { get; private set; }

        SocialClient(TcpClient tcp, SslStream ssl, string username)
        {
            _tcp = tcp;
            _ssl = ssl;
            Username = username;
            _readerTask = Task.Run(ReadLoopAsync);
            _writerTask = Task.Run(WriteLoopAsync);
        }

        /// <summary>Connect, resume the session, and auto-join the default
        /// channel. The join result arrives asynchronously through
        /// <see cref="TryReceiveChannelJoinResult"/> like any other join —
        /// no special first-frame case, since the background reader loop is
        /// already running by the time it's requested.</summary>
        public static SocialClient Connect(string serverHost, int serverPort, string sessionToken)
        {
            var tcp = new TcpClient();
            tcp.Connect(serverHost, serverPort);
            tcp.NoDelay = true;
            var ssl = new SslStream(tcp.GetStream(), false, ValidateServerCertificate);
            ssl.AuthenticateAsClient(serverHost);

            SendHandshakeFrame(ssl, (ref ByteWriter w) => ControlProtocol.WriteHello(ref w, ControlProtocol.CurrentVersion));
            var ack = ReadHandshakeFrame(ssl);
            ControlProtocol.ReadHelloAck(ref ack, out bool accepted, out string helloReason);
            if (!accepted)
                throw new InvalidOperationException($"relay server refused the connection: {helloReason}");

            SendHandshakeFrame(ssl, (ref ByteWriter w) => ControlProtocol.WriteResumeSession(ref w, sessionToken));
            var resumed = ReadHandshakeFrame(ssl);
            ControlProtocol.ReadResumeSessionResult(ref resumed, out var result, out string username);
            if (result != AccountResult.Ok)
                throw new InvalidOperationException($"could not resume session: {result}");

            var client = new SocialClient(tcp, ssl, username);
            client.JoinChannel(DefaultChannelName);
            return client;
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
            X509Chain chain, SslPolicyErrors errors) => true; // self-signed dev cert — see CertificateProvider

        // --- Outbound ------------------------------------------------------------

        /// <summary>Leaves whatever channel this connection is currently in
        /// (if any) and joins/creates the named one.</summary>
        public void JoinChannel(string channelName)
        {
            var w = new ByteWriter(channelName.Length + 16);
            ControlProtocol.WriteChannelJoin(ref w, channelName);
            Enqueue(ref w);
        }

        public void SendChannelChat(string text)
        {
            var w = new ByteWriter(text.Length + 16);
            ControlProtocol.WriteChannelChat(ref w, text);
            Enqueue(ref w);
        }

        /// <summary>Refused server-side unless this connection is the
        /// current channel's operator — see ChannelKickResult.</summary>
        public void KickFromChannel(string targetUsername)
        {
            var w = new ByteWriter(targetUsername.Length + 16);
            ControlProtocol.WriteChannelKick(ref w, targetUsername);
            Enqueue(ref w);
        }

        void Enqueue(ref ByteWriter w)
        {
            if (!_outbox.IsAddingCompleted)
                _outbox.Add(w.ToArray());
        }

        // --- Inbound ---------------------------------------------------------------

        public bool TryReceiveChannelJoinResult(out bool ok, out string reason, out string channelName,
            out string[] members, out string opUsername)
        {
            if (_channelJoinResults.TryDequeue(out var item))
            {
                (ok, reason, channelName, members, opUsername) = item;
                return true;
            }
            ok = false;
            reason = null;
            channelName = null;
            members = null;
            opUsername = null;
            return false;
        }

        public bool TryReceiveChannelMemberEvent(out string channelName, out string username, out bool joined,
            out string opUsername)
        {
            if (_channelMemberEvents.TryDequeue(out var item))
            {
                (channelName, username, joined, opUsername) = item;
                return true;
            }
            channelName = null;
            username = null;
            joined = false;
            opUsername = null;
            return false;
        }

        public bool TryReceiveChannelChat(out string channelName, out string senderUsername, out string text)
        {
            if (_channelChatInbox.TryDequeue(out var item))
            {
                (channelName, senderUsername, text) = item;
                return true;
            }
            channelName = null;
            senderUsername = null;
            text = null;
            return false;
        }

        public bool TryReceiveChannelKickResult(out bool ok, out string reason)
        {
            if (_channelKickResults.TryDequeue(out var item))
            {
                (ok, reason) = item;
                return true;
            }
            ok = false;
            reason = null;
            return false;
        }

        /// <summary>Distinct from an ordinary ChannelMemberEvent departure —
        /// this is only ever raised about THIS connection's own account.</summary>
        public bool TryReceiveChannelKicked(out string channelName, out string byUsername)
        {
            if (_channelKickedInbox.TryDequeue(out var item))
            {
                (channelName, byUsername) = item;
                return true;
            }
            channelName = null;
            byUsername = null;
            return false;
        }

        // --- Background I/O ---------------------------------------------------------

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
                        case ControlMessageKind.ChannelJoinResult:
                            ControlProtocol.ReadChannelJoinResult(ref r, out bool joinOk, out string joinReason,
                                out string joinedChannel, out string[] members, out string joinOp);
                            _channelJoinResults.Enqueue((joinOk, joinReason, joinedChannel, members, joinOp));
                            break;

                        case ControlMessageKind.ChannelMemberEvent:
                            ControlProtocol.ReadChannelMemberEvent(ref r, out string eventChannel,
                                out string eventUsername, out bool eventJoined, out string eventOp);
                            _channelMemberEvents.Enqueue((eventChannel, eventUsername, eventJoined, eventOp));
                            break;

                        case ControlMessageKind.ChannelChatBroadcast:
                            ControlProtocol.ReadChannelChatBroadcast(ref r, out string chatChannel,
                                out string senderUsername, out string text);
                            _channelChatInbox.Enqueue((chatChannel, senderUsername, text));
                            break;

                        case ControlMessageKind.ChannelKickResult:
                            ControlProtocol.ReadChannelKickResult(ref r, out bool kickOk, out string kickReason);
                            _channelKickResults.Enqueue((kickOk, kickReason));
                            break;

                        case ControlMessageKind.ChannelKicked:
                            ControlProtocol.ReadChannelKicked(ref r, out string kickedChannel, out string byUsername);
                            _channelKickedInbox.Enqueue((kickedChannel, byUsername));
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Any read failure means the social connection is gone — the
                // caller learns this through Faulted, same as RelayPeerSocket.
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
