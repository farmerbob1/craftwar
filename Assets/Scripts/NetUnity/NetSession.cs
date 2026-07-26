using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net.Unity
{
    /// <summary>
    /// Carries a live connection from the menu scene into the game scene.
    ///
    /// Only <c>MatchSession</c> and the music director survive a scene load, so a
    /// lobby that has already negotiated seats and opened a socket needs a
    /// parallel handoff — reconnecting after the load would drop the very
    /// agreement the lobby exists to reach. Deliberately shaped like
    /// <c>MatchSession</c>: a static the next scene consumes.
    /// </summary>
    public static class NetSession
    {
        public static UtpPeerSocket Socket;
        public static bool IsHost;
        public static byte LocalSlot;

        /// <summary>Seats whose input every turn waits for.</summary>
        public static byte[] ParticipatingSlots = System.Array.Empty<byte>();

        /// <summary>Host only: which lobby seat each transport peer was given.
        /// The seat is assigned here, never taken from the peer's own packets.</summary>
        public static readonly Dictionary<int, byte> SlotByPeerId = new Dictionary<int, byte>();

        public static int TicksPerTurn = SimConstants.TicksPerCommandTurn;

        /// <summary>Turns of input delay. 2 turns = 8 ticks = 160 ms at 50 Hz —
        /// enough budget for a LAN round trip without a noticeable lag on orders.</summary>
        public static int InputDelayTurns = 2;

        /// <summary>Host-owned game speed, so peers cannot feed the turn clock at
        /// different rates. A client at 0.5x would starve everyone; one at 3x
        /// would live permanently in the starvation clamp.</summary>
        public static float SpeedMultiplier = 1f;

        public static bool Active => Socket != null;

        /// <summary>Build the driver for this match, or null for single player.</summary>
        public static INetLockstepDriver CreateDriver(
            out HostTurnExchange host, out ClientTurnExchange client)
        {
            host = null;
            client = null;
            if (!Active)
                return null;

            ITurnExchange exchange;
            if (IsHost)
            {
                var relay = new TurnRelay(ParticipatingSlots);
                host = new HostTurnExchange(Socket, relay, LocalSlot,
                    m => UnityEngine.Debug.LogWarning(m));
                foreach (var pair in SlotByPeerId)
                    host.AssignSlot(pair.Key, pair.Value);
                exchange = host;
            }
            else
            {
                client = new ClientTurnExchange(Socket, LocalSlot);
                exchange = client;
            }

            return new TurnLockstepDriver(TicksPerTurn, InputDelayTurns, LocalSlot, exchange);
        }

        public static void Clear()
        {
            Socket?.Dispose();
            Socket = null;
            IsHost = false;
            LocalSlot = 0;
            ParticipatingSlots = System.Array.Empty<byte>();
            SlotByPeerId.Clear();
            SpeedMultiplier = 1f;
        }
    }
}
