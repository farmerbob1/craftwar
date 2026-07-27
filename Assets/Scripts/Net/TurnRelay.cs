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

        /// <summary>Slots the host now speaks for, because the player behind them
        /// went away.</summary>
        readonly bool[] _substituted = new bool[SimConstants.MaxPlayers];

        /// <summary>
        /// Take over a slot whose peer has dropped. A lockstep turn cannot
        /// complete without every participant's input, so a vanished peer freezes
        /// the match for everyone until somebody speaks for it. The slot stays
        /// participating — its units are still on the map, still ownable, still a
        /// target — the host simply supplies its input from now on.
        ///
        /// Turns already waiting on it are completed immediately, which is what
        /// actually un-sticks the match.
        /// </summary>
        public void SubstituteSlot(byte slot, bool substituted)
        {
            if (slot >= SimConstants.MaxPlayers)
                return;
            _substituted[slot] = substituted;
            if (!substituted)
                return;

            // Release everything that was blocked on this slot alone.
            var blocked = new List<int>();
            foreach (var pair in _pending)
                blocked.Add(pair.Key);
            blocked.Sort(); // deterministic order, and Dictionary iteration is not
            for (int i = 0; i < blocked.Count; i++)
                TryFreeze(blocked[i]);
        }

        public bool IsSubstituted(byte slot) =>
            slot < SimConstants.MaxPlayers && _substituted[slot];

        /// <summary>Input the host produces on a substituted slot's behalf — the
        /// AI's orders, or nothing at all.</summary>
        public void SubmitSubstituteInput(byte slot, int turn, List<GameCommand> commands)
        {
            if (!IsSubstituted(slot))
                return;
            SubmitInput(slot, turn, commands, -1, 0u);
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
            if (!_pending.TryGetValue(turn, out var pt))
                return;
            for (byte slot = 0; slot < SimConstants.MaxPlayers; slot++)
            {
                if (!_participating[slot] || pt.BySlot[slot] != null)
                    continue;
                if (_substituted[slot])
                {
                    // Nobody is going to send for this slot; treat silence as an
                    // empty turn rather than waiting forever.
                    pt.BySlot[slot] = new List<GameCommand>();
                    continue;
                }
                return; // still waiting on somebody who might yet answer
            }

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

        /// <summary>
        /// Which participating, not-yet-substituted slot(s) are the reason the
        /// oldest still-open turn cannot freeze. Empty (returns false) when
        /// nothing is pending, or everything pending is only waiting on slots
        /// already substituted (which TryFreeze resolves as empty on its own —
        /// not a stall).
        ///
        /// Exists so the app layer can run its OWN drop-detection timer instead
        /// of waiting on the transport's own disconnect callback, which can take
        /// far longer (UTP's is ~30s) than a turn actually needs to be considered
        /// stuck.
        /// </summary>
        public bool TryGetOldestBlockedTurn(out int turn, List<byte> blockingSlots)
        {
            blockingSlots.Clear();
            turn = -1;
            int lowest = int.MaxValue;
            foreach (int candidate in _pending.Keys)
                if (candidate < lowest)
                    lowest = candidate;
            if (lowest == int.MaxValue)
                return false;

            var pt = _pending[lowest];
            for (byte slot = 0; slot < SimConstants.MaxPlayers; slot++)
                if (_participating[slot] && pt.BySlot[slot] == null && !_substituted[slot])
                    blockingSlots.Add(slot);

            if (blockingSlots.Count == 0)
                return false;
            turn = lowest;
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
