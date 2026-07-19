using System.Collections.Generic;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Fog of war. Two things are pinned here: the reveal rule itself (radius,
    /// footprint, who grants vision) and the contract that fog is real hashed
    /// state which never breaks determinism.
    /// </summary>
    public class FogTests
    {
        static readonly IReadOnlyList<GameCommand> NoCommands = new List<GameCommand>();
        const byte P0 = 0;

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
            return pud;
        }

        static GameSim Boot(PudFile pud, ulong seed = 7)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
        }

        static int SlotOf(GameSim sim, UnitTypeId type)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].TypeId == (ushort)type)
                    return i;
            return -1;
        }

        [Test]
        public void LoneUnit_RevealsWithinSight_AndNotBeyond()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.Footman, Owner = P0 });
            var sim = Boot(pud);

            int slot = SlotOf(sim, UnitTypeId.Footman);
            int sight = sim.EffectiveSight(ref sim.State.Units[slot]);
            Assert.Greater(sight, 0, "footman should have a nonzero sight radius");

            Assert.IsTrue(sim.IsVisible(P0, 16, 16), "own tile must be visible");
            Assert.IsTrue(sim.IsVisible(P0, 16 + sight, 16), "tile at exactly sight range is visible");
            Assert.IsFalse(sim.IsVisible(P0, 16 + sight + 1, 16), "one tile past sight stays dark");
            // Diagonals use true squared distance, so the disc is round, not square.
            Assert.IsFalse(sim.IsVisible(P0, 16 + sight, 16 + sight),
                "the corner of the bounding box is outside the sight disc");
        }

        [Test]
        public void Explored_PersistsAfterUnitLeaves()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 5, Y = 5, Type = (byte)UnitTypeId.Footman, Owner = P0 });
            var sim = Boot(pud);

            Assert.IsTrue(sim.IsVisible(P0, 5, 5));
            Assert.IsTrue(sim.IsExplored(P0, 5, 5));

            // Teleport the unit far away and re-run fog.
            int slot = SlotOf(sim, UnitTypeId.Footman);
            sim.State.Units[slot].TileX = 28;
            sim.State.Units[slot].TileY = 28;
            sim.Advance(NoCommands);

            Assert.IsFalse(sim.IsVisible(P0, 5, 5), "sight follows the unit");
            Assert.IsTrue(sim.IsExplored(P0, 5, 5), "explored is never cleared");
        }

        [Test]
        public void HiddenUnits_GrantNoVision()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.Peasant, Owner = P0 });
            var sim = Boot(pud);

            int slot = SlotOf(sim, UnitTypeId.Peasant);
            sim.State.Units[slot].Flags |= UnitFlags.Hidden;
            sim.Advance(NoCommands);

            Assert.IsFalse(sim.IsVisible(P0, 16, 16),
                "a unit inside a mine or depot sees nothing");
        }

        [Test]
        public void NeutralUnits_HaveNoFogGrid()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.GoldMine, Owner = 15 });
            var sim = Boot(pud);

            // Neutral has no player slot; it must not reveal for anyone, and
            // must not throw while walking the unit list.
            Assert.IsFalse(sim.IsVisible(P0, 16, 16));
            Assert.IsFalse(sim.IsVisible(15, 16, 16));
        }

        [Test]
        public void RangerScouting_ExtendsSightByThree()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.Ranger, Owner = P0 });
            var sim = Boot(pud);

            int slot = SlotOf(sim, UnitTypeId.Ranger);
            int baseSight = sim.EffectiveSight(ref sim.State.Units[slot]);
            Assert.IsFalse(sim.IsVisible(P0, 16 + baseSight + 3, 16));

            sim.State.Players[P0].Researched |= 1ul << (int)UpgradeId.RangerScouting;
            sim.Advance(NoCommands);

            Assert.AreEqual(baseSight + 3, sim.EffectiveSight(ref sim.State.Units[slot]));
            Assert.IsTrue(sim.IsVisible(P0, 16 + baseSight + 3, 16),
                "scouting must actually widen the revealed disc");
        }

        [Test]
        public void Building_RevealsFromWholeFootprint_NotJustCorner()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 10, Y = 10, Type = (byte)UnitTypeId.TownHall, Owner = P0 });
            var sim = Boot(pud);

            int slot = SlotOf(sim, UnitTypeId.TownHall);
            ref Unit hall = ref sim.State.Units[slot];
            int size = sim.State.Footprint(hall.TypeId);
            int sight = sim.EffectiveSight(ref hall);
            Assert.Greater(size, 1, "town hall should be multi-tile");

            // Measured from the far edge of the footprint, not the top-left.
            Assert.IsTrue(sim.IsVisible(P0, 10 + size - 1 + sight, 10),
                "sight extends from the far edge of the building");
            Assert.IsFalse(sim.IsVisible(P0, 10 + size - 1 + sight + 1, 10));
            // Every tile the building occupies is itself visible.
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    Assert.IsTrue(sim.IsVisible(P0, 10 + dx, 10 + dy));
        }

        [Test]
        public void StateHash_IsSensitiveToExplored()
        {
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.Footman, Owner = P0 });
            var sim = Boot(pud);

            uint before = sim.State.ComputeHash();
            // A tile nothing has seen yet, so this is a real change.
            Assert.IsFalse(sim.IsExplored(P0, 0, 0));
            sim.State.Explored[P0][0] = 1;
            Assert.AreNotEqual(before, sim.State.ComputeHash(),
                "fog must be part of the hashed state");
        }

        [Test]
        public void Fog_IsDeterministic_AcrossIdenticalRuns()
        {
            GameSim Run()
            {
                var pud = BaseMap();
                pud.Units.Add(new PudUnitEntry { X = 8, Y = 8, Type = (byte)UnitTypeId.Footman, Owner = P0 });
                pud.Units.Add(new PudUnitEntry { X = 20, Y = 20, Type = (byte)UnitTypeId.TownHall, Owner = P0 });
                var s = Boot(pud, seed: 99);
                for (int i = 0; i < 200; i++)
                    s.Advance(NoCommands);
                return s;
            }

            Assert.AreEqual(Run().State.ComputeHash(), Run().State.ComputeHash());
        }

        [Test]
        public void EmptySlots_HaveNoGrids_AndHashCleanly()
        {
            // Player 1 is not in the map's owner table, so it never plays.
            var pud = BaseMap();
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 16, Type = (byte)UnitTypeId.Footman, Owner = P0 });
            var sim = Boot(pud);

            Assert.IsFalse(sim.State.Players[1].InGame);
            Assert.IsNull(sim.State.Visible[1], "unused slots allocate nothing");
            Assert.DoesNotThrow(() => sim.State.ComputeHash());
            Assert.IsFalse(sim.IsVisible(1, 16, 16));
        }

        [Test]
        public void SimWithoutMap_TicksFogSafely()
        {
            // DeterminismTests builds a sim with no Setup at all; TickFog must
            // tolerate null Terrain/grids.
            var sim = new GameSim(3);
            Assert.DoesNotThrow(() => sim.Advance(NoCommands));
            Assert.DoesNotThrow(() => sim.State.ComputeHash());
            Assert.IsFalse(sim.IsVisible(P0, 0, 0));
        }
    }
}
