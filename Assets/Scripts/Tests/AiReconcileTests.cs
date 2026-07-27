using System.Collections.Generic;
using NUnit.Framework;
using Craftwar.Sim.Pud;
using Craftwar.Sim.Ai;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// AiPlayer.ReconcileFromState() closes the M10/M11 "cold AI takeover" bug:
    /// a freshly constructed AiPlayer attached to a seat that is already
    /// mid-game (a substituted human seat, or a loaded save) starts with an
    /// empty pending-build ledger, so EffectiveGold/EffectiveLumber over-report
    /// the treasury while a builder is mid-walk to a site (cost is only
    /// deducted on arrival) and the AI can over-commit spend it doesn't
    /// actually have. (A duplicate *order* for the same role turns out not to
    /// be the observable symptom — GameSim creates the under-construction
    /// building entity as soon as the order is accepted, so OwnedForRole's
    /// CountAlive(includeUnderConstruction:true) already self-heals that part
    /// from live state regardless of the AI's own ledger.)
    /// </summary>
    public class AiReconcileTests
    {
        /// <summary>One computer base plus an inert human dummy far away —
        /// without a live opponent, TickVictory resolves the match at tick 1
        /// and the AI (correctly) stops thinking. Mirrors AiEconomyTests.SoloMap.</summary>
        static PudFile SoloMap()
        {
            var pud = AiTestHarness.TwoBaseMap();
            pud.Owner[1] = (byte)PudOwner.Human;
            return pud;
        }

        [Test]
        public void ReconcileFromState_RebuildsPendingLedgerFromLiveState()
        {
            var sim = AiTestHarness.Boot(SoloMap(), seed: 5);
            var live = new AiPlayer(0, AiBehavior.LandAttack);
            var buffer = new List<GameCommand>();

            // Run until the live AI has at least one builder actually in
            // flight (Order == Build, not yet Hidden/merged into the site).
            bool found = false;
            for (int t = 0; t < 20000 && !found; t++)
            {
                buffer.Clear();
                live.Think(sim, buffer);
                sim.Advance(buffer);
                found = live.PendingCount > 0;
            }
            Assert.IsTrue(found,
                "the live AI must have at least one pending build within budget");
            int livePending = live.PendingCount;

            // A freshly constructed AiPlayer for the same seat, with no
            // reconciliation, has no idea any of that is in flight.
            var blind = new AiPlayer(0, AiBehavior.LandAttack);
            Assert.AreEqual(0, blind.PendingCount,
                "sanity check: a fresh, un-reconciled AiPlayer must start with an " +
                "empty ledger, or this test is not exercising the bug");

            // ReconcileFromState must rebuild the same ledger a continuously-
            // running AI would have at this exact tick, purely from sim state.
            var reconciled = new AiPlayer(0, AiBehavior.LandAttack);
            reconciled.ReconcileFromState(sim);
            Assert.AreEqual(livePending, reconciled.PendingCount,
                "a reconciled AiPlayer must see the same in-flight builds the " +
                "live AI's own ledger holds");
        }

        [Test]
        public void ReconcileFromState_IgnoresHiddenBuilders()
        {
            // A builder that has already arrived and merged into the site
            // (Hidden) is no longer "in flight" from the ledger's point of
            // view — the sim has already deducted its real cost on arrival,
            // so holding it back a second time would be the ledger creating
            // its own double-spend in the other direction.
            var sim = AiTestHarness.Boot(SoloMap(), seed: 5);
            var live = new AiPlayer(0, AiBehavior.LandAttack);
            var buffer = new List<GameCommand>();

            bool sawHiddenBuilder = false;
            for (int t = 0; t < 20000 && !sawHiddenBuilder; t++)
            {
                buffer.Clear();
                live.Think(sim, buffer);
                sim.Advance(buffer);
                sawHiddenBuilder = CountHiddenBuilders(sim) > 0;
            }
            Assert.IsTrue(sawHiddenBuilder,
                "a builder must reach and merge into its site within budget");

            var reconciled = new AiPlayer(0, AiBehavior.LandAttack);
            reconciled.ReconcileFromState(sim);

            int expected = CountInFlightBuilders(sim);
            Assert.AreEqual(expected, reconciled.PendingCount,
                "the ledger must include exactly the non-hidden in-flight builders");
            Assert.Greater(CountHiddenBuilders(sim), 0,
                "sanity check: there must be a hidden builder the ledger correctly excluded");
        }

        static int CountInFlightBuilders(GameSim sim)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == 0 && u.Order == OrderType.Build
                    && u.BuildType != 0 && (u.Flags & UnitFlags.Hidden) == 0)
                    n++;
            }
            return n;
        }

        static int CountHiddenBuilders(GameSim sim)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == 0 && u.BuildType != 0
                    && (u.Flags & UnitFlags.Hidden) != 0)
                    n++;
            }
            return n;
        }
    }
}
