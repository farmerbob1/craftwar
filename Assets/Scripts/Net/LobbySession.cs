using System;
using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// Host side of the pre-game negotiation: accept joiners whose build matches,
    /// seat them, and mirror the resulting lobby to everyone.
    ///
    /// The identity check happens HERE, before a seat is granted, because it is
    /// the last moment a mismatch is cheap. Let one through and the failure
    /// surfaces hundreds of turns later as a desync, with nothing pointing at the
    /// stat table or map file that actually differed.
    /// </summary>
    public sealed class LobbyHost : IDisposable
    {
        readonly IPacketPeer _peer;
        readonly BuildIdentity _identity;
        readonly Action<string> _log;

        public LobbyHost(IPacketPeer peer, in BuildIdentity identity, LobbyPayload payload,
            Action<string> log = null)
        {
            _peer = peer;
            _identity = identity;
            Payload = payload;
            _log = log;
        }

        public LobbyPayload Payload { get; }

        /// <summary>Transport peer -> the seat it was given.</summary>
        public readonly Dictionary<int, byte> SlotByPeer = new Dictionary<int, byte>();

        /// <summary>Raised whenever the roster changes, so the UI can rebuild.</summary>
        public event Action Changed;

        public void Poll()
        {
            _peer.Poll();

            while (_peer.TryDequeueConnectionEvent(out int peerId, out bool connected))
            {
                if (connected || !SlotByPeer.TryGetValue(peerId, out byte seat))
                    continue;
                // Someone left before the match started: the seat goes back
                // to waiting, not to Computer — AI presence stays the host's
                // choice, never a side effect of a joiner leaving.
                SlotByPeer.Remove(peerId);
                Payload.Slots[seat].Name = "";
                Payload.Slots[seat].SeatStatus = (byte)LobbySeatStatus.Open;
                BroadcastState();
                Changed?.Invoke();
            }

            while (_peer.TryReceive(out int from, out byte[] data, out int length))
                Handle(from, data, length);
        }

        void Handle(int peerId, byte[] data, int length)
        {
            var r = new ByteReader(data, length);
            var kind = (NetMessageKind)r.ReadByte();

            if (kind == NetMessageKind.IdentityConfirm)
            {
                ConfirmIdentity(peerId, BuildIdentity.Read(ref r));
                return;
            }
            if (kind != NetMessageKind.JoinRequest)
                return;

            NetMessages.ReadJoinRequest(ref r, out var theirs, out string name);

            // Only the version fields can be judged yet: the joiner does not know
            // which map this game is playing, so it cannot have hashed it. The
            // map and rules are checked on IdentityConfirm, once the roster it
            // needs has been sent.
            var mismatch = _identity.CompareVersionsTo(theirs);
            if (mismatch != JoinRejectReason.None)
            {
                Reject(peerId, mismatch, name);
                return;
            }

            if (SlotByPeer.ContainsKey(peerId))
                return; // already seated; ignore a repeated request

            int seat = Payload.FirstOpenSeat();
            if (seat < 0)
            {
                Reject(peerId, JoinRejectReason.GameFull, name);
                return;
            }

            SlotByPeer[peerId] = (byte)seat;
            Payload.Slots[seat].SeatStatus = (byte)LobbySeatStatus.Human;
            Payload.Slots[seat].Name = string.IsNullOrWhiteSpace(name) ? $"Player {seat + 1}" : name;

            var w = new ByteWriter(256);
            NetMessages.WriteJoinAccept(ref w, (byte)seat, Payload);
            _peer.Send(peerId, w.Buffer, w.Position);

            BroadcastState();
            Changed?.Invoke();
            _log?.Invoke($"[lobby] {Payload.Slots[seat].Name} joined as seat {seat + 1}");
        }

        /// <summary>
        /// Second half of the handshake: the client has now hashed its own copy
        /// of the host's map and rules. A mismatch here costs it the seat — this
        /// is the last cheap moment to catch it, and letting it through would
        /// surface as a desync deep into the match instead.
        /// </summary>
        void ConfirmIdentity(int peerId, in BuildIdentity theirs)
        {
            if (!SlotByPeer.TryGetValue(peerId, out byte seat))
                return;

            var mismatch = _identity.CompareTo(theirs);
            if (mismatch == JoinRejectReason.None)
                return;

            Reject(peerId, mismatch, Payload.Slots[seat].Name);
            SlotByPeer.Remove(peerId);
            Payload.Slots[seat].Name = "";
            Payload.Slots[seat].SeatStatus = (byte)LobbySeatStatus.Open;
            BroadcastState();
            Changed?.Invoke();
        }

        /// <summary>
        /// Host-only seat control: cycle Closed/Open/Computer, or set it
        /// directly. Refuses to override an occupied Human seat — that
        /// changes only when the person leaves.
        /// </summary>
        public bool SetSeatStatus(int seat, LobbySeatStatus status)
        {
            if (seat < 0 || seat >= Payload.Slots.Length) return false;
            if (Payload.Slots[seat].SeatStatus == (byte)LobbySeatStatus.Human) return false;
            if (status == LobbySeatStatus.Human) return false; // only a real join does this
            Payload.Slots[seat].SeatStatus = (byte)status;
            BroadcastState();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Host-only: regroup a seat's alliance. Up to 8 teams for 8
        /// players — FFA (one team per seat) is just the default.</summary>
        public void SetSeatTeam(int seat, byte team)
        {
            if (seat < 0 || seat >= Payload.Slots.Length) return;
            Payload.Slots[seat].Team = team;
            BroadcastState();
            Changed?.Invoke();
        }

        /// <summary>False while any playable seat is still Open — AI presence
        /// must be a deliberate host choice, never a default at start time.</summary>
        public bool CanStart() => !Payload.HasOpenSeats();

        void Reject(int peerId, JoinRejectReason reason, string name)
        {
            var w = new ByteWriter(16);
            NetMessages.WriteJoinReject(ref w, reason);
            _peer.Send(peerId, w.Buffer, w.Position);
            _log?.Invoke($"[lobby] refused '{name}': {reason}");
        }

        /// <summary>Push the current roster to every client.</summary>
        public void BroadcastState()
        {
            var w = new ByteWriter(256);
            NetMessages.WriteLobbyState(ref w, Payload);
            _peer.Send(IPacketPeer.Broadcast, w.Buffer, w.Position);
        }

        /// <summary>Tell everyone to load the match. The payload travels with it,
        /// so no client is relying on a state message it might have missed.
        /// Refuses while any seat is still Open — see <see cref="CanStart"/>.</summary>
        public bool StartMatch()
        {
            if (!CanStart())
                return false;
            var w = new ByteWriter(256);
            NetMessages.WriteStartMatch(ref w, Payload);
            _peer.Send(IPacketPeer.Broadcast, w.Buffer, w.Position);
            return true;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Client side: present what we can prove up front, learn our seat and the
    /// host's map, then confirm we have the same copy of it.
    /// </summary>
    public sealed class LobbyClient : IDisposable
    {
        readonly IPacketPeer _peer;
        readonly BuildIdentity _identity;
        readonly string _playerName;
        bool _requestSent;

        public LobbyClient(IPacketPeer peer, in BuildIdentity identity, string playerName)
        {
            _peer = peer;
            _identity = identity;
            _playerName = playerName;
        }

        public LobbyPayload Payload { get; private set; }
        public byte MySlot { get; private set; }
        public bool Seated { get; private set; }
        public JoinRejectReason Rejection { get; private set; }
        public bool Disconnected { get; private set; }

        public event Action Changed;

        /// <summary>Raised when the host starts: payload plus our own seat.</summary>
        public event Action<LobbyPayload, byte> Started;

        /// <summary>
        /// Send our map/rules fingerprint now that we know which map is being
        /// played. Idempotent — the app calls it once the roster arrives.
        /// </summary>
        public void ConfirmIdentity(in BuildIdentity identity)
        {
            if (_confirmed)
                return;
            _confirmed = true;
            var w = new ByteWriter(64);
            NetMessages.WriteIdentityConfirm(ref w, identity);
            _peer.Send(ClientTurnExchange.HostPeerId, w.Buffer, w.Position);
        }

        bool _confirmed;

        public void Poll()
        {
            _peer.Poll();

            while (_peer.TryDequeueConnectionEvent(out int peerId, out bool connected))
            {
                if (connected && !_requestSent)
                {
                    // The connection is only usable once UTP reports Connect, so
                    // the join request waits for it rather than racing it.
                    SendJoinRequest();
                    _requestSent = true;
                }
                else if (!connected)
                {
                    Disconnected = true;
                    Changed?.Invoke();
                }
            }

            while (_peer.TryReceive(out int from, out byte[] data, out int length))
                Handle(data, length);
        }

        void SendJoinRequest()
        {
            var w = new ByteWriter(128);
            NetMessages.WriteJoinRequest(ref w, _identity, _playerName);
            _peer.Send(ClientTurnExchange.HostPeerId, w.Buffer, w.Position);
        }

        void Handle(byte[] data, int length)
        {
            var r = new ByteReader(data, length);
            switch ((NetMessageKind)r.ReadByte())
            {
                case NetMessageKind.JoinAccept:
                    NetMessages.ReadJoinAccept(ref r, out byte seat, out var accepted);
                    MySlot = seat;
                    Payload = accepted;
                    Seated = true;
                    Changed?.Invoke();
                    break;

                case NetMessageKind.JoinReject:
                    Rejection = (JoinRejectReason)r.ReadByte();
                    // A rejection can also arrive AFTER being seated, when the
                    // map/rules confirmation fails.
                    Seated = false;
                    Changed?.Invoke();
                    break;

                case NetMessageKind.LobbyState:
                    Payload = LobbyPayload.Read(ref r);
                    Changed?.Invoke();
                    break;

                case NetMessageKind.StartMatch:
                    Payload = LobbyPayload.Read(ref r);
                    Started?.Invoke(Payload, MySlot);
                    break;
            }
        }

        public void Dispose() { }
    }
}
