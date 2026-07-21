using NUnit.Framework;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim.Tests
{
    public class AiEconomyTests
    {
        /// <summary>One computer base plus an inert human dummy far away —
        /// without a live opponent, TickVictory resolves the match at tick 1
        /// and the AI (correctly) stops thinking.</summary>
        static PudFile SoloMap()
        {
            var pud = AiTestHarness.TwoBaseMap();
            pud.Owner[1] = (byte)PudOwner.Human;
            return pud;
        }

        [Test]
        public void FarmFirst_ThenWorkersRamp()
        {
            var sim = AiTestHarness.Boot(SoloMap(), seed: 5);
            AiTestHarness.RunAiMatch(sim, AiTestHarness.CreateAis(sim), 20000);

            // The base starts food-capped (hall = 1 food, 1 worker uses it), so
            // the very first act must be a farm; workers then climb toward the
            // phase-0 target of 9.
            Assert.GreaterOrEqual(AiTestHarness.CountAlive(sim, 0, UnitTypeId.Farm), 1,
                "food pressure must produce a farm");
            Assert.GreaterOrEqual(AiTestHarness.CountAlive(sim, 0, UnitTypeId.Peasant), 5,
                "worker count must ramp toward the script target");
            Assert.Greater(sim.State.Players[0].FoodMax, 1);
        }

        [Test]
        public void HarvestSplit_WorksBothResources()
        {
            var sim = AiTestHarness.Boot(SoloMap(), seed: 5);
            int maxOnWood = 0;
            AiTestHarness.RunAiMatch(sim, AiTestHarness.CreateAis(sim), 20000,
                stop: s =>
                {
                    int w = AiTestHarness.CountWorkersOnWood(s, 0);
                    if (w > maxOnWood)
                        maxOnWood = w;
                    return false;
                });

            Assert.Greater(maxOnWood, 0, "some workers must chop wood");
            // Both stocks must have moved: gold above start despite spending is
            // too strong early on, but deliveries are visible in the mine —
            // check the one at P0's base (18,6), not whichever scans last.
            int mineLeft = -1;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.TypeId == (ushort)UnitTypeId.GoldMine && u.TileX == 18)
                    mineLeft = u.ResourceAmount;
            }
            if (mineLeft >= 0)
                Assert.Less(mineLeft, 62500, "gold must have been mined");
        }

        [Test]
        public void NoWastedOrders_AiNeverTripsResourceDenies()
        {
            // The pending-build ledger exists so the AI never issues an order
            // the sim will bounce for money. Denies for gold/lumber/food would
            // mean the ledger failed (Build cost is deducted only on builder
            // arrival, so an unledgered AI double-spends).
            var sim = AiTestHarness.Boot(SoloMap(), seed: 5);
            int denies = 0;
            AiTestHarness.RunAiMatch(sim, AiTestHarness.CreateAis(sim), 20000,
                stop: s =>
                {
                    foreach (var e in s.State.Events)
                        if (e.Kind == SimEventKind.CommandDenied
                            && ((DenyReason)e.A == DenyReason.NotEnoughGold
                                || (DenyReason)e.A == DenyReason.NotEnoughLumber
                                || (DenyReason)e.A == DenyReason.NotEnoughOil))
                            denies++;
                    return false;
                });
            Assert.AreEqual(0, denies, "the AI must never order what it cannot pay for");
        }

        [Test]
        public void PendingLedger_HoldsBackTheSecondBuild()
        {
            // Funds cover the farm OR the lumber mill, never both. Without the
            // ledger the AI would order both (gold is only taken on arrival)
            // and waste a walk; with it, the mill waits for income.
            var pud = SoloMap();
            pud.StartGold[0] = 700;
            pud.StartLumber[0] = 500;
            var sim = AiTestHarness.Boot(pud, seed: 5);

            int overlapping = 0, inFlightCost = 0;
            AiTestHarness.RunAiMatch(sim, AiTestHarness.CreateAis(sim), 4000,
                onCommand: (tick, cmd) =>
                {
                    if (cmd.Op != CommandOp.Build)
                        return;
                    int cost = sim.State.Rules.Units[cmd.Param].GoldCost;
                    if (inFlightCost > 0 && inFlightCost + cost > 700)
                        overlapping++;
                    inFlightCost += cost;
                },
                stop: s =>
                {
                    // A build "lands" when the player's gold drops (arrival
                    // deduction); approximate by tracking builder hides.
                    for (int i = 0; i < s.State.HighestUnitIndex; i++)
                    {
                        ref Unit u = ref s.State.Units[i];
                        if (u.IsAlive && u.Player == 0
                            && (u.Flags & UnitFlags.Building) != 0
                            && (u.Flags & UnitFlags.UnderConstruction) != 0)
                        {
                            inFlightCost = 0; // first site landed and paid
                            return false;
                        }
                    }
                    return false;
                });

            Assert.AreEqual(0, overlapping,
                "no second Build may be issued while the first would exhaust the funds");
        }
    }
}
