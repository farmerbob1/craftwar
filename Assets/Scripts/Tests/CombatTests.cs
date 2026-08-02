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

        /// <summary>
        /// Catapults/ballistas fire a ground-targeted splash shot (BULLET.C):
        /// it commits to the target's position the instant it launches and
        /// never re-aims, so a target that moves away mid-flight is missed —
        /// while a bystander standing where the shot actually lands still
        /// takes splash damage even though it was never the locked-on target.
        /// Regression test for a homing-projectile bug: catapults used to
        /// chase the target's live position every tick like an arrow.
        /// </summary>
        [Test]
        public unsafe void Catapult_SplashesBystanders_ButDoesNotHomeOnAMovedTarget()
        {
            var pud = MakeMap(
                (UnitTypeId.Catapult, 0, 10, 10),
                (UnitTypeId.Footman, 1, 10, 18),   // locked-on target, at max range (8)
                (UnitTypeId.Peasant, 1, 10, 19));  // bystander next to the target
            var sim = new GameSim(11);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.Advance(new List<GameCommand> { AttackOrder(sim, 0, 1) });

            var none = new List<GameCommand>();
            int projSlot = -1;
            for (int t = 0; t < 200 && projSlot < 0; t++)
            {
                sim.Advance(none);
                for (int p = 0; p < sim.State.Projectiles.Length; p++)
                    if (sim.State.Projectiles[p].Active && sim.State.Projectiles[p].Splash)
                    {
                        projSlot = p;
                        break;
                    }
            }
            Assert.GreaterOrEqual(projSlot, 0, "catapult must fire a ground-targeted splash shot");

            int footmanHpBefore = sim.State.Units[1].Hp;
            int peasantHpBefore = sim.State.Units[2].Hp;

            // Yank the locked-on target far away mid-flight. A homing shot
            // would still follow it there; a ground-targeted one cannot.
            sim.State.Units[1].PixX = 2 * SimConstants.TilePixels;
            sim.State.Units[1].PixY = 2 * SimConstants.TilePixels;

            for (int t = 0; t < 60 && sim.State.Projectiles[projSlot].Active; t++)
                sim.Advance(none);

            Assert.IsFalse(sim.State.Projectiles[projSlot].Active, "the shot must land");
            Assert.AreEqual(footmanHpBefore, sim.State.Units[1].Hp,
                "a shot fired at a target's old position must miss once the target has moved away");
            Assert.Less(sim.State.Units[2].Hp, peasantHpBefore,
                "the bystander standing where the shot actually landed must take splash damage");
        }

        /// <summary>
        /// BULLET.C hard-codes O_DRAGON/H_GRIFFON in bullet_create_fireball to
        /// keep drifting past the impact point after the first hit, re-running
        /// the splash every few ticks instead of stopping at one impact — a
        /// short chain of explosions, unlike the single-hit catapult/ballista
        /// splash. Regression test for a bug where gryphons/dragons landed a
        /// single direct hit like an ordinary homing arrow.
        /// </summary>
        [Test]
        public unsafe void GryphonRider_FireballChainsMultipleSplashPulses()
        {
            var pud = MakeMap(
                (UnitTypeId.GryphonRider, 0, 10, 10),
                (UnitTypeId.Footman, 1, 10, 14)); // 4 tiles south: at max range
            var sim = new GameSim(7);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.Advance(new List<GameCommand> { AttackOrder(sim, 0, 1) });

            var none = new List<GameCommand>();
            int projSlot = -1;
            for (int t = 0; t < 200 && projSlot < 0; t++)
            {
                sim.Advance(none);
                for (int p = 0; p < sim.State.Projectiles.Length; p++)
                    if (sim.State.Projectiles[p].Active && sim.State.Projectiles[p].Splash)
                    {
                        projSlot = p;
                        break;
                    }
            }
            Assert.GreaterOrEqual(projSlot, 0, "gryphon must fire a ground-targeted splash shot");
            Assert.AreEqual(SimConstants.FireballChainPulses,
                sim.State.Projectiles[projSlot].ChainPulsesRemaining,
                "a fresh fireball still has all its chain pulses ahead of it");

            // Each pulse re-runs damage_area at an impact point that keeps
            // drifting forward, so — exactly like the original — only the
            // first pulse reliably lands on a stationary target; the rest are
            // a trail of explosions past it. What must hold is the pulse
            // countdown itself: 3 further splashes after the first hit,
            // rather than the projectile just vanishing on first contact.
            int lastPulses = sim.State.Projectiles[projSlot].ChainPulsesRemaining;
            int pulseEvents = 0;
            int footmanHpBefore = sim.State.Units[1].Hp;
            for (int t = 0; t < 120 && sim.State.Projectiles[projSlot].Active; t++)
            {
                sim.Advance(none);
                int pulses = sim.State.Projectiles[projSlot].ChainPulsesRemaining;
                if (pulses < lastPulses)
                    pulseEvents++;
                lastPulses = pulses;
            }
            Assert.AreEqual(SimConstants.FireballChainPulses, pulseEvents,
                "the fireball must re-splash on every chain pulse before it expires");
            Assert.Less(sim.State.Units[1].Hp, footmanHpBefore,
                "the first (point-blank) pulse must still land on the target");
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

        /// <summary>Issue an explicit Attack order on unit slot 1 to slot 0.</summary>
        static unsafe GameCommand AttackOrder(GameSim sim, int attacker, int target)
        {
            var cmd = new GameCommand
            {
                Op = CommandOp.Attack,
                Player = sim.State.Units[attacker].Player,
                TargetUnit = new UnitId((ushort)target, sim.State.Units[target].Gen).Packed,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] =
                new UnitId((ushort)attacker, sim.State.Units[attacker].Gen).Packed;
            return cmd;
        }

        /// <summary>
        /// An attacker holding an Attack order on something it is already in
        /// range of must not move at all. Movement runs before combat in the
        /// tick order, so without a gate the unit paths at its target's tile
        /// every tick, takes whatever step A* offers (a corner of a building's
        /// footprint is always reachable even when the goal tile is not), and
        /// has the path cancelled by TickCombat a moment later — the "units pace
        /// around while fighting" bug, most visible against buildings.
        /// </summary>
        [Test]
        public void EngagedAndInRange_TheAttackerNeverMoves()
        {
            // The hall covers 12..15 x 10..13. Standing off its north-west
            // corner is in range (footprint distance 1) but NOT adjacent to the
            // origin tile in a way that blocks the first step, so this is the
            // case that used to orbit.
            var pud = MakeMap(
                (UnitTypeId.Footman, 0, 11, 9),
                (UnitTypeId.TownHall, 1, 12, 10));
            var rules = RuleSet.CreateDefault();
            var sim = new GameSim(5);
            sim.Setup(pud, rules);
            sim.Advance(new List<GameCommand> { AttackOrder(sim, 0, 1) });

            var none = new List<GameCommand>();
            for (int t = 0; t < 600 && sim.State.Units[1].IsAlive; t++)
            {
                sim.Advance(none);
                Assert.AreEqual(11, sim.State.Units[0].TileX,
                    "an attacker already in range must hold its tile");
                Assert.AreEqual(9, sim.State.Units[0].TileY);
            }
            Assert.Less(sim.State.Units[1].Hp, rules.Units[(int)UnitTypeId.TownHall].Hp,
                "and it must actually be hitting the hall");
        }

        /// <summary>
        /// A chase aimed at a building's ORIGIN tile marches the attacker to the
        /// far side of the footprint. Coming at a 4x4 hall from the south-west,
        /// the attacker must stop on the nearest face, not walk up to the
        /// north-west corner where the origin tile happens to be.
        /// </summary>
        [Test]
        public void ChasingABuilding_ApproachesTheNearestFace()
        {
            // Hall covers 12..15 x 10..13, origin (12,10) at the NW corner.
            var pud = MakeMap(
                (UnitTypeId.Footman, 0, 4, 13),
                (UnitTypeId.TownHall, 1, 12, 10));
            var rules = RuleSet.CreateDefault();
            var sim = new GameSim(5);
            sim.Setup(pud, rules);
            sim.Advance(new List<GameCommand> { AttackOrder(sim, 0, 1) });

            var none = new List<GameCommand>();
            int stoppedAt = -1;
            for (int t = 0; t < 1500 && sim.State.Units[1].IsAlive; t++)
            {
                sim.Advance(none);
                ref var footman = ref sim.State.Units[0];
                if (footman.IsMoving || footman.TileX != 11)
                    continue;
                stoppedAt = footman.TileY;
                break;
            }
            Assert.GreaterOrEqual(stoppedAt, 0, "the footman never reached the hall");
            Assert.GreaterOrEqual(stoppedAt, 12,
                "it must stop on the face it approached, not walk round to the origin corner");
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
