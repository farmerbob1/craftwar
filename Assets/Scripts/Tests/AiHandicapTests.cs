using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The sim-side difficulty handicaps: start-resource bonus, harvest-yield
    /// bonus, and sight bonus. All are integer, live in hashed PlayerState, are
    /// set once at Setup, and are identity at zero (the whole existing suite,
    /// which uses no handicaps, still passes — that is the identity proof). Pure.
    /// </summary>
    public class AiHandicapTests
    {
        static GameSim Boot(PudFile pud, ulong seed, int slot,
            int harvestTenths = 0, int sight = 0, int startGold = 0, int startLumber = 0)
        {
            var sim = new GameSim(seed);
            var setup = MatchSetup.FromPud(pud);
            setup.Slots[slot].HarvestBonusTenths = harvestTenths;
            setup.Slots[slot].SightBonus = sight;
            setup.Slots[slot].StartGoldBonus = startGold;
            setup.Slots[slot].StartLumberBonus = startLumber;
            sim.Setup(pud, RuleSet.CreateDefault(), setup);
            return sim;
        }

        static int Find(GameSim sim, System.Func<int, bool> pred)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && pred(i))
                    return i;
            return -1;
        }

        static int HarvestGainOver(int harvestTenths, int ticks)
        {
            var pud = AiTestHarness.TwoBaseMap();
            int startGold = pud.StartGold[0];
            var sim = Boot(pud, seed: 1, slot: 0, harvestTenths: harvestTenths);

            int peon = Find(sim, i => sim.State.Units[i].Player == 0
                && sim.State.Rules.Units[sim.State.Units[i].TypeId].Is(UnitTypeFlags.Peon));
            int mine = Find(sim, i => sim.State.Rules.Units[sim.State.Units[i].TypeId]
                .Is(UnitTypeFlags.GoldMine));
            Assert.GreaterOrEqual(peon, 0);
            Assert.GreaterOrEqual(mine, 0);

            var harvest = AiQueries.Command(CommandOp.Harvest, 0,
                AiQueries.PackedId(sim.State, peon),
                targetUnit: AiQueries.PackedId(sim.State, mine));
            sim.Advance(new List<GameCommand> { harvest });
            var empty = new List<GameCommand>();
            for (int t = 1; t < ticks; t++)
                sim.Advance(empty);
            return sim.State.Players[0].Gold - startGold;
        }

        [Test]
        public void HarvestBonus_ScalesTheYield()
        {
            int baseGain = HarvestGainOver(harvestTenths: 0, ticks: 3000);
            int bonusGain = HarvestGainOver(harvestTenths: 10, ticks: 3000); // +100%
            Assert.Greater(baseGain, 0, "the worker must complete at least one gold cycle");
            Assert.AreEqual(2 * baseGain, bonusGain,
                "a +10/10 harvest handicap must exactly double the mined gold");
        }

        [Test]
        public void StartResourceBonus_AppliesAtSetup()
        {
            var pud = AiTestHarness.TwoBaseMap();
            int gold0 = pud.StartGold[0];
            int lumber0 = pud.StartLumber[0];
            var sim = Boot(pud, seed: 1, slot: 0, startGold: 5000, startLumber: 1234);
            Assert.AreEqual(gold0 + 5000, sim.State.Players[0].Gold);
            Assert.AreEqual(lumber0 + 1234, sim.State.Players[0].Lumber);
            // The un-handicapped slot is untouched.
            Assert.AreEqual(pud.StartGold[1], sim.State.Players[1].Gold);
        }

        [Test]
        public void SightBonus_WidensEffectiveSight()
        {
            var pud = AiTestHarness.TwoBaseMap();
            var plain = Boot(pud, seed: 1, slot: 0, sight: 0);
            var eagle = Boot(pud, seed: 1, slot: 0, sight: 4);
            int u = Find(plain, i => plain.State.Units[i].Player == 0
                && plain.State.Rules.Units[plain.State.Units[i].TypeId].Is(UnitTypeFlags.Peon));
            int u2 = Find(eagle, i => eagle.State.Units[i].Player == 0
                && eagle.State.Rules.Units[eagle.State.Units[i].TypeId].Is(UnitTypeFlags.Peon));
            Assert.AreEqual(plain.EffectiveSight(ref plain.State.Units[u]) + 4,
                eagle.EffectiveSight(ref eagle.State.Units[u2]));
        }

        [Test]
        public void Handicap_IsDeterministic_AndZeroMatchesBaseline()
        {
            var pud = AiTestHarness.TwoBaseMap();
            uint a = HandicapHash(pud, harvestTenths: 3, sight: 2, ticks: 400);
            uint b = HandicapHash(pud, harvestTenths: 3, sight: 2, ticks: 400);
            Assert.AreEqual(a, b, "same seed + same handicap must reproduce");

            uint zero = HandicapHash(pud, harvestTenths: 0, sight: 0, ticks: 400);
            uint on = HandicapHash(pud, harvestTenths: 5, sight: 0, ticks: 400);
            Assert.AreNotEqual(zero, on, "a real handicap must change the hashed state");
        }

        static uint HandicapHash(PudFile pud, int harvestTenths, int sight, int ticks)
        {
            var sim = Boot(pud, seed: 7, slot: 0, harvestTenths: harvestTenths, sight: sight);
            int peon = Find(sim, i => sim.State.Units[i].Player == 0
                && sim.State.Rules.Units[sim.State.Units[i].TypeId].Is(UnitTypeFlags.Peon));
            int mine = Find(sim, i => sim.State.Rules.Units[sim.State.Units[i].TypeId]
                .Is(UnitTypeFlags.GoldMine));
            sim.Advance(new List<GameCommand>
            {
                AiQueries.Command(CommandOp.Harvest, 0,
                    AiQueries.PackedId(sim.State, peon),
                    targetUnit: AiQueries.PackedId(sim.State, mine)),
            });
            var empty = new List<GameCommand>();
            for (int t = 1; t < ticks; t++)
                sim.Advance(empty);
            return sim.State.ComputeHash();
        }
    }
}
