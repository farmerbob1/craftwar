using System;

namespace Craftwar.Net
{
    /// <summary>
    /// The seam between the protocol and whatever moves bytes. Everything above
    /// this line is pure C# and runs in the headless harness; the Unity Transport
    /// implementation below it lives in a separate assembly so it can never leak
    /// an engine dependency back into the deterministic half.
    ///
    /// Deliberately tiny — the protocol needs "send these bytes to that peer" and
    /// "what arrived", nothing more. Reliability and ordering are the transport's
    /// job: lockstep cannot proceed past a missing turn, so every message here is
    /// reliable and in-order by construction.
    /// </summary>
    public interface IPacketPeer : IDisposable
    {
        /// <summary>Stable id for a connected peer. 0 is always the host.</summary>
        int LocalPeerId { get; }

        bool IsHost { get; }

        /// <summary>Send to a specific peer, or to everyone when
        /// <paramref name="peerId"/> is <see cref="Broadcast"/>.</summary>
        void Send(int peerId, byte[] payload, int length);

        /// <summary>
        /// Take the next received packet, or false when the queue is empty.
        /// The buffer is owned by the peer and is only valid until the next call.
        /// </summary>
        bool TryReceive(out int fromPeerId, out byte[] payload, out int length);

        /// <summary>Service the socket. Called once per frame.</summary>
        void Poll();

        /// <summary>Peers that have connected or dropped since the last call.</summary>
        bool TryDequeueConnectionEvent(out int peerId, out bool connected);

        const int Broadcast = -1;
    }
}
