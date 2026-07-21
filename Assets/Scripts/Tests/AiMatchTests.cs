using NUnit.Framework;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim.Tests
{
    /// <summary>The M9 success criterion, end to end: full AI matches must
    /// produce a victor and a vanquished on both the win and lose paths.</summary>
    public class AiMatchTests
    {
        const int Budget = 80000; // ~26 sim-minutes at 50 Hz

        static bool Resolved(GameSim sim) =>
            sim.State.Players[0].Outcome != PlayerOutcome.Playing
            || sim.State.Players[1].Outcome != PlayerOutcome.Playing;

        [Test]
        public void AiVsAi_ProducesVictorAndVanquished_WithinBudget()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 21);
            var ais = AiTestHarness.CreateAis(sim);

            int firstMill = 0, firstSmith = 0;
            int ticks = AiTestHarness.RunAiMatch(sim, ais, Budget, stop: s =>
            {
                if (firstMill == 0
                    && CountComplete(s, 0, UnitTypeId.ElvenLumberMill) > 0)
                    firstMill = s.State.Tick;
                if (firstSmith == 0
                    && CountComplete(s, 0, UnitTypeId.HumanBlacksmith) > 0)
                    firstSmith = s.State.Tick;
                return Resolved(s);
            });

            Assert.Less(ticks, Budget, "an AI-vs-AI match must actually end");
            int winners = 0, losers = 0;
            for (int p = 0; p < 2; p++)
            {
                if (sim.State.Players[p].Outcome == PlayerOutcome.Victorious) winners++;
                if (sim.State.Players[p].Outcome == PlayerOutcome.Defeated) losers++;
            }
            Assert.AreEqual(1, winners, "exactly one victor");
            Assert.AreEqual(1, losers, "exactly one vanquished");

            // Build-order fact: the mill precedes the blacksmith (script order).
            if (firstSmith > 0)
                Assert.Less(firstMill, firstSmith,
                    "the lumber mill must complete before the blacksmith");
        }

        [Test]
        public void IdleHuman_LosesToAi()
        {
            var pud = AiTestHarness.TwoBaseMap();
            pud.Owner[0] = (byte)PudOwner.Human; // slot 0: a human who never acts
            var sim = AiTestHarness.Boot(pud, seed: 21);
            var ais = AiTestHarness.CreateAis(sim);
            Assert.AreEqual(1, ais.Count, "only slot 1 is a computer");

            int ticks = AiTestHarness.RunAiMatch(sim, ais, Budget, stop: Resolved);

            Assert.Less(ticks, Budget, "the AI must finish off an idle player");
            Assert.AreEqual(PlayerOutcome.Defeated, sim.State.Players[0].Outcome,
                "the idle human loses — the lose path the victory screen polls");
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[1].Outcome);
        }

        static int CountComplete(GameSim sim, int slot, UnitTypeId type)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == slot && u.TypeId == (ushort)type
                    && (u.Flags & UnitFlags.UnderConstruction) == 0)
                    n++;
            }
            return n;
        }
    }
}
