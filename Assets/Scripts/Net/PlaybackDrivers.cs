using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// Replays a recorded command log through the normal driver seam. A match is
    /// fully described by (map, seed, command log), so a sim fed by this driver
    /// from tick 0 must reproduce the recorded run's final state hash — the
    /// project's end-to-end determinism check, now available to the app and not
    /// just to the test harness.
    /// </summary>
    public sealed class ReplayLockstepDriver : ILockstepDriver
    {
        readonly Replay _replay;
        int _cursor;
        int _starveCalls;

        public ReplayLockstepDriver(Replay replay)
        {
            _replay = replay ?? throw new System.ArgumentNullException(nameof(replay));
        }

        /// <summary>True once every recorded command has been handed out.</summary>
        public bool Finished => _cursor >= _replay.Entries.Count;

        /// <summary>Tick of the last recorded command, i.e. how far playback must
        /// run to be complete.</summary>
        public int LastTick =>
            _replay.Entries.Count == 0 ? 0 : _replay.Entries[_replay.Entries.Count - 1].tick;

        /// <summary>
        /// Make the next <paramref name="calls"/> calls report "not ready". The
        /// network drivers stall for real; this lets the game loop's starvation
        /// path be exercised deterministically without a socket.
        /// </summary>
        public void StarveFor(int calls) => _starveCalls = calls;

        /// <summary>Playback is authoritative: live input is discarded, not queued.</summary>
        public void SubmitLocalCommand(in GameCommand cmd) { }

        public bool TryGetTickCommands(int tick, List<GameCommand> commands)
        {
            if (_starveCalls > 0)
            {
                _starveCalls--;
                return false;
            }

            commands.Clear();
            // Defensive: a log that somehow contains an out-of-order entry must
            // not park the cursor forever.
            while (_cursor < _replay.Entries.Count && _replay.Entries[_cursor].tick < tick)
                _cursor++;
            while (_cursor < _replay.Entries.Count && _replay.Entries[_cursor].tick == tick)
            {
                commands.Add(_replay.Entries[_cursor].cmd);
                _cursor++;
            }
            LocalLockstepDriver.SortCanonically(commands);
            return true;
        }
    }

    /// <summary>
    /// Local play with an artificial input delay: a command submitted while tick
    /// T is current executes at T + delay. Networked lockstep imposes exactly
    /// this, so it makes the feel of a given delay testable — and any code that
    /// wrongly assumes an order takes effect immediately fails here rather than
    /// on the LAN.
    /// </summary>
    public sealed class DelayedLockstepDriver : ILockstepDriver
    {
        readonly int _delayTicks;
        readonly List<(int tick, GameCommand cmd)> _queue = new List<(int, GameCommand)>();
        int _currentTick;

        public DelayedLockstepDriver(int delayTicks)
        {
            _delayTicks = delayTicks < 0 ? 0 : delayTicks;
        }

        public int DelayTicks => _delayTicks;

        public void SubmitLocalCommand(in GameCommand cmd) =>
            _queue.Add((_currentTick + _delayTicks, cmd));

        public bool TryGetTickCommands(int tick, List<GameCommand> commands)
        {
            _currentTick = tick;
            commands.Clear();
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i].tick > tick)
                    continue;
                commands.Add(_queue[i].cmd);
                _queue.RemoveAt(i);
            }
            // The scan above runs backwards, so restore submission order before
            // the canonical per-player sort (which is stable and preserves it).
            commands.Reverse();
            LocalLockstepDriver.SortCanonically(commands);
            return true;
        }
    }
}
