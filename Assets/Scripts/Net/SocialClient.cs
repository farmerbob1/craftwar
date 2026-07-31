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

        readonly ConcurrentQueue<(bool ok, string reason, string channelName, string[] members, string opUsername, string motd)>
            _channelJoinResults = new();
        readonly ConcurrentQueue<(string channelName, string username, bool joined, string opUsername)>
            _channelMemberEvents = new();
        readonly ConcurrentQueue<(string channelName, string senderUsername, string text)> _channelChatInbox = new();
        readonly ConcurrentQueue<(bool ok, string reason)> _channelKickResults = new();
        readonly ConcurrentQueue<(string channelName, string byUsername)> _channelKickedInbox = new();
        readonly ConcurrentQueue<(bool ok, string reason)> _channelSetMotdResults = new();
        readonly ConcurrentQueue<(string channelName, string motd)> _channelMotdChanged = new();

        readonly ConcurrentQueue<(bool ok, string reason, bool becameFriends)> _friendRequestResults = new();
        readonly ConcurrentQueue<string> _friendRequestsReceived = new();
        readonly ConcurrentQueue<(bool ok, string reason)> _friendRespondResults = new();
        readonly ConcurrentQueue<(string byUsername, bool accepted)> _friendRequestsAnswered = new();
        readonly ConcurrentQueue<(bool ok, string reason)> _friendRemoveResults = new();
        readonly ConcurrentQueue<string> _friendsRemoved = new();
        readonly ConcurrentQueue<(string[] friendUsernames, bool[] friendOnline, string[] incoming, string[] outgoing)>
            _friendListResults = new();

        readonly ConcurrentQueue<(bool ok, string reason)> _whisperResults = new();
        readonly ConcurrentQueue<(string fromUsername, string toUsername, string text)> _whisperInbox = new();

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

        /// <summary>Refused server-side unless this connection is the
        /// current channel's operator — see ChannelSetMotdResult.</summary>
        public void SetChannelMotd(string motd)
        {
            var w = new ByteWriter((motd?.Length ?? 0) + 16);
            ControlProtocol.WriteChannelSetMotd(ref w, motd);
            Enqueue(ref w);
        }

        /// <summary>A mutual request (the target already requested you)
        /// completes the friendship immediately — see FriendRequestResult's
        /// becameFriends flag.</summary>
        public void SendFriendRequest(string toUsername)
        {
            var w = new ByteWriter(toUsername.Length + 16);
            ControlProtocol.WriteFriendRequest(ref w, toUsername);
            Enqueue(ref w);
        }

        public void RespondToFriendRequest(string requesterUsername, bool accept)
        {
            var w = new ByteWriter(requesterUsername.Length + 16);
            ControlProtocol.WriteFriendRespond(ref w, requesterUsername, accept);
            Enqueue(ref w);
        }

        public void RemoveFriend(string username)
        {
            var w = new ByteWriter(username.Length + 16);
            ControlProtocol.WriteFriendRemove(ref w, username);
            Enqueue(ref w);
        }

        /// <summary>Poll-on-demand, matching the M12 design decision that
        /// presence is never proactively pushed beyond request/accept/remove
        /// events — call this periodically (the menu polls it the same way
        /// it polls the room browser).</summary>
        public void RequestFriendList()
        {
            var w = new ByteWriter(16);
            ControlProtocol.WriteFriendListRequest(ref w);
            Enqueue(ref w);
        }

        /// <summary>Not restricted to friends — matches the original
        /// Battle.net /w model, where you can whisper anyone by name.</summary>
        public void SendWhisper(string toUsername, string text)
        {
            var w = new ByteWriter(toUsername.Length + text.Length + 16);
            ControlProtocol.WriteWhisper(ref w, toUsername, text);
            Enqueue(ref w);
        }

        void Enqueue(ref ByteWriter w)
        {
            if (!_outbox.IsAddingCompleted)
                _outbox.Add(w.ToArray());
        }

        // --- Inbound ---------------------------------------------------------------

        public bool TryReceiveChannelJoinResult(out bool ok, out string reason, out string channelName,
            out string[] members, out string opUsername, out string motd)
        {
            if (_channelJoinResults.TryDequeue(out var item))
            {
                (ok, reason, channelName, members, opUsername, motd) = item;
                return true;
            }
            ok = false;
            reason = null;
            channelName = null;
            members = null;
            opUsername = null;
            motd = null;
            return false;
        }

        public bool TryReceiveChannelSetMotdResult(out bool ok, out string reason)
        {
            if (_channelSetMotdResults.TryDequeue(out var item))
            {
                (ok, reason) = item;
                return true;
            }
            ok = false;
            reason = null;
            return false;
        }

        public bool TryReceiveChannelMotdChanged(out string channelName, out string motd)
        {
            if (_channelMotdChanged.TryDequeue(out var item))
            {
                (channelName, motd) = item;
                return true;
            }
            channelName = null;
            motd = null;
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

        public bool TryReceiveFriendRequestResult(out bool ok, out string reason, out bool becameFriends)
        {
            if (_friendRequestResults.TryDequeue(out var item))
            {
                (ok, reason, becameFriends) = item;
                return true;
            }
            ok = false;
            reason = null;
            becameFriends = false;
            return false;
        }

        public bool TryReceiveFriendRequestReceived(out string fromUsername) =>
            _friendRequestsReceived.TryDequeue(out fromUsername);

        public bool TryReceiveFriendRespondResult(out bool ok, out string reason)
        {
            if (_friendRespondResults.TryDequeue(out var item))
            {
                (ok, reason) = item;
                return true;
            }
            ok = false;
            reason = null;
            return false;
        }

        public bool TryReceiveFriendRequestAnswered(out string byUsername, out bool accepted)
        {
            if (_friendRequestsAnswered.TryDequeue(out var item))
            {
                (byUsername, accepted) = item;
                return true;
            }
            byUsername = null;
            accepted = false;
            return false;
        }

        public bool TryReceiveFriendRemoveResult(out bool ok, out string reason)
        {
            if (_friendRemoveResults.TryDequeue(out var item))
            {
                (ok, reason) = item;
                return true;
            }
            ok = false;
            reason = null;
            return false;
        }

        public bool TryReceiveFriendRemoved(out string byUsername) => _friendsRemoved.TryDequeue(out byUsername);

        public bool TryReceiveFriendListResult(out string[] friendUsernames, out bool[] friendOnline,
            out string[] incoming, out string[] outgoing)
        {
            if (_friendListResults.TryDequeue(out var item))
            {
                (friendUsernames, friendOnline, incoming, outgoing) = item;
                return true;
            }
            friendUsernames = null;
            friendOnline = null;
            incoming = null;
            outgoing = null;
            return false;
        }

        public bool TryReceiveWhisperResult(out bool ok, out string reason)
        {
            if (_whisperResults.TryDequeue(out var item))
            {
                (ok, reason) = item;
                return true;
            }
            ok = false;
            reason = null;
            return false;
        }

        /// <summary>fromUsername == Username means this is the echo of a
        /// whisper WE sent (see WhisperReceived's doc comment).</summary>
        public bool TryReceiveWhisperReceived(out string fromUsername, out string toUsername, out string text)
        {
            if (_whisperInbox.TryDequeue(out var item))
            {
                (fromUsername, toUsername, text) = item;
                return true;
            }
            fromUsername = null;
            toUsername = null;
            text = null;
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
                                out string joinedChannel, out string[] members, out string joinOp, out string joinMotd);
                            _channelJoinResults.Enqueue((joinOk, joinReason, joinedChannel, members, joinOp, joinMotd));
                            break;

                        case ControlMessageKind.ChannelSetMotdResult:
                            ControlProtocol.ReadChannelSetMotdResult(ref r, out bool motdOk, out string motdReason);
                            _channelSetMotdResults.Enqueue((motdOk, motdReason));
                            break;

                        case ControlMessageKind.ChannelMotdChanged:
                            ControlProtocol.ReadChannelMotdChanged(ref r, out string motdChannel, out string newMotd);
                            _channelMotdChanged.Enqueue((motdChannel, newMotd));
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

                        case ControlMessageKind.FriendRequestResult:
                            ControlProtocol.ReadFriendRequestResult(ref r, out bool freqOk, out string freqReason,
                                out bool becameFriends);
                            _friendRequestResults.Enqueue((freqOk, freqReason, becameFriends));
                            break;

                        case ControlMessageKind.FriendRequestReceived:
                            _friendRequestsReceived.Enqueue(ControlProtocol.ReadFriendRequestReceived(ref r));
                            break;

                        case ControlMessageKind.FriendRespondResult:
                            ControlProtocol.ReadFriendRespondResult(ref r, out bool frespOk, out string frespReason);
                            _friendRespondResults.Enqueue((frespOk, frespReason));
                            break;

                        case ControlMessageKind.FriendRequestAnswered:
                            ControlProtocol.ReadFriendRequestAnswered(ref r, out string answeredBy, out bool accepted);
                            _friendRequestsAnswered.Enqueue((answeredBy, accepted));
                            break;

                        case ControlMessageKind.FriendRemoveResult:
                            ControlProtocol.ReadFriendRemoveResult(ref r, out bool fremOk, out string fremReason);
                            _friendRemoveResults.Enqueue((fremOk, fremReason));
                            break;

                        case ControlMessageKind.FriendRemoved:
                            _friendsRemoved.Enqueue(ControlProtocol.ReadFriendRemoved(ref r));
                            break;

                        case ControlMessageKind.FriendListResult:
                            ControlProtocol.ReadFriendListResult(ref r, out string[] friendUsernames,
                                out bool[] friendOnline, out string[] incoming, out string[] outgoing);
                            _friendListResults.Enqueue((friendUsernames, friendOnline, incoming, outgoing));
                            break;

                        case ControlMessageKind.WhisperResult:
                            ControlProtocol.ReadWhisperResult(ref r, out bool whispOk, out string whispReason);
                            _whisperResults.Enqueue((whispOk, whispReason));
                            break;

                        case ControlMessageKind.WhisperReceived:
                            ControlProtocol.ReadWhisperReceived(ref r, out string whispFrom, out string whispTo,
                                out string whispText);
                            _whisperInbox.Enqueue((whispFrom, whispTo, whispText));
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
