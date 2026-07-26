using System;
using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// The host's arbiter. Collects every participant's input for a turn and,
    /// once the set is complete, freezes it into the one bundle every peer will
    /// execute — including the host itself, so the host is a player rather than a
    /// privileged observer and its own hash is a real comparison point.
    ///
    /// Pure logic, no transport: the loopback tests and the UDP host drive the
    /// same class, so what LAN play runs is what the headless suite proved.
    ///
    /// Ordering is the whole game here. <see cref="LocalLockstepDriver.SortCanonically"/>
    /// is a stable sort on player alone, so it only yields one canonical bundle
    /// if each player's commands form a single contiguous run. Inputs are
    /// therefore emitted in ascending slot order, one block per slot.
    /// </summary>
    public sealed class TurnRelay
    {
        /// <summary>A turn's inputs, one slot at a time.</summary>
        sealed class PendingTurn
        {
            public readonly List<GameCommand>[] BySlot = new List<GameCommand>[SimConstants.MaxPlayers];
            public readonly uint[] HashBySlot = new uint[SimConstants.MaxPlayers];
            public readonly bool[] HashPresent = new bool[SimConstants.MaxPlayers];
            public readonly int[] HashTurnBySlot = new int[SimConstants.MaxPlayers];
        }

        readonly Dictionary<int, PendingTurn> _pending = new Dictionary<int, PendingTurn>();
        readonly Dictionary<int, List<GameCommand>> _committed = new Dictionary<int, List<GameCommand>>();
        readonly bool[] _participating = new bool[SimConstants.MaxPlayers];

        /// <summary>Turns whose bundle is frozen but which some peer may not have
        /// fetched yet. Kept so a slow peer can still catch up.</summary>
        int _lowestRetainedTurn;

        public TurnRelay(IEnumerable<byte> participatingSlots)
        {
            foreach (byte slot in participatingSlots)
                if (slot < SimConstants.MaxPlayers)
                    _participating[slot] = true;
        }

        /// <summary>Slots whose input a turn waits for. A slot handed to an AI on
        /// drop stays participating — the host simply produces its input.</summary>
        public bool IsParticipating(byte slot) => slot < SimConstants.MaxPlayers && _participating[slot];

        public void SetParticipating(byte slot, bool participating)
        {
            if (slot < SimConstants.MaxPlayers)
                _participating[slot] = participating;
        }

        /// <summary>Highest turn frozen so far, or -1.</summary>
        public int HighestCommittedTurn { get; private set; } = -1;

        /// <summary>Fired when a slot's reported hash disagrees with one already
        /// recorded for the same turn.</summary>
        public event Action<DesyncReport> Desynced;

        public void SubmitInput(byte slot, int turn, List<GameCommand> commands, int hashTurn, uint stateHash)
        {
            if (slot >= SimConstants.MaxPlayers || !_participating[slot])
                return;
            if (turn < _lowestRetainedTurn)
                return; // late duplicate for a turn already dispatched

            if (!_pending.TryGetValue(turn, out var pt))
            {
                pt = new PendingTurn();
                _pending[turn] = pt;
            }
            if (pt.BySlot[slot] != null)
                return; // one input per slot per turn; ignore duplicates

            var copy = new List<GameCommand>(commands.Count);
            for (int i = 0; i < commands.Count; i++)
            {
                var c = commands[i];
                // A peer may only speak for itself. Cheap, and it turns a hostile
                // or buggy client into an ignored command instead of a desync.
                if (c.Player != slot)
                    continue;
                copy.Add(c);
            }
            pt.BySlot[slot] = copy;

            if (hashTurn >= 0)
            {
                pt.HashBySlot[slot] = stateHash;
                pt.HashPresent[slot] = true;
                pt.HashTurnBySlot[slot] = hashTurn;
                CompareHashes(pt, slot, hashTurn, stateHash);
            }

            TryFreeze(turn);
        }

        void CompareHashes(PendingTurn pt, byte slot, int hashTurn, uint hash)
        {
            for (byte other = 0; other < SimConstants.MaxPlayers; other++)
            {
                if (other == slot || !pt.HashPresent[other])
                    continue;
                // Only comparable when both peers hashed the same point in time.
                if (pt.HashTurnBySlot[other] != hashTurn)
                    continue;
                if (pt.HashBySlot[other] != hash)
                {
                    Desynced?.Invoke(new DesyncReport(hashTurn, slot, pt.HashBySlot[other], hash));
                    return;
                }
            }
        }

        void TryFreeze(int turn)
        {
            if (_committed.ContainsKey(turn))
                return;
            var pt = _pending[turn];
            for (byte slot = 0; slot < SimConstants.MaxPlayers; slot++)
                if (_participating[slot] && pt.BySlot[slot] == null)
                    return; // still waiting on somebody

            // Ascending slot order: one contiguous block per player, which is
            // exactly what the canonical sort needs to be well defined.
            var bundle = new List<GameCommand>();
            for (byte slot = 0; slot < SimConstants.MaxPlayers; slot++)
                if (pt.BySlot[slot] != null)
                    bundle.AddRange(pt.BySlot[slot]);

            LocalLockstepDriver.SortCanonically(bundle);
            _committed[turn] = bundle;
            _pending.Remove(turn);
            if (turn > HighestCommittedTurn)
                HighestCommittedTurn = turn;
        }

        public bool TryGetCommitted(int turn, List<GameCommand> into)
        {
            if (!_committed.TryGetValue(turn, out var bundle))
                return false;
            into.AddRange(bundle);
            return true;
        }

        /// <summary>Drop bundles nobody can still need.</summary>
        public void ReleaseThrough(int turn)
        {
            while (_lowestRetainedTurn <= turn)
            {
                _committed.Remove(_lowestRetainedTurn);
                _lowestRetainedTurn++;
            }
        }
    }
}
