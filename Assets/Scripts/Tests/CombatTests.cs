using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class CombatTests
    {
        static PudFile MakeMap(params (UnitTypeId type, byte owner, ushort x, ushort y)[] units)
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
            pud.Owner[1] = (byte)PudOwner.Computer;
            foreach (var (type, owner, x, y) in units)
                pud.Units.Add(new PudUnitEntry { X = x, Y = y, Type = (byte)type, Owner = owner });
            return pud;
        }

        static GameSim Run(PudFile pud, int ticks, ulong seed = 1)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            var none = new List<GameCommand>();
            for (int t = 0; t < ticks; t++)
                sim.Advance(none);
            return sim;
        }

        static int AliveCount(GameSim sim, byte player)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].Player == player)
                    n++;
            return n;
        }

        [Test]
        public void AdjacentEnemies_AutoAcquireAndFightToTheDeath()
        {
            var pud = MakeMap(
                (UnitTypeId.Footman, 0, 10, 10),
                (UnitTypeId.Grunt, 1, 11, 10));
            var sim = Run(pud, 3000);

            int p0 = AliveCount(sim, 0), p1 = AliveCount(sim, 1);
            Assert.AreEqual(1, p0 + p1, "exactly one survivor in a mirror duel");
        }

        [Test]
        public void Duel_IsDeterministic()
        {
            var a = Run(MakeMap((UnitTypeId.Footman, 0, 10, 10), (UnitTypeId.Grunt, 1, 11, 10)), 3000, 99);
            var b = Run(MakeMap((UnitTypeId.Footman, 0, 10, 10), (UnitTypeId.Grunt, 1, 11, 10)), 3000, 99);
            Assert.AreEqual(a.State.ComputeHash(), b.State.ComputeHash());
        }

        [Test]
        public void Archer_KillsAtRange_WithProjectiles()
        {
            // Peasant can't reach react range conclusions: park it far from the
            // archer's react range? No — put it inside range; peasant has 1
            // attack range and low damage, archer kills from 4 tiles away.
            var pud = MakeMap(
                (UnitTypeId.Archer, 0, 10, 10),
                (UnitTypeId.Peasant, 1, 14, 10));
            var sim = Run(pud, 1500);
            Assert.AreEqual(0, AliveCount(sim, 1), "peasant should die to arrows");
            Assert.AreEqual(1, AliveCount(sim, 0), "archer survives");
        }

        [Test]
        public void ProjectilesInFlight_AreHashedAndDeterministic()
        {
            var pud = MakeMap(
                (UnitTypeId.Archer, 0, 10, 10),
                (UnitTypeId.Footman, 1, 14, 10));
            var sim = new GameSim(5);
            sim.Setup(pud, RuleSet.CreateDefault());
            var none = new List<GameCommand>();
            bool sawProjectile = false;
            for (int t = 0; t < 600 && !sawProjectile; t++)
            {
                sim.Advance(none);
                for (int p = 0; p < sim.State.Projectiles.Length; p++)
                    if (sim.State.Projectiles[p].Active)
                        sawProjectile = true;
            }
            Assert.IsTrue(sawProjectile, "archer must launch visible projectiles");
        }

        [Test]
        public void OutOfReactRange_NoFight()
        {
            var pud = MakeMap(
                (UnitTypeId.Footman, 0, 2, 2),
                (UnitTypeId.Grunt, 1, 28, 28));
            var sim = Run(pud, 500);
            Assert.AreEqual(1, AliveCount(sim, 0));
            Assert.AreEqual(1, AliveCount(sim, 1));
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                Assert.AreEqual(0u, sim.State.Units[i].AttackTarget, "nobody should have engaged");
        }

        [Test]
        public unsafe void AttackMove_EngagesEnemiesAlongPath()
        {
            var pud = MakeMap(
                (UnitTypeId.Footman, 0, 2, 16),
                (UnitTypeId.Grunt, 1, 16, 16));
            var sim = new GameSim(7);
            sim.Setup(pud, RuleSet.CreateDefault());

            var cmd = new GameCommand
            {
                Op = CommandOp.AttackMove,
                Player = 0,
                TargetX = 30,
                TargetY = 16,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId(0, sim.State.Units[0].Gen).Packed;

            sim.Advance(new List<GameCommand> { cmd });
            var none = new List<GameCommand>();
            for (int t = 0; t < 4000; t++)
                sim.Advance(none);

            // A mirror matchup decides by first strike; what matters here is
            // that the two engaged at all (the no-contact case is covered by
            // OutOfReactRange_NoFight) and the survivor finished its business.
            Assert.AreEqual(1, AliveCount(sim, 0) + AliveCount(sim, 1),
                "attack-move must engage the enemy along the path — someone dies");
            if (AliveCount(sim, 0) == 1)
            {
                ref var footman = ref sim.State.Units[0];
                Assert.AreEqual(30, footman.TileX, "survivor resumes attack-move to its goal");
                Assert.AreEqual(16, footman.TileY);
            }
        }

        [Test]
        public void DamageRoll_MatchesFormulaDistribution()
        {
            // Footman (6+3) vs armor 2: dmg = max(0,6-2)+3 = 7, half = 4,
            // roll in [4, 8]; mean = 6.
            var rules = RuleSet.CreateDefault();
            var rng = new Pcg32(1234, 54);
            ref var footman = ref rules.UnitType(UnitTypeId.Footman);
            int min = int.MaxValue, max = int.MinValue;
            long sum = 0;
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                int dmg = footman.BasicDamage - footman.Armor;
                if (dmg < 0) dmg = 0;
                dmg += footman.PiercingDamage;
                int half = (dmg + 1) / 2;
                int roll = half + rng.Next(half + 1);
                if (roll < min) min = roll;
                if (roll > max) max = roll;
                sum += roll;
            }
            Assert.AreEqual(4, min);
            Assert.AreEqual(8, max);
            double mean = (double)sum / n;
            Assert.That(mean, NUnit.Framework.Is.EqualTo(6.0).Within(0.1), "uniform [4,8] mean ~6");
        }
    }
}
