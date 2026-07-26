using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class EconomyTests
    {
        static PudFile BaseMap()
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0001;
            }
            pud.Owner[0] = (byte)PudOwner.Human;
            pud.StartGold[0] = 2000;
            pud.StartLumber[0] = 1000;
            // Forest block at rows 24-26, cols 4-8.
            for (int y = 24; y <= 26; y++)
                for (int x = 4; x <= 8; x++)
                {
                    pud.Tiles[y * 32 + x] = 0x0070;   // solid forest
                    pud.MoveMap[y * 32 + x] = 0x0081; // blocked by trees
                }
            pud.Units.Add(new PudUnitEntry { X = 10, Y = 10, Type = (byte)UnitTypeId.TownHall, Owner = 0 });
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 12, Type = (byte)UnitTypeId.Peasant, Owner = 0 });
            pud.Units.Add(new PudUnitEntry { X = 22, Y = 10, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 4 }); // 10000 gold
            return pud;
        }

        static GameSim Boot(PudFile pud, ulong seed = 3)
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

        int SlotOf(GameSim sim, UnitTypeId type)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].TypeId == (ushort)type)
                    return i;
            return -1;
        }

        [Test]
        public void GoldCycle_DeliversInHundredsWithDepotReturn()
        {
            var sim = Boot(BaseMap());
            int peasant = SlotOf(sim, UnitTypeId.Peasant);
            int mine = SlotOf(sim, UnitTypeId.GoldMine);
            uint minePacked = new UnitId((ushort)mine, sim.State.Units[mine].Gen).Packed;

            Run(sim, 4000, Cmd(sim, CommandOp.Harvest, peasant, targetUnit: minePacked));

            int gained = sim.State.Players[0].Gold - 2000;
            Assert.Greater(gained, 0, "peasant must deliver gold");
            Assert.AreEqual(0, gained % 100, "gold arrives in 100-chunks");
            Assert.Less(sim.State.Units[mine].ResourceAmount, 10000, "mine depletes");
        }

        [Test]
        public void WoodChop_MutatesMapAndDeliversLumber()
        {
            var sim = Boot(BaseMap());
            int peasant = SlotOf(sim, UnitTypeId.Peasant);

            Run(sim, 4000, Cmd(sim, CommandOp.Harvest, peasant, tx: 6, ty: 24));

            int gained = sim.State.Players[0].Lumber - 1000;
            Assert.GreaterOrEqual(gained, 100, "at least one tree delivered");
            Assert.AreEqual(0, gained % 100);

            // Auto-tiling: the felled cell becomes a boundary/stump/grass tile
            // (never solid forest 0x007x again) and opens for movement.
            bool anyChopped = false;
            for (int y = 24; y <= 26 && !anyChopped; y++)
                for (int x = 4; x <= 8 && !anyChopped; x++)
                {
                    ushort id = sim.State.Tile(y * 32 + x);
                    bool solidForest = (id & 0xFF00) == 0 && ((id >> 4) & 0xF) == 0x7;
                    if (!solidForest && sim.State.Terrain.IsPassable(MoveDomain.Land, x, y)
                        && !sim.State.Terrain.HasWood(x, y))
                        anyChopped = true;
                }
            Assert.IsTrue(anyChopped, "the map must lose a tree and open the tile");
        }

        [Test]
        public void BuildFarm_ConstructsRampsHpAndRaisesFood()
        {
            var sim = Boot(BaseMap());
            int peasant = SlotOf(sim, UnitTypeId.Peasant);

            Run(sim, 200, Cmd(sim, CommandOp.Build, peasant, tx: 18, ty: 14,
                param: (ushort)UnitTypeId.Farm));

            int farm = SlotOf(sim, UnitTypeId.Farm);
            Assert.GreaterOrEqual(farm, 0, "farm site must exist");
            Assert.IsTrue((sim.State.Units[farm].Flags & UnitFlags.UnderConstruction) != 0);
            ref var rules = ref sim.State.Rules.Units[(int)UnitTypeId.Farm];
            Assert.Less(sim.State.Units[farm].Hp, rules.Hp);
            Assert.AreEqual(2000 - rules.GoldCost, sim.State.Players[0].Gold, "gold deducted");

            Run(sim, rules.BuildTime * 50 / 6 + 100);
            Assert.IsTrue((sim.State.Units[farm].Flags & UnitFlags.UnderConstruction) == 0, "construction completes");
            Assert.AreEqual(rules.Hp, sim.State.Units[farm].Hp);
            Assert.AreEqual(SimConstants.FoodPerFarm + 1, sim.State.Players[0].FoodMax,
                "farm 4 + town hall 1");
            Assert.IsTrue((sim.State.Units[peasant].Flags & UnitFlags.Hidden) == 0,
                "builder pops back out");
        }

        [Test]
        public void TrainPeasant_SpawnsAdjacentAndChargesGold()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.Farm, Owner = 0 });
            var sim = Boot(pud);
            int th = SlotOf(sim, UnitTypeId.TownHall);
            int before = CountAlive(sim, UnitTypeId.Peasant);

            Run(sim, 20, Cmd(sim, CommandOp.Train, th, param: (ushort)UnitTypeId.Peasant));
            Assert.AreEqual(2000 - 400, sim.State.Players[0].Gold, "peasant costs 400");

            Run(sim, 45 * 50 / 6 + 60);
            Assert.AreEqual(before + 1, CountAlive(sim, UnitTypeId.Peasant), "new peasant emerges");
        }

        [Test]
        public void TrainFootman_SpawnsFromBarracks()
        {
            // Regression: Footman is unit type 0x00, which collides with the
            // BuildType==0 "idle" sentinel. Before the 1-based BuildType fix the
            // gold was spent but TickProduction skipped the queue and nothing
            // ever spawned. Guards the whole barracks->footman path.
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.Farm, Owner = 0 });
            pud.Units.Add(new PudUnitEntry { X = 5, Y = 5, Type = (byte)UnitTypeId.HumanBarracks, Owner = 0 });
            var sim = Boot(pud);
            int barracks = SlotOf(sim, UnitTypeId.HumanBarracks);
            int before = CountAlive(sim, UnitTypeId.Footman);

            Run(sim, 20, Cmd(sim, CommandOp.Train, barracks, param: (ushort)UnitTypeId.Footman));
            Assert.AreEqual(2000 - 600, sim.State.Players[0].Gold, "footman costs 600");
            Assert.AreNotEqual(0, sim.State.Units[barracks].BuildType,
                "barracks must hold the queued footman (1-based, non-zero)");
            Assert.Greater(sim.State.Units[barracks].TrainTicks, 0, "training must be counting down");

            Run(sim, 60 * 50 / 6 + 60);
            Assert.AreEqual(before + 1, CountAlive(sim, UnitTypeId.Footman), "footman emerges");
        }

        [Test]
        public void TrainingBlockedAtFoodCap()
        {
            var sim = Boot(BaseMap()); // TH food 1, one peasant already uses it
            int th = SlotOf(sim, UnitTypeId.TownHall);
            Run(sim, 60, Cmd(sim, CommandOp.Train, th, param: (ushort)UnitTypeId.Peasant));
            Assert.AreEqual(2000, sim.State.Players[0].Gold, "training must be rejected at food cap");
        }

        [Test]
        public void EconomyRun_IsDeterministic()
        {
            uint HashRun()
            {
                var sim = Boot(BaseMap(), seed: 11);
                int peasant = SlotOf(sim, UnitTypeId.Peasant);
                int mine = SlotOf(sim, UnitTypeId.GoldMine);
                uint minePacked = new UnitId((ushort)mine, sim.State.Units[mine].Gen).Packed;
                Run(sim, 3500, Cmd(sim, CommandOp.Harvest, peasant, targetUnit: minePacked));
                return sim.State.ComputeHash();
            }
            Assert.AreEqual(HashRun(), HashRun());
        }

        int CountAlive(GameSim sim, UnitTypeId type)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].TypeId == (ushort)type)
                    n++;
            return n;
        }
    }
}
