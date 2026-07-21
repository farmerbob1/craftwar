using System.Collections.Generic;
using NUnit.Framework;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim.Tests
{
    public class AiMilitaryTests
    {
        /// <summary>Slot 0 computer with a ready army; slot 1 an inert human
        /// hall far away, so there is a target but no resistance.</summary>
        static PudFile ArmyMap()
        {
            var pud = AiTestHarness.TwoBaseMap();
            pud.Owner[1] = (byte)PudOwner.Human;
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 10, 14);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 11, 14);
            AiTestHarness.Place(pud, 0, UnitTypeId.Footman, 12, 14);
            return pud;
        }

        [Test]
        public void Wave_LaunchesAtMusterSize_ThenSleeps()
        {
            var sim = AiTestHarness.Boot(ArmyMap(), seed: 9);
            var ais = new List<AiPlayer> { new AiPlayer(0, AiBehavior.LandAttack) };

            var waveTicks = new List<int>();
            AiTestHarness.RunAiMatch(sim, ais, 1200,
                onCommand: (tick, cmd) =>
                {
                    if (cmd.Op == CommandOp.AttackMove && cmd.Player == 0)
                        waveTicks.Add(tick);
                });

            Assert.GreaterOrEqual(waveTicks.Count, 1,
                "three footmen meet the phase-0 wave size of 3");
            if (waveTicks.Count >= 2)
                Assert.GreaterOrEqual(waveTicks[1] - waveTicks[0],
                    AiScript.PostWaveSleepTicks,
                    "the post-wave sleep must gate the next wave");
        }

        [Test]
        public void Wave_TargetsTheEnemyHall()
        {
            var sim = AiTestHarness.Boot(ArmyMap(), seed: 9);
            var ais = new List<AiPlayer> { new AiPlayer(0, AiBehavior.LandAttack) };

            GameCommand? wave = null;
            AiTestHarness.RunAiMatch(sim, ais, 200,
                onCommand: (tick, cmd) =>
                {
                    if (cmd.Op == CommandOp.AttackMove && wave == null)
                        wave = cmd;
                });

            Assert.IsTrue(wave.HasValue, "a wave must launch");
            Assert.AreEqual((52, 52), ((int)wave.Value.TargetX, (int)wave.Value.TargetY),
                "the wave aims at the enemy hall");
            Assert.LessOrEqual((int)wave.Value.SelectionCount, GameCommand.MaxSelection);
        }

        [Test]
        public void TrainSubstitute_TrainsRangersNeverArchers()
        {
            var pud = AiTestHarness.TwoBaseMap();
            pud.Owner[1] = (byte)PudOwner.Human;
            pud.StartGold[0] = 5000;
            pud.StartLumber[0] = 3000;
            AiTestHarness.Place(pud, 0, UnitTypeId.HumanBarracks, 14, 16);
            AiTestHarness.Place(pud, 0, UnitTypeId.ElvenLumberMill, 4, 14);
            AiTestHarness.Place(pud, 0, UnitTypeId.Farm, 12, 4);
            AiTestHarness.Place(pud, 0, UnitTypeId.Farm, 4, 4);
            var sim = AiTestHarness.Boot(pud, seed: 9);
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.TrainRangers;

            var ai = new AiPlayer(0, AiBehavior.LandAttack);
            ai.ForcePhase(1); // phase 1 wants archers
            var trained = new List<ushort>();
            AiTestHarness.RunAiMatch(sim, new List<AiPlayer> { ai }, 3000,
                onCommand: (tick, cmd) =>
                {
                    if (cmd.Op == CommandOp.Train)
                        trained.Add(cmd.Param);
                });

            Assert.Contains((ushort)UnitTypeId.Ranger, trained,
                "the archer want must resolve to rangers");
            Assert.IsFalse(trained.Contains((ushort)UnitTypeId.Archer),
                "plain archers must never be requested once rangers are researched");
        }

        [Test]
        public void PassiveBehavior_EmitsNothing()
        {
            var sim = AiTestHarness.Boot(ArmyMap(), seed: 9);
            var ais = new List<AiPlayer> { new AiPlayer(0, AiBehavior.Passive) };
            int commands = 0;
            AiTestHarness.RunAiMatch(sim, ais, 2000,
                onCommand: (tick, cmd) => commands++);
            Assert.AreEqual(0, commands, "a passive slot sits inert");
        }
    }
}
