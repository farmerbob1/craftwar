using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Higher-tier scouting and expansion, gated on tier competences. Baselines
    /// prove Normal (M9) does neither. Pure/deterministic.
    /// </summary>
    public class AiExpandTests
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
            pud.StartGold[0] = pud.StartGold[1] = 3000;
            pud.StartLumber[0] = pud.StartLumber[1] = 2000;
            pud.StartOil[0] = pud.StartOil[1] = 1000;
            return pud;
        }

        static void Mine(PudFile pud, int x, int y) => pud.Units.Add(new PudUnitEntry
        {
            X = (ushort)x, Y = (ushort)y, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 25,
        });

        [Test]
        public void God_ExpandsToAFreshMineWhenWorkerSaturated()
        {
            var pud = FlatMap();
            AiTestHarness.Place(pud, 0, UnitTypeId.TownHall, 8, 8);
            for (int i = 0; i < 12; i++) // saturate the home mine
                AiTestHarness.Place(pud, 0, UnitTypeId.Peasant, 2 + i, 20);
            Mine(pud, 13, 11);          // home mine, beside the hall (already ours)
            Mine(pud, 40, 40);          // a fresh, untapped mine
            var sim = AiTestHarness.Boot(pud, seed: 17);

            var buf = new List<GameCommand>();
            new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.God).Think(sim, buf);

            var build = buf.Find(c => c.Op == CommandOp.Build && c.Player == 0
                && c.Param == (ushort)UnitTypeId.TownHall);
            Assert.AreNotEqual(CommandOp.None, build.Op, "God builds a second hall");
            int toMine = System.Math.Abs(build.TargetX - 40) + System.Math.Abs(build.TargetY - 40);
            int toHome = System.Math.Abs(build.TargetX - 8) + System.Math.Abs(build.TargetY - 8);
            Assert.Less(toMine, toHome, "the second hall goes by the fresh mine, not back home");
        }

        [Test]
        public void Normal_DoesNotExpand()
        {
            var pud = FlatMap();
            AiTestHarness.Place(pud, 0, UnitTypeId.TownHall, 8, 8);
            for (int i = 0; i < 12; i++)
                AiTestHarness.Place(pud, 0, UnitTypeId.Peasant, 2 + i, 20);
            Mine(pud, 13, 11);
            Mine(pud, 40, 40);
            var sim = AiTestHarness.Boot(pud, seed: 17);

            var buf = new List<GameCommand>();
            new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.Normal).Think(sim, buf);

            Assert.IsFalse(
                buf.Exists(c => c.Op == CommandOp.Build && c.Param == (ushort)UnitTypeId.TownHall),
                "the M9 baseline never builds a second hall");
        }

        [Test]
        public void Smart_SendsExactlyOneScout()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 19);
            var ais = new List<AiPlayer> { new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.Smart) };
            int moves = 0;
            AiTestHarness.RunAiMatch(sim, ais, 12000,
                onCommand: (t, c) => { if (c.Op == CommandOp.Move && c.Player == 0) moves++; });
            Assert.AreEqual(1, moves, "a scouting tier sends one early scout and no more");
        }

        [Test]
        public void Normal_DoesNotScout()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 19);
            var ais = new List<AiPlayer> { new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.Normal) };
            int moves = 0;
            AiTestHarness.RunAiMatch(sim, ais, 12000,
                onCommand: (t, c) => { if (c.Op == CommandOp.Move && c.Player == 0) moves++; });
            Assert.AreEqual(0, moves, "the M9 baseline never issues a scout Move");
        }
    }
}
