using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The cumulative per-player statistics that feed the end-game score screen:
    /// kills/razings credited to the attacker, losses to the victim, and resources
    /// gathered at drop-off. Hashed, so they are deterministic. Pure tests.
    /// </summary>
    public class MatchStatsTests
    {
        static PudFile FlatMap(int size = 48)
        {
            var pud = new PudFile { Width = size, Height = size };
            pud.Tiles = new ushort[size * size];
            pud.MoveMap = new ushort[size * size];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0001;
            }
            AiTestHarness.Seat(pud, 0, PudOwner.Computer, Race.Human);
            AiTestHarness.Seat(pud, 1, PudOwner.Computer, Race.Orc);
            pud.StartGold[0] = pud.StartGold[1] = 2000;
            pud.StartLumber[0] = pud.StartLumber[1] = 1500;
            pud.StartOil[0] = pud.StartOil[1] = 1000;
            return pud;
        }

        [Test]
        public void CombatKill_CreditsKillerAndVictim()
        {
            var pud = FlatMap();
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 20, 20);
            AiTestHarness.Place(pud, 1, UnitTypeId.Grunt, 21, 20); // adjacent enemy
            var sim = AiTestHarness.Boot(pud, seed: 5);

            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == 1 && u.TypeId == (ushort)UnitTypeId.Grunt)
                    u.Hp = 3; // one blow finishes it
            }

            var empty = new List<GameCommand>();
            for (int t = 0; t < 300; t++)
                sim.Advance(empty);

            Assert.AreEqual(0, AiTestHarness.CountAlive(sim, 1, UnitTypeId.Grunt),
                "the grunt should be dead");
            Assert.AreEqual(1, sim.State.Players[0].UnitsKilled, "the footman's owner gets the kill");
            Assert.AreEqual(1, sim.State.Players[1].UnitsLost, "the grunt's owner takes the loss");
            Assert.AreEqual(0, sim.State.Players[0].UnitsLost);
            Assert.AreEqual(0, sim.State.Players[0].BuildingsRazed);
        }

        [Test]
        public void Harvesting_AccumulatesGathered()
        {
            var pud = FlatMap();
            AiTestHarness.Place(pud, 0, UnitTypeId.TownHall, 8, 8);
            AiTestHarness.Place(pud, 0, UnitTypeId.Peasant, 13, 10);
            pud.Units.Add(new PudUnitEntry
            {
                X = 16, Y = 8, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 25,
            });
            var sim = AiTestHarness.Boot(pud, seed: 5);

            int peon = -1, mine = -1;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == 0 && sim.State.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                    peon = i;
                if (u.IsAlive && sim.State.Rules.Units[u.TypeId].Is(UnitTypeFlags.GoldMine))
                    mine = i;
            }
            sim.Advance(new List<GameCommand>
            {
                AiQueries.Command(CommandOp.Harvest, 0, AiQueries.PackedId(sim.State, peon),
                    targetUnit: AiQueries.PackedId(sim.State, mine)),
            });
            var empty = new List<GameCommand>();
            for (int t = 1; t < 3000; t++)
                sim.Advance(empty);

            Assert.Greater(sim.State.Players[0].GoldGathered, 0, "gold gathered should accumulate");
            Assert.AreEqual(0, sim.State.Players[0].LumberGathered);
        }
    }
}
