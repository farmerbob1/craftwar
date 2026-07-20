namespace Craftwar.Sim
{
    /// <summary>
    /// Decides who has won or lost. The seam the campaign track (M13) replaces to
    /// add scenario objectives and triggers without touching GameSim.
    ///
    /// Contract: pure. Reads GameState, writes only <paramref name="outcomes"/>,
    /// allocates nothing, and never touches <c>State.Rng</c> — a victory check
    /// that drew from the PRNG would make the result depend on how often it ran.
    /// </summary>
    public interface IVictoryEvaluator
    {
        /// <summary>
        /// Fill <paramref name="outcomes"/> (length SimConstants.MaxPlayers) with
        /// each slot's standing. Called with the previous tick's outcomes already
        /// in the array; implementations must overwrite every entry.
        /// </summary>
        void Evaluate(GameState state, PlayerOutcome[] outcomes);
    }

    /// <summary>
    /// Standard WC2 melee: you are out when you hold no units at all. Buildings
    /// are units in this engine, so that single test covers the original's
    /// "no units and no buildings" rule. Critters and the neutral slot's gold
    /// mines / oil patches do not count as belonging to anyone.
    ///
    /// A slot wins when it is still alive and every slot on a *different* team
    /// with <see cref="Controller"/> != None is defeated. Slots with no
    /// controller (empty, passive-computer, rescue) are ignored on both sides of
    /// that test.
    ///
    /// Faithful quirk, deliberately preserved: a player reduced to a single
    /// peasant with no gold is neither defeated nor able to win. The original
    /// stalls the same way; the UI's Surrender button is the answer.
    /// </summary>
    public sealed class MeleeVictoryEvaluator : IVictoryEvaluator
    {
        public void Evaluate(GameState state, PlayerOutcome[] outcomes)
        {
            // Nothing to decide without participants. This is the normal state of
            // a bare GameSim built without Setup (test harnesses, replay scaffolds),
            // where State.Rules is also null — so the early-out must come before
            // the unit scan, which needs Rules for the critter test.
            bool anyParticipant = false;
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                outcomes[p] = PlayerOutcome.Playing;
                if (state.Players[p].Controller != Controller.None)
                    anyParticipant = true;
            }
            if (!anyParticipant)
                return;

            // Pass 1: does each slot still hold anything?
            // Fixed-size stack array: no allocation, no collection iteration.
            bool alive0 = false, alive1 = false, alive2 = false, alive3 = false;
            bool alive4 = false, alive5 = false, alive6 = false, alive7 = false;

            for (int i = 0; i < state.HighestUnitIndex; i++)
            {
                ref Unit u = ref state.Units[i];
                if ((u.Flags & UnitFlags.Alive) == 0)
                    continue;
                if (u.Player >= SimConstants.MaxPlayers)
                    continue; // neutral 15: mines, oil patches, critters
                if (state.Rules.Units[u.TypeId].Is(UnitTypeFlags.Critter))
                    continue;

                switch (u.Player)
                {
                    case 0: alive0 = true; break;
                    case 1: alive1 = true; break;
                    case 2: alive2 = true; break;
                    case 3: alive3 = true; break;
                    case 4: alive4 = true; break;
                    case 5: alive5 = true; break;
                    case 6: alive6 = true; break;
                    case 7: alive7 = true; break;
                }
            }

            // Pass 2: standing per slot.
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                ref PlayerState ps = ref state.Players[p];
                if (ps.Controller == Controller.None)
                {
                    outcomes[p] = PlayerOutcome.Playing; // not a participant
                    continue;
                }

                bool alive = IsAlive(state, p, alive0, alive1, alive2, alive3, alive4, alive5, alive6, alive7);
                if (!alive)
                {
                    outcomes[p] = PlayerOutcome.Defeated;
                    continue;
                }

                bool anyEnemyLeft = false;
                for (int q = 0; q < SimConstants.MaxPlayers && !anyEnemyLeft; q++)
                {
                    if (q == p)
                        continue;
                    ref PlayerState qs = ref state.Players[q];
                    if (qs.Controller == Controller.None || qs.Team == ps.Team)
                        continue;
                    if (IsAlive(state, q, alive0, alive1, alive2, alive3, alive4, alive5, alive6, alive7))
                        anyEnemyLeft = true;
                }

                outcomes[p] = anyEnemyLeft ? PlayerOutcome.Playing : PlayerOutcome.Victorious;
            }
        }

        /// <summary>
        /// Holding units is not enough: a player who has already been marked
        /// Defeated stays dead. That is what makes Surrender work — otherwise a
        /// conceding player's surviving army would keep the match alive and
        /// nobody could win.
        /// </summary>
        static bool IsAlive(GameState state, int p,
            bool a0, bool a1, bool a2, bool a3, bool a4, bool a5, bool a6, bool a7)
        {
            if (state.Players[p].Outcome == PlayerOutcome.Defeated)
                return false;
            return AliveAt(p, a0, a1, a2, a3, a4, a5, a6, a7);
        }

        static bool AliveAt(int p, bool a0, bool a1, bool a2, bool a3, bool a4, bool a5, bool a6, bool a7)
        {
            switch (p)
            {
                case 0: return a0;
                case 1: return a1;
                case 2: return a2;
                case 3: return a3;
                case 4: return a4;
                case 5: return a5;
                case 6: return a6;
                default: return a7;
            }
        }
    }
}
