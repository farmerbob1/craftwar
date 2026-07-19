using System.Collections.Generic;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Patrol marches between OrderX/Y and GoalX/Y, swapping ends on arrival.
    /// These pin the march itself and the determinism of the resulting loop.
    /// </summary>
    public class PatrolTests
    {
        /// <summary>All-land 32x32 with a single footman at (4,4).</summary>
        static PudFile MakeMap()
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0001; // land
            }
            pud.Owner[0] = (byte)PudOwner.Human;
            pud.Units.Add(new PudUnitEntry
            {
                X = 4, Y = 4, Type = (byte)UnitTypeId.Footman, Owner = 0,
            });
            return pud;
        }

        static GameSim Boot(ulong seed = 11)
        {
            var sim = new GameSim(seed);
            sim.Setup(MakeMap(), RuleSet.CreateDefault());
            return sim;
        }

        static int FootmanSlot(GameSim sim)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive
                    && sim.State.Units[i].TypeId == (ushort)UnitTypeId.Footman)
                    return i;
            return -1;
        }

        static unsafe GameCommand PatrolTo(GameSim sim, int slot, ushort tx, ushort ty)
        {
            var cmd = new GameCommand
            {
                Op = CommandOp.Patrol,
                Player = 0,
                TargetX = tx,
                TargetY = ty,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId((ushort)slot, sim.State.Units[slot].Gen).Packed;
            return cmd;
        }

        [Test]
        public void PatrolCommandSetsBothEndsOfTheBeat()
        {
            var sim = Boot();
            int slot = FootmanSlot(sim);
            sim.Advance(new List<GameCommand> { PatrolTo(sim, slot, 10, 4) });

            ref var u = ref sim.State.Units[slot];
            Assert.AreEqual(OrderType.Patrol, u.Order);
            Assert.AreEqual(10, u.OrderX, "current leg should target the clicked tile");
            Assert.AreEqual(4, u.OrderY);
            Assert.AreEqual(4, u.GoalX, "far end should be where the unit started");
            Assert.AreEqual(4, u.GoalY);
        }

        [Test]
        public void PatrolReachesTheFarEndAndTurnsAround()
        {
            var sim = Boot();
            var none = new List<GameCommand>();
            int slot = FootmanSlot(sim);
            sim.Advance(new List<GameCommand> { PatrolTo(sim, slot, 10, 4) });

            // Footmen cover roughly a tile per 50 ticks; 6 tiles each way.
            bool reachedFarEnd = false, cameBack = false;
            for (int t = 0; t < 2000; t++)
            {
                sim.Advance(none);
                ref var u = ref sim.State.Units[slot];
                if (!reachedFarEnd && u.TileX == 10 && u.TileY == 4)
                    reachedFarEnd = true;
                else if (reachedFarEnd && u.TileX == 4 && u.TileY == 4)
                {
                    cameBack = true;
                    break;
                }
            }

            Assert.IsTrue(reachedFarEnd, "patrolling unit never reached the far end");
            Assert.IsTrue(cameBack, "patrolling unit never marched back to its origin");
            Assert.AreEqual(OrderType.Patrol, sim.State.Units[slot].Order,
                "patrol should still be active after a full circuit");
        }

        [Test]
        public void PatrolIsDeterministic()
        {
            uint Run(ulong seed)
            {
                var sim = Boot(seed);
                var none = new List<GameCommand>();
                int slot = FootmanSlot(sim);
                sim.Advance(new List<GameCommand> { PatrolTo(sim, slot, 12, 9) });
                for (int t = 0; t < 1500; t++)
                    sim.Advance(none);
                return sim.State.ComputeHash();
            }

            Assert.AreEqual(Run(11), Run(11), "identical patrol runs diverged");
        }

        [Test]
        public void StopEndsAPatrol()
        {
            var sim = Boot();
            var none = new List<GameCommand>();
            int slot = FootmanSlot(sim);
            sim.Advance(new List<GameCommand> { PatrolTo(sim, slot, 10, 4) });
            for (int t = 0; t < 60; t++)
                sim.Advance(none);
            Assert.AreEqual(OrderType.Patrol, sim.State.Units[slot].Order);

            var stop = new GameCommand { Op = CommandOp.Stop, Player = 0, SelectionCount = 1 };
            unsafe
            {
                stop.Selection.Ids[0] =
                    new UnitId((ushort)slot, sim.State.Units[slot].Gen).Packed;
            }
            sim.Advance(new List<GameCommand> { stop });
            Assert.AreEqual(OrderType.None, sim.State.Units[slot].Order);
        }
    }
}
