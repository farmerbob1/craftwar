using System;
using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// Lockstep scheduling: buckets commands into fixed-length turns, holds them
    /// for a fixed input delay, and refuses to let the sim advance into a turn
    /// nobody has agreed on yet.
    ///
    /// TIMING. A turn is <c>ticksPerTurn</c> sim ticks (4 at 50 Hz = 80 ms). A
    /// turn's whole bundle executes on its FIRST tick; the rest of the turn runs
    /// empty. <c>inputDelayTurns</c> L is the delay a player actually feels: a
    /// command issued while turn X is executing runs at turn X+L. The buffer
    /// filled during turn X is closed and published at the start of turn X+1, as
    /// the input for turn X+L; turns 0..L-2 are bootstrapped empty so the first
    /// turns have something to commit.
    ///
    /// L = 1 with ticksPerTurn = 1 reproduces <see cref="LocalLockstepDriver"/>
    /// exactly — a command submitted during tick T executes at tick T+1, and one
    /// submitted before the first tick executes at tick 0. That equivalence is
    /// the point: the whole determinism suite can be run through this driver, so
    /// turn scheduling is proven against the same expectations single-player
    /// already meets, before a socket is anywhere near it.
    /// </summary>
    public sealed class TurnLockstepDriver : INetLockstepDriver
    {
        readonly int _ticksPerTurn;
        readonly int _inputDelayTurns;
        readonly ITurnExchange _exchange;
        readonly List<GameCommand> _localBuffer = new List<GameCommand>();

        /// <summary>Recent (turn, hash) pairs. A desync is only detectable after
        /// the delay has elapsed, so by the time it is reported the peers have
        /// already executed past it — this is what says how far past.</summary>
        readonly (int turn, uint hash)[] _hashRing;
        int _hashRingCount;

        int _publishedThroughTurn = -1;
        int _confirmedTurn = -1;
        int _currentTurn;

        public TurnLockstepDriver(int ticksPerTurn, int inputDelayTurns, byte localSlot,
            ITurnExchange exchange, int hashRingSize = 256)
        {
            if (ticksPerTurn < 1)
                throw new ArgumentOutOfRangeException(nameof(ticksPerTurn), "a turn is at least one tick");
            if (inputDelayTurns < 1)
                throw new ArgumentOutOfRangeException(nameof(inputDelayTurns),
                    "a command cannot execute in the turn it was issued: peers must agree first");
            _ticksPerTurn = ticksPerTurn;
            _inputDelayTurns = inputDelayTurns;
            LocalSlot = localSlot;
            _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
            _hashRing = new (int, uint)[hashRingSize < 1 ? 1 : hashRingSize];

            // Bootstrap the turns whose input can never be produced by a running
            // turn, because no turn has run yet.
            for (int t = 0; t <= _inputDelayTurns - 2; t++)
            {
                _exchange.SendInput(t, _localBuffer, 0, 0u);
                _publishedThroughTurn = t;
            }
        }

        public int TicksPerTurn => _ticksPerTurn;
        public int InputDelayTurns => _inputDelayTurns;
        public byte LocalSlot { get; }
        public NetStatus Status { get; private set; } = NetStatus.Local;
        public int ConfirmedTurn => _confirmedTurn;
        public int CurrentTurn => _currentTurn;

        public event Action<DesyncReport> Desynced;
        public event Action<byte> PeerDropped;

        public void SubmitLocalCommand(in GameCommand cmd) => _localBuffer.Add(cmd);

        public void Poll() => _exchange.Poll();

        /// <summary>The hash this peer had entering a turn, kept so a later halt
        /// can say where the divergence started.</summary>
        public void RecordTurnHash(int turn, uint hash)
        {
            _hashRing[_hashRingCount % _hashRing.Length] = (turn, hash);
            _hashRingCount++;
            _pendingHash = hash;
            _pendingHashTurn = turn;
        }

        uint _pendingHash;
        int _pendingHashTurn = -1;

        /// <summary>Recent (turn, hash) pairs, oldest first — the desync dump.</summary>
        public IEnumerable<(int turn, uint hash)> HashHistory()
        {
            int count = Math.Min(_hashRingCount, _hashRing.Length);
            int start = _hashRingCount - count;
            for (int i = 0; i < count; i++)
                yield return _hashRing[(start + i) % _hashRing.Length];
        }

        public bool TryGetTickCommands(int tick, List<GameCommand> commands)
        {
            commands.Clear();
            int turn = tick / _ticksPerTurn;

            if (tick % _ticksPerTurn != 0)
            {
                // Mid-turn ticks carry nothing, but must not run ahead of the
                // turn they belong to.
                return turn <= _confirmedTurn;
            }

            // Close the buffer filled during the previous turn and publish it as
            // this turn's contribution to a turn `inputDelayTurns` ahead. Doing it
            // here — before asking for the commit — is what keeps every peer's
            // publish order identical and gapless.
            PublishInputFor(turn + _inputDelayTurns - 1);

            _exchange.Poll();
            if (!_exchange.TryGetCommit(turn, commands))
            {
                Status = _exchange.Status == NetStatus.Local ? NetStatus.Local : NetStatus.Waiting;
                return false;
            }

            _confirmedTurn = turn;
            _currentTurn = turn;
            Status = _exchange.Status;
            LocalLockstepDriver.SortCanonically(commands);
            return true;
        }

        void PublishInputFor(int turn)
        {
            // Publish every turn up to `turn` so the sequence never gains a hole,
            // even if a caller skips ticks.
            while (_publishedThroughTurn < turn)
            {
                int next = _publishedThroughTurn + 1;
                if (next == turn)
                {
                    _exchange.SendInput(next, _localBuffer, _pendingHashTurn, _pendingHash);
                    _localBuffer.Clear();
                }
                else
                {
                    _exchange.SendInput(next, EmptyCommands, _pendingHashTurn, _pendingHash);
                }
                _publishedThroughTurn = next;
            }
        }

        static readonly List<GameCommand> EmptyCommands = new List<GameCommand>();

        internal void RaiseDesync(in DesyncReport report)
        {
            Status = NetStatus.Desynced;
            Desynced?.Invoke(report);
        }

        internal void RaisePeerDropped(byte slot) => PeerDropped?.Invoke(slot);

        public void Dispose() => (_exchange as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Single-player exchange: whatever this peer publishes is immediately the
    /// agreed bundle. Lets one code path cover local play, so single-player is
    /// genuinely "lockstep with one participant" rather than a separate
    /// implementation that can drift from the networked one.
    /// </summary>
    public sealed class LocalTurnExchange : ITurnExchange
    {
        readonly Dictionary<int, List<GameCommand>> _turns = new Dictionary<int, List<GameCommand>>();

        public NetStatus Status => NetStatus.Local;

        public void SendInput(int turn, List<GameCommand> commands, int hashTurn, uint stateHash)
        {
            if (!_turns.TryGetValue(turn, out var list))
            {
                list = new List<GameCommand>();
                _turns[turn] = list;
            }
            list.AddRange(commands);
        }

        public bool TryGetCommit(int turn, List<GameCommand> into)
        {
            if (_turns.TryGetValue(turn, out var list))
            {
                into.AddRange(list);
                _turns.Remove(turn);
            }
            // A turn nobody contributed to is simply an empty turn.
            return true;
        }

        public void Poll() { }
    }
}
