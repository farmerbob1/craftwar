using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// The seam between input and simulation. Local play, replays and (later)
    /// network play all feed the sim exclusively through an implementation of
    /// this — the sim itself never knows the difference.
    /// </summary>
    public interface ILockstepDriver
    {
        /// <summary>Queue a command originating from the local player.</summary>
        void SubmitLocalCommand(in GameCommand cmd);

        /// <summary>
        /// True if the sim may advance into `tick`; fills `commands` with the
        /// commands scheduled for that tick (canonical order). A network
        /// driver returns false while waiting on remote turn bundles.
        /// </summary>
        bool TryGetTickCommands(int tick, List<GameCommand> commands);
    }

    /// <summary>
    /// Zero-latency driver for single player and replay recording: commands
    /// submitted during a frame execute on the next sim tick.
    /// </summary>
    public sealed class LocalLockstepDriver : ILockstepDriver
    {
        readonly List<GameCommand> _pending = new List<GameCommand>();

        public void SubmitLocalCommand(in GameCommand cmd) => _pending.Add(cmd);

        public bool TryGetTickCommands(int tick, List<GameCommand> commands)
        {
            commands.Clear();
            commands.AddRange(_pending);
            _pending.Clear();
            return true;
        }
    }
}
