using System;
using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    public enum NetStatus : byte
    {
        /// <summary>No network involved (single player, replay playback).</summary>
        Local = 0,
        Connecting,
        /// <summary>Turns are flowing.</summary>
        Running,
        /// <summary>Blocked waiting on at least one peer's input.</summary>
        Waiting,
        /// <summary>A peer's state hash disagreed. The match is over as a contest —
        /// the two sides are already simulating different games.</summary>
        Desynced,
        Disconnected,
    }

    /// <summary>Everything the game loop and the HUD need from a networked driver
    /// that the local-play seam has no concept of.</summary>
    public interface INetLockstepDriver : ILockstepDriver, IDisposable
    {
        /// <summary>Pump the transport. Must be called every frame — including
        /// while the game is paused or showing the victory screen, or this peer
        /// stops acknowledging turns and stalls everyone else.</summary>
        void Poll();

        /// <summary>Which slot this machine drives.</summary>
        byte LocalSlot { get; }

        NetStatus Status { get; }

        /// <summary>Highest turn whose commands are agreed and executable.</summary>
        int ConfirmedTurn { get; }

        /// <summary>Turn the sim is currently executing.</summary>
        int CurrentTurn { get; }

        /// <summary>True while any player holds a pause. Distinct from starving:
        /// the turn clock is still running, the sim clock is not.</summary>
        bool IsPaused { get; }

        /// <summary>
        /// Hand the driver this peer's state hash for a turn it is about to
        /// execute, so it can be compared against everyone else's. Detection
        /// necessarily lags by the input delay, so peers also keep a short ring
        /// of past hashes: without it a halt reports that we diverged but not
        /// when, which is the only fact that makes it debuggable.
        /// </summary>
        void RecordTurnHash(int turn, uint hash);

        /// <summary>Raised once, on every peer, when hashes disagree.</summary>
        event Action<DesyncReport> Desynced;

        /// <summary>Raised when a peer stops feeding the turn schedule.</summary>
        event Action<byte> PeerDropped;
    }

    public readonly struct DesyncReport
    {
        public readonly int Turn;
        public readonly byte Slot;
        public readonly uint ExpectedHash;
        public readonly uint ActualHash;

        public DesyncReport(int turn, byte slot, uint expected, uint actual)
        {
            Turn = turn;
            Slot = slot;
            ExpectedHash = expected;
            ActualHash = actual;
        }

        public override string ToString() =>
            $"desync at turn {Turn}: slot {Slot} hashed {ActualHash:X8}, host had {ExpectedHash:X8}";
    }

    /// <summary>
    /// How a driver gets a turn's commands agreed. Separating this from the
    /// scheduling above it means the turn/tick/delay arithmetic is written once
    /// and tested in-process, with the socket swapped in underneath.
    /// </summary>
    public interface ITurnExchange
    {
        /// <summary>Publish this peer's commands for a turn. Called exactly once
        /// per turn, in ascending order, and never split — the canonical sort is
        /// stable on player only, so a player's commands must arrive as one
        /// contiguous run or the bundle order stops being deterministic.</summary>
        /// <param name="hashTurn">Which turn <paramref name="stateHash"/> describes,
        /// or -1 if none yet. Hashes are only comparable between peers that took
        /// them at the same point in time, so the turn travels with the value.</param>
        void SendInput(int turn, List<GameCommand> commands, int hashTurn, uint stateHash);

        /// <summary>True once every participant's input for this turn is in, with
        /// the agreed bundle written into <paramref name="into"/>.</summary>
        bool TryGetCommit(int turn, List<GameCommand> into);

        void Poll();

        NetStatus Status { get; }
    }
}
