using System.Collections.Generic;

namespace Craftwar.Net
{
    /// <summary>
    /// An in-memory switch implementing <see cref="IPacketPeer"/>. Lets the real
    /// host/client protocol — serialization included — run several peers in one
    /// process, so the headless harness proves the same code LAN play uses, with
    /// only the socket swapped out.
    ///
    /// Delivery is immediate and lossless, which is the right model here: the
    /// transport underneath is a reliable ordered pipeline, so loss and reorder
    /// are its problem, not the protocol's. What this does exercise is the part
    /// that can still go wrong above the socket — slot mapping, turn completion,
    /// commit fan-out and the byte format.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        readonly List<LoopbackPacketPeer> _peers = new List<LoopbackPacketPeer>();

        public LoopbackPacketPeer CreatePeer()
        {
            var peer = new LoopbackPacketPeer(this, _peers.Count);
            // Everyone already present learns about the newcomer, and vice versa.
            for (int i = 0; i < _peers.Count; i++)
            {
                _peers[i].EnqueueConnection(peer.LocalPeerId, true);
                peer.EnqueueConnection(_peers[i].LocalPeerId, true);
            }
            _peers.Add(peer);
            return peer;
        }

        internal void Deliver(int fromPeerId, int toPeerId, byte[] payload, int length)
        {
            var copy = new byte[length];
            System.Array.Copy(payload, copy, length);
            if (toPeerId == IPacketPeer.Broadcast)
            {
                for (int i = 0; i < _peers.Count; i++)
                    if (_peers[i].LocalPeerId != fromPeerId)
                        _peers[i].EnqueueReceive(fromPeerId, copy, length);
                return;
            }
            for (int i = 0; i < _peers.Count; i++)
                if (_peers[i].LocalPeerId == toPeerId)
                    _peers[i].EnqueueReceive(fromPeerId, copy, length);
        }

        /// <summary>Stop delivering to a peer, as a cable pull would.</summary>
        public void Disconnect(int peerId)
        {
            for (int i = _peers.Count - 1; i >= 0; i--)
            {
                if (_peers[i].LocalPeerId != peerId)
                {
                    _peers[i].EnqueueConnection(peerId, false);
                    continue;
                }
                _peers.RemoveAt(i);
            }
        }
    }

    public sealed class LoopbackPacketPeer : IPacketPeer
    {
        readonly LoopbackNetwork _network;
        readonly Queue<(int from, byte[] payload, int length)> _inbox
            = new Queue<(int, byte[], int)>();
        readonly Queue<(int peerId, bool connected)> _connectionEvents
            = new Queue<(int, bool)>();

        internal LoopbackPacketPeer(LoopbackNetwork network, int peerId)
        {
            _network = network;
            LocalPeerId = peerId;
        }

        public int LocalPeerId { get; }

        /// <summary>Peer 0 hosts, by construction.</summary>
        public bool IsHost => LocalPeerId == 0;

        public void Send(int peerId, byte[] payload, int length) =>
            _network.Deliver(LocalPeerId, peerId, payload, length);

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

        public void Poll() { }

        public void Dispose() { }

        internal void EnqueueReceive(int from, byte[] payload, int length) =>
            _inbox.Enqueue((from, payload, length));

        internal void EnqueueConnection(int peerId, bool connected) =>
            _connectionEvents.Enqueue((peerId, connected));
    }
}
