using System;
using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>A fresh connection's claim to be a previously-dropped seat.</summary>
    public readonly struct RejoinAttempt
    {
        public readonly int FromPeerId;
        public readonly byte ClaimedSlot;
        public readonly BuildIdentity Identity;
        public readonly string PlayerName;

        public RejoinAttempt(int fromPeerId, byte claimedSlot, in BuildIdentity identity, string playerName)
        {
            FromPeerId = fromPeerId;
            ClaimedSlot = claimedSlot;
            Identity = identity;
            PlayerName = playerName;
        }
    }

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

        /// <summary>Raised when a fresh connection claims a previously-dropped
        /// seat. No LobbyHost exists once the match has started, so this is
        /// the mid-match equivalent of its JoinRequest handling — the app
        /// layer decides whether to believe the claim (it owns the account/
        /// session identity the relay server will eventually vouch for) and
        /// calls back <see cref="AcceptRejoin"/> or <see cref="RejectRejoin"/>.</summary>
        public event Action<RejoinAttempt> RejoinRequested;

        public bool IsSubstituted(byte slot) => _relay.IsSubstituted(slot);

        /// <summary>Bind a transport peer to the lobby seat it was given.</summary>
        public void AssignSlot(int peerId, byte slot) => _slotByPeer[peerId] = slot;

        public void SendInput(int turn, List<GameCommand> commands, int hashTurn, uint stateHash) =>
            _relay.SubmitInput(_localSlot, turn, commands, hashTurn, stateHash);

        public bool TryGetCommit(int turn, List<GameCommand> into) =>
            _relay.TryGetCommitted(turn, into);

        /// <summary>Which participating slot(s) are blocking the oldest open
        /// turn right now — the app layer's own drop-starvation timer feeds off
        /// this instead of waiting on the transport's own (much slower)
        /// disconnect callback.</summary>
        public bool TryGetOldestBlockedTurn(out int turn, List<byte> blockingSlots) =>
            _relay.TryGetOldestBlockedTurn(out turn, blockingSlots);

        /// <summary>Take over a slot's input ourselves — called once the app
        /// layer's own grace timer decides a seat is really gone, independent
        /// of whether the transport has reported a disconnect yet.</summary>
        public void SubstitutePeer(byte slot) => _relay.SubstituteSlot(slot, true);

        /// <summary>Submit a substituted slot's input (an on-behalf AI's
        /// commands, or nothing) for a turn.</summary>
        public void SubmitSubstituteInput(byte slot, int turn, List<GameCommand> commands) =>
            _relay.SubmitSubstituteInput(slot, turn, commands);

        public void Poll()
        {
            _peer.Poll();

            while (_peer.TryDequeueConnectionEvent(out int peerId, out bool connected))
            {
                if (connected || !_slotByPeer.TryGetValue(peerId, out byte lost))
                    continue;
                _slotByPeer.Remove(peerId);
                // Speak for the vanished seat immediately. Until somebody does,
                // no turn can complete and the match is frozen for everyone.
                _relay.SubstituteSlot(lost, true);
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

            if (kind == NetMessageKind.RejoinRequest)
            {
                NetMessages.ReadRejoinRequest(ref r, out var identity, out byte claimedSlot, out string name);
                RejoinRequested?.Invoke(new RejoinAttempt(fromPeerId, claimedSlot, identity, name));
                return;
            }

            if (kind != NetMessageKind.TurnInput)
                return;

            NetMessages.ReadTurnInput(ref r, out byte claimedSlot2, out int turn,
                _scratch, out int hashTurn, out uint hash);

            // The peer's seat comes from the lobby assignment, never from the
            // packet: a client does not get to decide which slot it is.
            if (!_slotByPeer.TryGetValue(fromPeerId, out byte slot))
            {
                _log?.Invoke($"[net] input from unassigned peer {fromPeerId}, ignored");
                return;
            }
            if (claimedSlot2 != slot)
                _log?.Invoke($"[net] peer {fromPeerId} claimed slot {claimedSlot2}, is {slot}");

            _relay.SubmitInput(slot, turn, _scratch, hashTurn, hash);
        }

        /// <summary>
        /// Believe a rejoin claim: re-bind the (new) transport peer to the
        /// seat, hand its input back from the AI, and send the driver-state
        /// header followed by the snapshot in chunks.
        /// </summary>
        public void AcceptRejoin(int peerId, byte slot, int resumeTurn, byte ticksPerTurn,
            byte inputDelayTurns, bool[] pausingSlots, byte[] rawSnapshot)
        {
            AssignSlot(peerId, slot);
            _relay.SubstituteSlot(slot, false);

            byte[] compressed = SnapshotTransfer.Compress(rawSnapshot);
            var chunks = SnapshotTransfer.PlanChunks(compressed.Length);

            var header = new ByteWriter(64);
            NetMessages.WriteRejoinAccept(ref header, slot, resumeTurn, ticksPerTurn, inputDelayTurns,
                pausingSlots, chunks.Count, rawSnapshot.Length);
            _peer.Send(peerId, header.Buffer, header.Position);

            for (int i = 0; i < chunks.Count; i++)
            {
                var (offset, count) = chunks[i];
                var w = new ByteWriter(count + 16);
                NetMessages.WriteSnapshotChunk(ref w, i, compressed, offset, count);
                _peer.Send(peerId, w.Buffer, w.Position);
            }

            // Input delay means a turn's commit can run up to (delay - 1)
            // turns AHEAD of when the sim actually executes it (the host's
            // own contribution for turn T is published while still EXECUTING
            // turn T - delay + 1). So by the time a rejoin is accepted, turns
            // between the snapshot's own tick and the relay's true
            // HighestCommittedTurn may already be decided AND already
            // broadcast — broadcast is fire-and-forget to whoever was
            // connected at the time, so a peer that connects after the fact
            // never received it and never will via the normal path. Unicast
            // whatever is already committed in that gap; nothing beyond it
            // needs backfilling — those turns have not frozen yet, so the new
            // peer receives them the normal way once they do.
            for (int t = resumeTurn; t <= _relay.HighestCommittedTurn; t++)
            {
                _scratch.Clear();
                if (!_relay.TryGetCommitted(t, _scratch))
                    continue;
                var w = new ByteWriter(64);
                NetMessages.WriteTurnCommit(ref w, t, _scratch);
                _peer.Send(peerId, w.Buffer, w.Position);
            }
        }

        public void RejectRejoin(int peerId, JoinRejectReason reason)
        {
            var w = new ByteWriter(16);
            NetMessages.WriteRejoinReject(ref w, reason);
            _peer.Send(peerId, w.Buffer, w.Position);
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

        /// <summary>Pre-seed a turn's commit — for a rejoined client, whatever
        /// <see cref="ReconnectClient"/> captured while it was still handling
        /// the handshake on this same connection (turns already decided
        /// between the snapshot's tick and the moment the rejoin was
        /// accepted, which a plain TurnCommit broadcast can never reach:
        /// broadcast is fire-and-forget to whoever was connected at the
        /// time).</summary>
        public void SeedCommit(int turn, List<GameCommand> commands) =>
            _commits[turn] = new List<GameCommand>(commands);

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
