using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// Wires a driver straight into a <see cref="TurnRelay"/> with no socket in
    /// between. Every peer in a test shares one relay, so N sims run the real
    /// host-relay protocol in-process: the scheduling, the completeness rule and
    /// the hash comparison are all exercised, and the result is reproducible in
    /// the headless harness in under a second. Only the bytes-on-the-wire layer
    /// is missing, and that is tested separately by the message round-trips.
    /// </summary>
    public sealed class LoopbackTurnExchange : ITurnExchange
    {
        readonly TurnRelay _relay;
        readonly byte _slot;

        public LoopbackTurnExchange(TurnRelay relay, byte slot)
        {
            _relay = relay;
            _slot = slot;
        }

        public NetStatus Status => NetStatus.Running;

        public void SendInput(int turn, List<GameCommand> commands, int hashTurn, uint stateHash) =>
            _relay.SubmitInput(_slot, turn, commands, hashTurn, stateHash);

        public bool TryGetCommit(int turn, List<GameCommand> into) =>
            _relay.TryGetCommitted(turn, into);

        public void Poll() { }
    }
}
