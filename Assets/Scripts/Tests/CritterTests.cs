using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Critters mill about on their own (the original's fidget/sheep_try_move),
    /// and that wandering must not cost the world its reproducibility.
    /// </summary>
    public class CritterTests
    {
        static PudFile MakeMap(int critters)
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
            pud.Owner[1] = (byte)PudOwner.Computer;
            // Type 0x39 Critter on neutral slot 15 — what real maps place; the
            // tileset decides whether it is a sheep, a seal, a boar or a hog.
            for (int i = 0; i < critters; i++)
                pud.Units.Add(new PudUnitEntry
                {
                    X = (ushort)(6 + (i % 4) * 4),
                    Y = (ushort)(6 + (i / 4) * 4),
                    Type = (byte)UnitTypeId.Critter,
                    Owner = 15,
                });
            return pud;
        }

        static GameSim Run(PudFile pud, int ticks, ulong seed)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            var none = new List<GameCommand>();
            for (int t = 0; t < ticks; t++)
                sim.Advance(none);
            return sim;
        }

        [Test]
        public void Critters_WanderOnTheirOwn()
        {
            var pud = MakeMap(8);
            var sim = new GameSim(11);
            sim.Setup(pud, RuleSet.CreateDefault());

            var start = new (ushort x, ushort y)[sim.State.HighestUnitIndex];
            for (int i = 0; i < start.Length; i++)
                start[i] = (sim.State.Units[i].TileX, sim.State.Units[i].TileY);

            var none = new List<GameCommand>();
            for (int t = 0; t < 2000; t++)   // 40 seconds
                sim.Advance(none);

            int moved = 0;
            for (int i = 0; i < start.Length; i++)
            {
                ref var u = ref sim.State.Units[i];
                if (u.TileX != start[i].x || u.TileY != start[i].y)
                    moved++;
            }
            Assert.Greater(moved, 0, "critters must wander without being told to");
        }

        [Test]
        public void Critters_StayOnTheMap()
        {
            // All eight parked on the edges, where an unclamped random step
            // would walk them off the world.
            var pud = MakeMap(0);
            foreach (var (x, y) in new (ushort, ushort)[]
                     { (0, 0), (31, 0), (0, 31), (31, 31), (0, 16), (31, 16), (16, 0), (16, 31) })
                pud.Units.Add(new PudUnitEntry
                {
                    X = x, Y = y, Type = (byte)UnitTypeId.Critter, Owner = 15,
                });

            var sim = Run(pud, 3000, 3);
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref var u = ref sim.State.Units[i];
                if (!u.IsAlive) continue;
                Assert.That(u.TileX, Is.InRange(0, 31), "critter walked off the map in x");
                Assert.That(u.TileY, Is.InRange(0, 31), "critter walked off the map in y");
            }
        }

        /// <summary>
        /// The wander draws from GameState.Rng, so it is part of the world.
        /// Two runs of the same seed must still hash identically.
        /// </summary>
        [Test]
        public void CritterWandering_IsDeterministic()
        {
            var a = Run(MakeMap(8), 1500, 99);
            var b = Run(MakeMap(8), 1500, 99);
            Assert.AreEqual(a.State.ComputeHash(), b.State.ComputeHash());
        }
    }
}
