using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class TechTreeTests
    {
        static PudFile FlatMap()
        {
            var pud = new PudFile { Width = 48, Height = 48 };
            pud.Tiles = new ushort[48 * 48];
            pud.MoveMap = new ushort[48 * 48];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0001;
            }
            pud.Owner[0] = (byte)PudOwner.Human;
            pud.StartGold[0] = 5000;
            pud.StartLumber[0] = 3000;
            pud.StartOil[0] = 1000;
            return pud;
        }

        static void Add(PudFile pud, UnitTypeId type, int x, int y, byte owner = 0) =>
            pud.Units.Add(new PudUnitEntry { X = (ushort)x, Y = (ushort)y, Type = (byte)type, Owner = owner });

        static GameSim Boot(PudFile pud, ulong seed = 7)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
        }

        static unsafe GameCommand Cmd(GameSim sim, CommandOp op, int unitSlot,
            ushort tx = 0, ushort ty = 0, uint targetUnit = 0, ushort param = 0)
        {
            var cmd = new GameCommand
            {
                Op = op,
                Player = 0,
                TargetX = tx,
                TargetY = ty,
                TargetUnit = targetUnit,
                Param = param,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId((ushort)unitSlot, sim.State.Units[unitSlot].Gen).Packed;
            return cmd;
        }

        static void Run(GameSim sim, int ticks, GameCommand? first = null)
        {
            var none = new List<GameCommand>();
            if (first.HasValue)
                sim.Advance(new List<GameCommand> { first.Value });
            for (int t = 0; t < ticks; t++)
                sim.Advance(none);
        }

        static int SlotOf(GameSim sim, UnitTypeId type)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].TypeId == (ushort)type)
                    return i;
            return -1;
        }

        static int ResearchTicks(GameSim sim, UpgradeId u) =>
            sim.State.Rules.Upgrades[(int)u].Time * 50 / 6;

        // ------------------------------------------------------------------

        [Test]
        public void SwordResearch_AppliesPlusTwoPerLevel()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.HumanBlacksmith, 10, 10);
            Add(pud, UnitTypeId.Footman, 20, 20);
            var sim = Boot(pud);
            int smith = SlotOf(sim, UnitTypeId.HumanBlacksmith);
            int footman = SlotOf(sim, UnitTypeId.Footman);
            int baseStr = sim.EffectiveStrength(ref sim.State.Units[footman]);
            ref var row = ref sim.State.Rules.Upgrades[(int)UpgradeId.Sword1];
            int gold0 = sim.State.Players[0].Gold;

            Run(sim, ResearchTicks(sim, UpgradeId.Sword1) + 10,
                Cmd(sim, CommandOp.Research, smith, param: (ushort)UpgradeId.Sword1));

            Assert.AreEqual(gold0 - row.Gold, sim.State.Players[0].Gold, "research charged");
            Assert.IsTrue(sim.State.Players[0].HasResearched(UpgradeId.Sword1));
            Assert.AreEqual(baseStr + 2, sim.EffectiveStrength(ref sim.State.Units[footman]),
                "sword 1 = +2 strength");

            Run(sim, ResearchTicks(sim, UpgradeId.Sword2) + 10,
                Cmd(sim, CommandOp.Research, smith, param: (ushort)UpgradeId.Sword2));
            Assert.AreEqual(baseStr + 4, sim.EffectiveStrength(ref sim.State.Units[footman]),
                "sword 2 = +4 strength");
        }

        [Test]
        public void Research_RequiresPriorLevelAndProvider()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.HumanBlacksmith, 10, 10);
            Add(pud, UnitTypeId.ElvenLumberMill, 20, 10);
            var sim = Boot(pud);
            int smith = SlotOf(sim, UnitTypeId.HumanBlacksmith);
            int mill = SlotOf(sim, UnitTypeId.ElvenLumberMill);
            int gold0 = sim.State.Players[0].Gold;

            // Level 2 before level 1: rejected, nothing charged.
            Run(sim, 20, Cmd(sim, CommandOp.Research, smith, param: (ushort)UpgradeId.Sword2));
            Assert.AreEqual(gold0, sim.State.Players[0].Gold);
            Assert.AreEqual(0, sim.State.Units[smith].ResearchId);

            // Swords at the lumber mill: wrong provider, rejected.
            Run(sim, 20, Cmd(sim, CommandOp.Research, mill, param: (ushort)UpgradeId.Sword1));
            Assert.AreEqual(gold0, sim.State.Players[0].Gold);
        }

        [Test]
        public void ArrowResearch_AppliesPlusOnePierce()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.ElvenLumberMill, 10, 10);
            Add(pud, UnitTypeId.Archer, 20, 20);
            var sim = Boot(pud);
            int mill = SlotOf(sim, UnitTypeId.ElvenLumberMill);
            int archer = SlotOf(sim, UnitTypeId.Archer);
            int basePierce = sim.EffectivePierce(ref sim.State.Units[archer]);

            Run(sim, ResearchTicks(sim, UpgradeId.Arrow1) + 10,
                Cmd(sim, CommandOp.Research, mill, param: (ushort)UpgradeId.Arrow1));

            Assert.AreEqual(basePierce + 1, sim.EffectivePierce(ref sim.State.Units[archer]),
                "arrow 1 = +1 pierce");
        }

        [Test]
        public void HallUpgrade_NeedsBarracksThenSwapsType()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.TownHall, 10, 10);
            var sim = Boot(pud);
            int th = SlotOf(sim, UnitTypeId.TownHall);
            int gold0 = sim.State.Players[0].Gold;

            // No barracks yet: upgrade denied.
            Run(sim, 20, Cmd(sim, CommandOp.Train, th, param: (ushort)UnitTypeId.Keep));
            Assert.AreEqual(gold0, sim.State.Players[0].Gold, "keep upgrade must be gated on barracks");
            Assert.AreEqual(0, sim.State.Units[th].BuildType);

            // With a barracks it goes through and the hall becomes a Keep.
            var pud2 = FlatMap();
            Add(pud2, UnitTypeId.TownHall, 10, 10);
            Add(pud2, UnitTypeId.HumanBarracks, 20, 10);
            var sim2 = Boot(pud2);
            th = SlotOf(sim2, UnitTypeId.TownHall);
            ref var keepRow = ref sim2.State.Rules.Units[(int)UnitTypeId.Keep];
            gold0 = sim2.State.Players[0].Gold;

            Run(sim2, keepRow.BuildTime * 50 / 6 + 20,
                Cmd(sim2, CommandOp.Train, th, param: (ushort)UnitTypeId.Keep));

            Assert.AreEqual(gold0 - keepRow.GoldCost, sim2.State.Players[0].Gold);
            Assert.AreEqual((ushort)UnitTypeId.Keep, sim2.State.Units[th].TypeId,
                "town hall upgrades in place");
            Assert.AreEqual(keepRow.Hp, sim2.State.Units[th].Hp, "hp delta applied");
        }

        [Test]
        public void RangerResearch_ConvertsArchersAndTrainQueue()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.ElvenLumberMill, 10, 10);
            Add(pud, UnitTypeId.Keep, 16, 10);
            Add(pud, UnitTypeId.HumanBarracks, 24, 10);
            Add(pud, UnitTypeId.Archer, 30, 20);
            var sim = Boot(pud);
            int mill = SlotOf(sim, UnitTypeId.ElvenLumberMill);
            int archer = SlotOf(sim, UnitTypeId.Archer);

            Run(sim, ResearchTicks(sim, UpgradeId.TrainRangers) + 10,
                Cmd(sim, CommandOp.Research, mill, param: (ushort)UpgradeId.TrainRangers));

            Assert.IsTrue(sim.State.Players[0].HasResearched(UpgradeId.TrainRangers));
            Assert.AreEqual((ushort)UnitTypeId.Ranger, sim.State.Units[archer].TypeId,
                "existing archers become rangers");
            Assert.IsTrue(sim.CanTrainAt(0, UnitTypeId.HumanBarracks, UnitTypeId.Ranger),
                "barracks now trains rangers");
            Assert.IsFalse(sim.CanTrainAt(0, UnitTypeId.HumanBarracks, UnitTypeId.Archer),
                "plain archers are gone from the queue");
        }

        [Test]
        public void Alow_DeniesRestrictedUnitAndUpgrade()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.HumanBarracks, 10, 10);
            Add(pud, UnitTypeId.Farm, 16, 16);
            Add(pud, UnitTypeId.HumanBlacksmith, 20, 10);
            pud.AllowUnits = new uint[PudFile.SlotCount];
            pud.AllowUpgrades = new uint[PudFile.SlotCount];
            pud.AllowSpellStart = new uint[PudFile.SlotCount];
            pud.AllowSpellResearch = new uint[PudFile.SlotCount];
            for (int i = 0; i < PudFile.SlotCount; i++)
            {
                pud.AllowUnits[i] = ~1u;      // bit0: no footmen/grunts
                pud.AllowUpgrades[i] = ~4u;   // bit2: no melee weapons 1
                pud.AllowSpellResearch[i] = ~0u;
            }
            var sim = Boot(pud);
            int rax = SlotOf(sim, UnitTypeId.HumanBarracks);
            int smith = SlotOf(sim, UnitTypeId.HumanBlacksmith);
            int gold0 = sim.State.Players[0].Gold;

            Run(sim, 20, Cmd(sim, CommandOp.Train, rax, param: (ushort)UnitTypeId.Footman));
            Assert.AreEqual(gold0, sim.State.Players[0].Gold, "ALOW must block footman training");

            Run(sim, 20, Cmd(sim, CommandOp.Research, smith, param: (ushort)UpgradeId.Sword1));
            Assert.AreEqual(gold0, sim.State.Players[0].Gold, "ALOW must block sword research");

            // Unrestricted lines still work.
            Assert.IsTrue(sim.CanResearchAt(0, UnitTypeId.HumanBlacksmith, UpgradeId.HumanShield1));
        }

        [Test]
        public void Repair_RestoresHpAndChargesResources()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.TownHall, 10, 10);
            Add(pud, UnitTypeId.Peasant, 16, 12);
            var sim = Boot(pud);
            int th = SlotOf(sim, UnitTypeId.TownHall);
            int peasant = SlotOf(sim, UnitTypeId.Peasant);
            int fullHp = sim.State.Rules.Units[(int)UnitTypeId.TownHall].Hp;
            sim.State.Units[th].Hp = fullHp / 2;
            uint thPacked = new UnitId((ushort)th, sim.State.Units[th].Gen).Packed;
            int gold0 = sim.State.Players[0].Gold;
            int lumber0 = sim.State.Players[0].Lumber;

            Run(sim, 3000, Cmd(sim, CommandOp.Repair, peasant, targetUnit: thPacked));

            Assert.AreEqual(fullHp, sim.State.Units[th].Hp, "building repaired to full");
            Assert.Less(sim.State.Players[0].Gold, gold0, "repair costs gold");
            Assert.Less(sim.State.Players[0].Lumber, lumber0, "repair costs lumber");
            Assert.AreEqual(OrderType.None, sim.State.Units[peasant].Order,
                "peasant stops when the building is whole");
        }

        [Test]
        public void CancelTraining_RefundsFullCost()
        {
            var pud = FlatMap();
            Add(pud, UnitTypeId.TownHall, 10, 10);
            Add(pud, UnitTypeId.Farm, 16, 16);
            var sim = Boot(pud);
            int th = SlotOf(sim, UnitTypeId.TownHall);
            int gold0 = sim.State.Players[0].Gold;

            Run(sim, 30, Cmd(sim, CommandOp.Train, th, param: (ushort)UnitTypeId.Peasant));
            Assert.Less(sim.State.Players[0].Gold, gold0, "training charged");

            Run(sim, 5, Cmd(sim, CommandOp.Cancel, th));
            Assert.AreEqual(gold0, sim.State.Players[0].Gold, "cancel refunds in full");
            Assert.AreEqual(0, sim.State.Units[th].BuildType);
        }

        [Test]
        public void TechRun_IsDeterministic()
        {
            uint HashRun()
            {
                var pud = FlatMap();
                Add(pud, UnitTypeId.TownHall, 10, 10);
                Add(pud, UnitTypeId.HumanBarracks, 20, 10);
                Add(pud, UnitTypeId.HumanBlacksmith, 26, 10);
                Add(pud, UnitTypeId.Peasant, 16, 14);
                var sim = Boot(pud, seed: 21);
                int th = SlotOf(sim, UnitTypeId.TownHall);
                int smith = SlotOf(sim, UnitTypeId.HumanBlacksmith);
                Run(sim, 5, Cmd(sim, CommandOp.Research, smith, param: (ushort)UpgradeId.Sword1));
                Run(sim, 3000, Cmd(sim, CommandOp.Train, th, param: (ushort)UnitTypeId.Keep));
                return sim.State.ComputeHash();
            }
            Assert.AreEqual(HashRun(), HashRun());
        }
    }
}
