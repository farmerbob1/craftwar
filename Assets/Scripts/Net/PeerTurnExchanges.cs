using System;
using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// The host half of the star topology. Feeds its own and every client's
    /// input into a <see cref="TurnRelay"/>, and broadcasts each turn's frozen
    /// bundle once the set is complete.
    ///
    /// The host is a player, not a referee: its own input goes through the same
    /// relay, and its own state hash is one of the values compared. A pure relay
    /// would have no hash of its own to compare against, which is the whole
    /// mechanism for spotting a desync.
    /// </summary>
    public sealed class HostTurnExchange : ITurnExchange, IDisposable
    {
        readonly IPacketPeer _peer;
        readonly TurnRelay _relay;
        readonly byte _localSlot;
        readonly Dictionary<int, byte> _slotByPeer = new Dictionary<int, byte>();
        readonly List<GameCommand> _scratch = new List<GameCommand>();
        readonly Action<string> _log;

        int _broadcastThroughTurn = -1;

        public HostTurnExchange(IPacketPeer peer, TurnRelay relay, byte localSlot, Action<string> log = null)
        {
            _peer = peer;
            _relay = relay;
            _localSlot = localSlot;
            _log = log;
            _relay.Desynced += OnRelayDesync;
        }

        public NetStatus Status { get; private set; } = NetStatus.Running;

        public event Action<DesyncReport> Desynced;

        /// <summary>Raised when a connected client's transport drops.</summary>
        public event Action<byte> PeerDropped;

        /// <summary>Bind a transport peer to the lobby seat it was given.</summary>
        public void AssignSlot(int peerId, byte slot) => _slotByPeer[peerId] = slot;

        public void SendInput(int turn, List<GameCommand> commands, int hashTurn, uint stateHash) =>
            _relay.SubmitInput(_localSlot, turn, commands, hashTurn, stateHash);

        public bool TryGetCommit(int turn, List<GameCommand> into) =>
            _relay.TryGetCommitted(turn, into);

        public void Poll()
        {
            _peer.Poll();

            while (_peer.TryDequeueConnectionEvent(out int peerId, out bool connected))
            {
                if (connected || !_slotByPeer.TryGetValue(peerId, out byte lost))
                    continue;
                _slotByPeer.Remove(peerId);
                PeerDropped?.Invoke(lost);
            }

            while (_peer.TryReceive(out int from, out byte[] payload, out int length))
                Handle(from, payload, length);

            BroadcastNewlyFrozenTurns();
        }

        void Handle(int fromPeerId, byte[] payload, int length)
        {
            var r = new ByteReader(payload, length);
            var kind = (NetMessageKind)r.ReadByte();
            if (kind != NetMessageKind.TurnInput)
                return;

            NetMessages.ReadTurnInput(ref r, out byte claimedSlot, out int turn,
                _scratch, out int hashTurn, out uint hash);

            // The peer's seat comes from the lobby assignment, never from the
            // packet: a client does not get to decide which slot it is.
            if (!_slotByPeer.TryGetValue(fromPeerId, out byte slot))
            {
                _log?.Invoke($"[net] input from unassigned peer {fromPeerId}, ignored");
                return;
            }
            if (claimedSlot != slot)
                _log?.Invoke($"[net] peer {fromPeerId} claimed slot {claimedSlot}, is {slot}");

            _relay.SubmitInput(slot, turn, _scratch, hashTurn, hash);
        }

        void BroadcastNewlyFrozenTurns()
        {
            while (_broadcastThroughTurn < _relay.HighestCommittedTurn)
            {
                int turn = _broadcastThroughTurn + 1;
                _scratch.Clear();
                if (!_relay.TryGetCommitted(turn, _scratch))
                    break;
                var w = new ByteWriter(64);
                NetMessages.WriteTurnCommit(ref w, turn, _scratch);
                _peer.Send(IPacketPeer.Broadcast, w.Buffer, w.Position);
                _broadcastThroughTurn = turn;
            }
        }

        void OnRelayDesync(DesyncReport report)
        {
            Status = NetStatus.Desynced;
            var w = new ByteWriter(32);
            NetMessages.WriteDesyncHalt(ref w, report);
            _peer.Send(IPacketPeer.Broadcast, w.Buffer, w.Position);
            Desynced?.Invoke(report);
        }

        public void Dispose()
        {
            _relay.Desynced -= OnRelayDesync;
            _peer.Dispose();
        }
    }

    /// <summary>
    /// The client half: publish our own input to the host, execute the bundles
    /// it sends back. A client never decides what a turn contains — that is what
    /// keeps every peer executing the identical command stream.
    /// </summary>
    public sealed class ClientTurnExchange : ITurnExchange, IDisposable
    {
        public const int HostPeerId = 0;

        readonly IPacketPeer _peer;
        readonly byte _localSlot;
        readonly Dictionary<int, List<GameCommand>> _commits = new Dictionary<int, List<GameCommand>>();
        readonly List<GameCommand> _scratch = new List<GameCommand>();

        public ClientTurnExchange(IPacketPeer peer, byte localSlot)
        {
            _peer = peer;
            _localSlot = localSlot;
        }

        public NetStatus Status { get; private set; } = NetStatus.Running;

        public event Action<DesyncReport> Desynced;

        public void SendInput(int turn, List<GameCommand> commands, int hashTurn, uint stateHash)
        {
            var w = new ByteWriter(64);
            NetMessages.WriteTurnInput(ref w, _localSlot, turn, commands, hashTurn, stateHash);
            _peer.Send(HostPeerId, w.Buffer, w.Position);
        }

        public bool TryGetCommit(int turn, List<GameCommand> into)
        {
            if (!_commits.TryGetValue(turn, out var bundle))
                return false;
            into.AddRange(bundle);
            _commits.Remove(turn);
            return true;
        }

        public void Poll()
        {
            _peer.Poll();

            while (_peer.TryDequeueConnectionEvent(out int peerId, out bool connected))
                if (!connected && peerId == HostPeerId)
                    Status = NetStatus.Disconnected;

            while (_peer.TryReceive(out int from, out byte[] payload, out int length))
                Handle(payload, length);
        }

        void Handle(byte[] payload, int length)
        {
            var r = new ByteReader(payload, length);
            switch ((NetMessageKind)r.ReadByte())
            {
                case NetMessageKind.TurnCommit:
                    NetMessages.ReadTurnCommit(ref r, out int turn, _scratch);
                    _commits[turn] = new List<GameCommand>(_scratch);
                    break;

                case NetMessageKind.DesyncHalt:
                    Status = NetStatus.Desynced;
                    Desynced?.Invoke(NetMessages.ReadDesyncHalt(ref r));
                    break;
            }
        }

        public void Dispose() => _peer.Dispose();
    }
}
