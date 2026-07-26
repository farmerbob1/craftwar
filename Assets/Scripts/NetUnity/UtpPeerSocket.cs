using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
// The per-stage parameter setters (WithReliableStageParameters,
// WithFragmentationStageParameters) are extensions in the Utilities namespace,
// not members of NetworkSettings.
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Craftwar.Net.Unity
{
    /// <summary>
    /// Unity Transport behind <see cref="IPacketPeer"/>. Everything above this
    /// class is engine-free and tested headlessly; this is the only part that
    /// needs a real machine, so it is kept as thin as possible — connections in,
    /// bytes out, no protocol decisions.
    ///
    /// All traffic goes down one Fragmentation -> ReliableSequenced pipeline.
    /// Lockstep cannot proceed past a missing turn, so there is nothing here
    /// that wants to be unreliable; ordering and retransmission are exactly what
    /// we would otherwise have to build.
    /// </summary>
    public sealed class UtpPeerSocket : IPacketPeer
    {
        public const ushort DefaultPort = 27015;

        /// <summary>Stay under the 1400-byte MTU so ordinary turn traffic never
        /// fragments; larger payloads (state snapshots) are chunked by the
        /// protocol above rather than leaning on the fragmentation stage, which
        /// would consume several reliable-window slots per message.</summary>
        public const int SafePayloadBytes = 1200;

        NetworkDriver _driver;
        NetworkPipeline _pipeline;
        NativeList<NetworkConnection> _connections;
        NetworkConnection _hostConnection;

        readonly Queue<(int from, byte[] payload, int length)> _inbox
            = new Queue<(int, byte[], int)>();
        readonly Queue<(int peerId, bool connected)> _connectionEvents
            = new Queue<(int, bool)>();
        readonly Dictionary<int, int> _peerIdByConnectionId = new Dictionary<int, int>();
        readonly List<(int peerId, NetworkConnection connection)> _peers
            = new List<(int, NetworkConnection)>();

        int _nextPeerId = 1;
        bool _disposed;

        UtpPeerSocket(bool isHost)
        {
            IsHost = isHost;
            LocalPeerId = isHost ? 0 : -1;

            var settings = new NetworkSettings();
            // UTP's own disconnect timeout defaults to 30 s, far longer than the
            // grace a stalled match can tolerate; the drop decision is driven by
            // turn starvation above, and this just stops a dead socket lingering.
            settings.WithNetworkConfigParameters(disconnectTimeoutMS: 5000);
            settings.WithReliableStageParameters(windowSize: 64);
            settings.WithFragmentationStageParameters(payloadCapacity: 4096);

            _driver = NetworkDriver.Create(settings);
            // Stage order must be IDENTICAL on both ends. UTP does not validate
            // it, and a mismatch produces garbage rather than an error.
            _pipeline = _driver.CreatePipeline(
                typeof(FragmentationPipelineStage),
                typeof(ReliableSequencedPipelineStage));
            _connections = new NativeList<NetworkConnection>(8, Allocator.Persistent);
        }

        public int LocalPeerId { get; private set; }
        public bool IsHost { get; }

        /// <summary>True once a client's connection has completed.</summary>
        public bool IsConnected => IsHost || _hostConnection.IsCreated;

        public static UtpPeerSocket Host(ushort port = DefaultPort)
        {
            var socket = new UtpPeerSocket(true);
            var endpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            if (socket._driver.Bind(endpoint) != 0)
            {
                socket.Dispose();
                throw new InvalidOperationException(
                    $"Could not bind UDP port {port}. Another game may already be hosting.");
            }
            socket._driver.Listen();
            return socket;
        }

        public static UtpPeerSocket Join(string address, ushort port = DefaultPort)
        {
            var socket = new UtpPeerSocket(false);
            if (socket._driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                socket.Dispose();
                throw new InvalidOperationException("Could not bind a local UDP port.");
            }
            if (!NetworkEndpoint.TryParse(address, port, out var endpoint))
            {
                socket.Dispose();
                throw new ArgumentException($"'{address}' is not a valid address.", nameof(address));
            }
            socket._hostConnection = socket._driver.Connect(endpoint);
            return socket;
        }

        public void Poll()
        {
            if (_disposed)
                return;

            _driver.ScheduleUpdate().Complete();

            if (IsHost)
            {
                NetworkConnection incoming;
                while ((incoming = _driver.Accept()) != default)
                {
                    int peerId = _nextPeerId++;
                    _connections.Add(incoming);
                    _peers.Add((peerId, incoming));
                    _peerIdByConnectionId[incoming.GetHashCode()] = peerId;
                    _connectionEvents.Enqueue((peerId, true));
                }
            }

            for (int i = _peers.Count - 1; i >= 0; i--)
                PumpConnection(_peers[i].peerId, _peers[i].connection, i);

            if (!IsHost && _hostConnection.IsCreated)
                PumpConnection(0, _hostConnection, -1);
        }

        void PumpConnection(int peerId, NetworkConnection connection, int peerIndex)
        {
            NetworkEvent.Type kind;
            while ((kind = _driver.PopEventForConnection(connection, out var reader))
                   != NetworkEvent.Type.Empty)
            {
                switch (kind)
                {
                    case NetworkEvent.Type.Connect:
                        // Only a client sees this; the host assigns our id.
                        _connectionEvents.Enqueue((peerId, true));
                        break;

                    case NetworkEvent.Type.Data:
                        var payload = new byte[reader.Length];
                        var native = new NativeArray<byte>(reader.Length, Allocator.Temp);
                        reader.ReadBytes(native);
                        native.CopyTo(payload);
                        native.Dispose();
                        _inbox.Enqueue((peerId, payload, payload.Length));
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _connectionEvents.Enqueue((peerId, false));
                        if (peerIndex >= 0)
                        {
                            _peers.RemoveAt(peerIndex);
                            _peerIdByConnectionId.Remove(connection.GetHashCode());
                        }
                        else
                        {
                            _hostConnection = default;
                        }
                        return;
                }
            }
        }

        public void Send(int peerId, byte[] payload, int length)
        {
            if (_disposed || length <= 0)
                return;

            if (peerId == IPacketPeer.Broadcast)
            {
                for (int i = 0; i < _peers.Count; i++)
                    SendTo(_peers[i].connection, payload, length);
                return;
            }
            if (!IsHost)
            {
                SendTo(_hostConnection, payload, length);
                return;
            }
            for (int i = 0; i < _peers.Count; i++)
                if (_peers[i].peerId == peerId)
                {
                    SendTo(_peers[i].connection, payload, length);
                    return;
                }
        }

        void SendTo(NetworkConnection connection, byte[] payload, int length)
        {
            if (!connection.IsCreated)
                return;

            int status = _driver.BeginSend(_pipeline, connection, out var writer);
            if (status != 0)
            {
                // NetworkSendQueueFull and friends are silent killers: a dropped
                // turn packet presents as an unexplained permanent stall, because
                // lockstep simply waits forever for input that was never sent.
                Debug.LogWarning($"[craftwar-net] BeginSend failed ({status}); turn traffic dropped");
                return;
            }

            var native = new NativeArray<byte>(length, Allocator.Temp);
            NativeArray<byte>.Copy(payload, native, length);
            writer.WriteBytes(native);
            native.Dispose();

            int sent = _driver.EndSend(writer);
            if (sent < 0)
                Debug.LogWarning($"[craftwar-net] EndSend failed ({sent}); turn traffic dropped");
        }

        public bool TryReceive(out int fromPeerId, out byte[] payload, out int length)
        {
            if (_inbox.Count == 0)
            {
                fromPeerId = -1;
                payload = null;
                length = 0;
                return false;
            }
            var (from, buffer, len) = _inbox.Dequeue();
            fromPeerId = from;
            payload = buffer;
            length = len;
            return true;
        }

        public bool TryDequeueConnectionEvent(out int peerId, out bool connected)
        {
            if (_connectionEvents.Count == 0)
            {
                peerId = -1;
                connected = false;
                return false;
            }
            var (id, state) = _connectionEvents.Dequeue();
            peerId = id;
            connected = state;
            return true;
        }

        /// <summary>Host-side: the transport ids of everyone currently connected.</summary>
        public IReadOnlyList<(int peerId, NetworkConnection connection)> ConnectedPeers => _peers;

        /// <summary>A client learns its own id from the host's join acceptance.</summary>
        public void SetLocalPeerId(int peerId) => LocalPeerId = peerId;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_connections.IsCreated)
                _connections.Dispose();
            if (_driver.IsCreated)
                _driver.Dispose();
        }
    }
}
