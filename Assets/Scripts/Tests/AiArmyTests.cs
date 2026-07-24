using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Higher-tier army tactics — focus-fire, active defense, reinforcement — all
    /// gated on tier competences. The baseline cases prove Normal (M9) never does
    /// any of it. Pure/deterministic; single Think() calls inspect the emitted
    /// commands directly.
    /// </summary>
    public class AiArmyTests
    {
        static PudFile CombatMap()
        {
            const int size = 48;
            var pud = new PudFile { Width = size, Height = size };
            pud.Tiles = new ushort[size * size];
            pud.MoveMap = new ushort[size * size];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;  // grass
                pud.MoveMap[i] = 0x0001; // land-passable
            }
            AiTestHarness.Seat(pud, 0, PudOwner.Computer, Race.Human);
            AiTestHarness.Seat(pud, 1, PudOwner.Computer, Race.Orc);
            pud.StartGold[0] = pud.StartGold[1] = 1000;
            pud.StartLumber[0] = pud.StartLumber[1] = 1000;
            pud.StartOil[0] = pud.StartOil[1] = 1000;
            return pud;
        }

        static int UnitAt(GameSim sim, int x, int y)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.TileX == x && u.TileY == y)
                    return i;
            }
            return -1;
        }

        static List<GameCommand> ThinkOnce(GameSim sim, AiTier tier)
        {
            var buf = new List<GameCommand>();
            new AiPlayer(0, AiBehavior.LandAttack, null, tier).Think(sim, buf);
            return buf;
        }

        [Test]
        public void FocusFire_TargetsTheWeakestEnemyInRange()
        {
            var pud = CombatMap();
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 20, 20);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 21, 20);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 22, 20);
            AiTestHarness.Place(pud, 1, UnitTypeId.Grunt, 24, 20);
            AiTestHarness.Place(pud, 1, UnitTypeId.Grunt, 25, 20);
            var sim = AiTestHarness.Boot(pud, seed: 11);

            int weak = UnitAt(sim, 25, 20);
            sim.State.Units[weak].Hp = 5; // wound one so it is clearly weakest
            uint weakPacked = AiQueries.PackedId(sim.State, weak);

            var buf = ThinkOnce(sim, AiTier.Smart);
            var atk = buf.Find(c => c.Op == CommandOp.Attack && c.Player == 0);
            Assert.AreNotEqual(CommandOp.None, atk.Op, "a smart AI issues a focus-fire attack");
            Assert.AreEqual(weakPacked, atk.TargetUnit, "and it focuses the weakest enemy in range");
        }

        [Test]
        public void Normal_DoesNotFocusFire()
        {
            var pud = CombatMap();
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 20, 20);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 21, 20);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 22, 20);
            AiTestHarness.Place(pud, 1, UnitTypeId.Grunt, 24, 20);
            var sim = AiTestHarness.Boot(pud, seed: 11);

            var buf = ThinkOnce(sim, AiTier.Normal);
            Assert.IsTrue(buf.TrueForAll(c => c.Op != CommandOp.Attack),
                "the M9 baseline never emits a focus-fire Attack (it musters via AttackMove)");
        }

        [Test]
        public void ActiveDefense_RecallsArmyToTheThreat()
        {
            var pud = CombatMap();
            pud.StartGold[0] = 0;
            pud.StartLumber[0] = 0; // emergency: silence the economy managers
            AiTestHarness.Place(pud, 0, UnitTypeId.TownHall, 8, 8);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 30, 30);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 31, 30);
            AiTestHarness.Place(pud, 1, UnitTypeId.Grunt, 12, 10); // right by the hall
            var sim = AiTestHarness.Boot(pud, seed: 13);

            var buf = ThinkOnce(sim, AiTier.Smart);
            var def = buf.Find(c => c.Op == CommandOp.AttackMove && c.Player == 0);
            Assert.AreNotEqual(CommandOp.None, def.Op, "a smart AI recalls to defend the base");
            Assert.LessOrEqual(
                System.Math.Abs(def.TargetX - 12) + System.Math.Abs(def.TargetY - 10), 2,
                "the recall aims at the intruder");
        }

        [Test]
        public void Normal_DoesNotActivelyDefend()
        {
            var pud = CombatMap();
            pud.StartGold[0] = 0;
            pud.StartLumber[0] = 0;
            AiTestHarness.Place(pud, 0, UnitTypeId.TownHall, 8, 8);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 30, 30);
            AiTestHarness.Place(pud, 1, UnitTypeId.Grunt, 12, 10);
            var sim = AiTestHarness.Boot(pud, seed: 13);

            var buf = ThinkOnce(sim, AiTier.Normal);
            // Only one footman, below the wave size, and no muster target work —
            // the M9 baseline just sits (no recall).
            Assert.IsTrue(buf.TrueForAll(c => c.Op != CommandOp.AttackMove),
                "the M9 baseline does not recall to defend");
        }

        [Test]
        public void SmartVsNormal_StillResolves()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 31);
            var ais = new List<AiPlayer>
            {
                new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.Smart),
                new AiPlayer(1, AiBehavior.LandAttack, null, AiTier.Normal),
            };
            int ticks = AiTestHarness.RunAiMatch(sim, ais, 80000, stop: s =>
                s.State.Players[0].Outcome != PlayerOutcome.Playing
                || s.State.Players[1].Outcome != PlayerOutcome.Playing);

            Assert.Less(ticks, 80000, "the tactical tiers must not deadlock a match");
            int winners = 0, losers = 0;
            for (int p = 0; p < 2; p++)
            {
                if (sim.State.Players[p].Outcome == PlayerOutcome.Victorious) winners++;
                if (sim.State.Players[p].Outcome == PlayerOutcome.Defeated) losers++;
            }
            Assert.AreEqual(1, winners, "exactly one victor");
            Assert.AreEqual(1, losers, "exactly one vanquished");
        }
    }
}
