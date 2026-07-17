using System.Collections.Generic;

namespace Craftwar.Sim
{
    /// <summary>
    /// The deterministic fixed-tick simulation. Advance() is the ONLY way time
    /// passes; commands are the ONLY inputs. Systems run in a fixed order every
    /// tick — never reorder without a determinism review, and never read
    /// anything outside GameState.
    /// </summary>
    public sealed class GameSim
    {
        public readonly GameState State;

        public GameSim(ulong seed)
        {
            State = new GameState(seed);
        }

        /// <summary>
        /// Advance one tick. commands may be empty; when present they were
        /// scheduled for this tick by the lockstep driver and are applied
        /// first, in list order (driver sorts them canonically).
        /// </summary>
        public void Advance(IReadOnlyList<GameCommand> commands)
        {
            if (commands != null)
            {
                for (int i = 0; i < commands.Count; i++)
                    ApplyCommand(commands[i]);
            }

            // Fixed system order — the spine of determinism.
            TickProduction();
            TickMovement();
            TickCombat();
            TickHarvest();
            TickConstruction();
            TickFog();
            TickVictory();

            State.Tick++;
        }

        void ApplyCommand(in GameCommand cmd)
        {
            // M2: dispatch to order queues per unit.
        }

        void TickProduction() { }
        void TickMovement() { }
        void TickCombat() { }
        void TickHarvest() { }
        void TickConstruction() { }
        void TickFog() { }
        void TickVictory() { }
    }
}
