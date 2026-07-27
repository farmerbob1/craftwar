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

        /// <summary>
        /// Next turn to agree. Deliberately NOT derived from the sim tick: while
        /// paused the turn clock keeps running and the tick clock does not, so
        /// the two genuinely diverge. Deriving one from the other is what made an
        /// earlier design unable to deliver a Resume.
        /// </summary>
        int _turn;

        public TurnLockstepDriver(int ticksPerTurn, int inputDelayTurns, byte localSlot,
            ITurnExchange exchange, int hashRingSize = 256)
            : this(ticksPerTurn, inputDelayTurns, localSlot, exchange, startTurn: 0,
                  pausingSlots: null, hashRingSize)
        {
        }

        /// <summary>
        /// Resume-at-a-turn overload: a client rejoining mid-match from a
        /// snapshot lands here instead of turn 0, since turns before
        /// <paramref name="startTurn"/> already ran on every peer and will
        /// never be asked for again. <paramref name="pausingSlots"/> mirrors
        /// whichever slots the host currently has paused — pause state is
        /// driver-only (GameSim ignores Pause/Resume entirely, see
        /// CommandOp.Pause), so a snapshot alone can never carry it.
        /// </summary>
        public TurnLockstepDriver(int ticksPerTurn, int inputDelayTurns, byte localSlot,
            ITurnExchange exchange, int startTurn, bool[] pausingSlots, int hashRingSize = 256)
        {
            if (ticksPerTurn < 1)
                throw new ArgumentOutOfRangeException(nameof(ticksPerTurn), "a turn is at least one tick");
            if (inputDelayTurns < 1)
                throw new ArgumentOutOfRangeException(nameof(inputDelayTurns),
                    "a command cannot execute in the turn it was issued: peers must agree first");
            if (startTurn < 0)
                throw new ArgumentOutOfRangeException(nameof(startTurn));
            _ticksPerTurn = ticksPerTurn;
            _inputDelayTurns = inputDelayTurns;
            LocalSlot = localSlot;
            _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
            _hashRing = new (int, uint)[hashRingSize < 1 ? 1 : hashRingSize];
            _turn = startTurn;
            _publishedThroughTurn = startTurn - 1;
            _confirmedTurn = startTurn - 1;
            _currentTurn = startTurn - 1;

            if (pausingSlots != null)
                for (byte s = 0; s < pausingSlots.Length && s < SimConstants.MaxPlayers; s++)
                    if (pausingSlots[s])
                    {
                        _pausingSlots[s] = true;
                        _pausingCount++;
                    }

            // Bootstrap the turns whose input can never be produced by a running
            // turn, because no turn has run yet (relative to startTurn — turns
            // before it are the responsibility of no one anymore).
            for (int t = startTurn; t <= startTurn + _inputDelayTurns - 2; t++)
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

        /// <summary>
        /// True while at least one player has paused. The turn clock keeps
        /// running — see <see cref="TryGetTickCommands"/> — but no sim tick
        /// executes.
        /// </summary>
        public bool IsPaused => _pausingCount > 0;

        /// <summary>Slots currently holding a pause. A SET, not a flag: two
        /// players pausing at once must not cancel each other out.</summary>
        readonly bool[] _pausingSlots = new bool[SimConstants.MaxPlayers];
        int _pausingCount;
        readonly List<GameCommand> _commitScratch = new List<GameCommand>();

        /// <summary>How far through the current turn the sim is.</summary>
        int _tickInTurn;

        public bool TryGetTickCommands(int tick, List<GameCommand> commands)
        {
            commands.Clear();

            // Mid-turn ticks: the turn they belong to is already agreed, and they
            // carry nothing.
            if (_tickInTurn > 0)
            {
                _tickInTurn++;
                if (_tickInTurn >= _ticksPerTurn)
                {
                    _tickInTurn = 0;
                    _turn++;
                }
                return true;
            }

            // Close the buffer filled during the previous turn and publish it as
            // our contribution to a turn `inputDelayTurns` ahead. Doing it here —
            // before asking for the commit — keeps every peer's publish sequence
            // identical and gapless.
            PublishInputFor(_turn + _inputDelayTurns - 1);

            _exchange.Poll();
            _commitScratch.Clear();
            if (!_exchange.TryGetCommit(_turn, _commitScratch))
            {
                Status = _exchange.Status == NetStatus.Local ? NetStatus.Local : NetStatus.Waiting;
                return false;
            }

            // Pause/Resume are read here, by the driver, because the sim ignores
            // them: pausing must not touch simulation state or the tick a replay
            // resumes on would depend on when somebody paused.
            ApplyPauseCommands(_commitScratch);

            _confirmedTurn = _turn;
            _currentTurn = _turn;
            Status = _exchange.Status;

            if (IsPaused)
            {
                // THE reason the turn counter is not derived from the tick:
                // a paused match must keep agreeing turns so that a Resume issued
                // during the pause has something to travel in. So consume the
                // turn, execute no sim tick, and drop its other commands — every
                // peer does exactly the same, from the same committed bundle, so
                // the decision stays deterministic.
                _turn++;
                return false;
            }

            commands.AddRange(_commitScratch);
            LocalLockstepDriver.SortCanonically(commands);

            _tickInTurn = 1;
            if (_tickInTurn >= _ticksPerTurn)
            {
                _tickInTurn = 0;
                _turn++;
            }
            return true;
        }

        void ApplyPauseCommands(List<GameCommand> bundle)
        {
            for (int i = 0; i < bundle.Count; i++)
            {
                var cmd = bundle[i];
                if (cmd.Player >= SimConstants.MaxPlayers)
                    continue;
                if (cmd.Op == CommandOp.Pause && !_pausingSlots[cmd.Player])
                {
                    _pausingSlots[cmd.Player] = true;
                    _pausingCount++;
                }
                else if (cmd.Op == CommandOp.Resume && _pausingSlots[cmd.Player])
                {
                    _pausingSlots[cmd.Player] = false;
                    _pausingCount--;
                }
            }
        }

        /// <summary>Release a dropped player's pause, so one peer disappearing
        /// mid-pause cannot freeze the match forever.</summary>
        public void ReleasePause(byte slot)
        {
            if (slot < SimConstants.MaxPlayers && _pausingSlots[slot])
            {
                _pausingSlots[slot] = false;
                _pausingCount--;
            }
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
