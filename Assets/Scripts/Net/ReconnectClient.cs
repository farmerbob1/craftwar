using System;
using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// Client side of the mid-match rejoin handshake: claim a seat, receive
    /// the driver-state header and the snapshot in chunks, and hand back
    /// everything <see cref="TurnLockstepDriver"/>'s resume-at-a-turn
    /// constructor and <see cref="Sim.SimSerializer.Load"/> need.
    ///
    /// Deliberately NOT <see cref="LobbyClient"/>: that class only exists in
    /// the menu scene and is torn down once the match starts, so a mid-match
    /// rejoin needs its own tiny, self-contained negotiation over a fresh
    /// <see cref="IPacketPeer"/> connection to the host.
    /// </summary>
    public sealed class ReconnectClient : IDisposable
    {
        readonly IPacketPeer _peer;
        readonly BuildIdentity _identity;
        readonly byte _claimedSlot;
        readonly string _playerName;
        bool _requestSent;

        SnapshotTransfer.Reassembler _reassembler;
        byte _yourSlot;
        int _resumeTurn;
        byte _ticksPerTurn, _inputDelayTurns;
        bool[] _pausingSlots;
        int _rawSnapshotLength;

        /// <summary>Turns already decided between the snapshot's own tick and
        /// the moment the rejoin was accepted — input delay lets a commit run
        /// ahead of when the sim that produced it has actually executed, so
        /// these exist and were never going to arrive via the normal
        /// broadcast (fire-and-forget to whoever was already connected). The
        /// caller must seed them into the new ClientTurnExchange once
        /// constructed, via <see cref="ClientTurnExchange.SeedCommit"/>.</summary>
        public IReadOnlyDictionary<int, List<GameCommand>> BackfilledCommits => _backfilledCommits;
        readonly Dictionary<int, List<GameCommand>> _backfilledCommits = new Dictionary<int, List<GameCommand>>();
        readonly List<GameCommand> _scratch = new List<GameCommand>();

        public ReconnectClient(IPacketPeer peer, in BuildIdentity identity, byte claimedSlot, string playerName)
        {
            _peer = peer;
            _identity = identity;
            _claimedSlot = claimedSlot;
            _playerName = playerName;
        }

        public bool Rejected { get; private set; }
        public JoinRejectReason Rejection { get; private set; }
        public bool Disconnected { get; private set; }

        /// <summary>True once every chunk has arrived and <see cref="TryComplete"/>
        /// can be called.</summary>
        public bool Ready => _reassembler != null && _reassembler.Complete;

        public void Poll()
        {
            _peer.Poll();

            while (_peer.TryDequeueConnectionEvent(out int peerId, out bool connected))
            {
                if (connected && !_requestSent)
                {
                    SendRejoinRequest();
                    _requestSent = true;
                }
                else if (!connected)
                {
                    Disconnected = true;
                }
            }

            while (_peer.TryReceive(out int from, out byte[] data, out int length))
                Handle(data, length);
        }

        void SendRejoinRequest()
        {
            var w = new ByteWriter(128);
            NetMessages.WriteRejoinRequest(ref w, _identity, _claimedSlot, _playerName);
            _peer.Send(ClientTurnExchange.HostPeerId, w.Buffer, w.Position);
        }

        void Handle(byte[] data, int length)
        {
            var r = new ByteReader(data, length);
            switch ((NetMessageKind)r.ReadByte())
            {
                case NetMessageKind.RejoinReject:
                    Rejected = true;
                    Rejection = (JoinRejectReason)r.ReadByte();
                    break;

                case NetMessageKind.RejoinAccept:
                    NetMessages.ReadRejoinAccept(ref r, out _yourSlot, out _resumeTurn,
                        out _ticksPerTurn, out _inputDelayTurns, out _pausingSlots,
                        out int chunkCount, out _rawSnapshotLength);
                    _reassembler = new SnapshotTransfer.Reassembler(chunkCount);
                    break;

                case NetMessageKind.SnapshotChunk:
                    if (_reassembler == null)
                        break; // header not seen yet; drop (should not happen, host sends it first)
                    NetMessages.ReadSnapshotChunk(ref r, out int index, out byte[] chunkData);
                    _reassembler.Add(index, chunkData);
                    break;

                case NetMessageKind.TurnCommit:
                    NetMessages.ReadTurnCommit(ref r, out int turn, _scratch);
                    _backfilledCommits[turn] = new List<GameCommand>(_scratch);
                    break;
            }
        }

        /// <summary>Decompresses the reassembled snapshot and hands back
        /// everything needed to rebuild the sim and driver. Only valid once
        /// <see cref="Ready"/> is true.</summary>
        public bool TryComplete(out byte[] rawSnapshot, out byte yourSlot, out int resumeTurn,
            out byte ticksPerTurn, out byte inputDelayTurns, out bool[] pausingSlots)
        {
            rawSnapshot = null;
            yourSlot = 0;
            resumeTurn = 0;
            ticksPerTurn = 0;
            inputDelayTurns = 0;
            pausingSlots = null;
            if (!Ready)
                return false;

            byte[] compressed = _reassembler.Build();
            rawSnapshot = SnapshotTransfer.Decompress(compressed, _rawSnapshotLength);
            yourSlot = _yourSlot;
            resumeTurn = _resumeTurn;
            ticksPerTurn = _ticksPerTurn;
            inputDelayTurns = _inputDelayTurns;
            pausingSlots = _pausingSlots;
            return true;
        }

        public void Dispose() { }
    }
}
