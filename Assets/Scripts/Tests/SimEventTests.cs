using System.Collections.Generic;
using System.Text;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The SimEvent channel is derived output, exactly like TileChanges. These
    /// tests pin the two properties that make that claim true: events are
    /// reproducible from (map, seed, commands), and they never influence the
    /// state hash.
    /// </summary>
    public class SimEventTests
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
            pud.Units.Add(new PudUnitEntry { X = 10, Y = 10, Type = (byte)UnitTypeId.TownHall, Owner = 0 });
            pud.Units.Add(new PudUnitEntry { X = 16, Y = 12, Type = (byte)UnitTypeId.Peasant, Owner = 0 });
            pud.Units.Add(new PudUnitEntry { X = 4, Y = 4, Type = (byte)UnitTypeId.Farm, Owner = 0 });
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

        static unsafe GameCommand Cmd(GameSim sim, CommandOp op, int unitSlot, ushort param)
        {
            var cmd = new GameCommand
            {
                Op = op,
                Player = 0,
                Param = param,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId((ushort)unitSlot, sim.State.Units[unitSlot].Gen).Packed;
            return cmd;
        }

        /// <summary>Every event emitted over a run, flattened for comparison.</summary>
        static string RunAndTrace(ulong seed, int ticks, out uint finalHash)
        {
            var sim = Boot(BaseMap(), seed);
            var sb = new StringBuilder();
            var none = new List<GameCommand>();

            int hall = SlotOf(sim, UnitTypeId.TownHall);
            var train = Cmd(sim, CommandOp.Train, hall, (ushort)UnitTypeId.Peasant);

            for (int t = 0; t < ticks; t++)
            {
                sim.Advance(t == 0 ? new List<GameCommand> { train } : none);
                foreach (var e in sim.State.Events)
                    sb.Append(t).Append(':').Append(e.Kind).Append(',')
                      .Append(e.Player).Append(',').Append(e.A).Append(',')
                      .Append(e.B).Append(',').Append(e.UnitPacked).Append(';');
            }
            finalHash = sim.State.ComputeHash();
            return sb.ToString();
        }

        [Test]
        public void SameScenarioProducesIdenticalEventSequence()
        {
            string a = RunAndTrace(7, 400, out uint hashA);
            string b = RunAndTrace(7, 400, out uint hashB);
            Assert.AreEqual(a, b, "event sequence diverged between identical runs");
            Assert.AreEqual(hashA, hashB, "state hash diverged between identical runs");
            Assert.IsNotEmpty(a, "scenario emitted no events at all — test is not exercising the channel");
        }

        [Test]
        public void EventsDoNotAffectStateHash()
        {
            var sim = Boot(BaseMap());
            var none = new List<GameCommand>();
            int hall = SlotOf(sim, UnitTypeId.TownHall);
            sim.Advance(new List<GameCommand> { Cmd(sim, CommandOp.Train, hall, (ushort)UnitTypeId.Peasant) });

            for (int t = 0; t < 200; t++)
            {
                sim.Advance(none);
                uint before = sim.State.ComputeHash();
                // Injecting junk into the channel must be invisible to the hash.
                sim.State.Events.Add(new SimEvent
                {
                    Kind = SimEventKind.UnderAttack,
                    Player = 3,
                    A = 111,
                    B = 222,
                    UnitPacked = 333,
                });
                Assert.AreEqual(before, sim.State.ComputeHash(),
                    "SimEvents leaked into the state hash");
            }
        }

        [Test]
        public void EventsAreClearedEachTick()
        {
            var sim = Boot(BaseMap());
            var none = new List<GameCommand>();
            int hall = SlotOf(sim, UnitTypeId.TownHall);
            sim.Advance(new List<GameCommand> { Cmd(sim, CommandOp.Train, hall, (ushort)UnitTypeId.Peasant) });
            sim.State.Events.Add(new SimEvent { Kind = SimEventKind.UnderAttack });
            sim.Advance(none);
            foreach (var e in sim.State.Events)
                Assert.AreNotEqual(SimEventKind.UnderAttack, e.Kind,
                    "stale event survived into the next tick");
        }

        [Test]
        public void DeniedTrainWithNoGoldEmitsExactlyOneNotEnoughGold()
        {
            var pud = BaseMap();
            pud.StartGold[0] = 0;
            pud.StartLumber[0] = 0;
            var sim = Boot(pud);

            int hall = SlotOf(sim, UnitTypeId.TownHall);
            sim.Advance(new List<GameCommand> { Cmd(sim, CommandOp.Train, hall, (ushort)UnitTypeId.Peasant) });

            int denials = 0;
            foreach (var e in sim.State.Events)
                if (e.Kind == SimEventKind.CommandDenied)
                {
                    denials++;
                    Assert.AreEqual(DenyReason.NotEnoughGold, (DenyReason)e.A);
                    Assert.AreEqual(0, e.Player);
                }
            Assert.AreEqual(1, denials, "expected exactly one CommandDenied for the whole command");
        }

        [Test]
        public void AffordableTrainEmitsNoDenial()
        {
            var sim = Boot(BaseMap());
            int hall = SlotOf(sim, UnitTypeId.TownHall);
            sim.Advance(new List<GameCommand> { Cmd(sim, CommandOp.Train, hall, (ushort)UnitTypeId.Peasant) });
            foreach (var e in sim.State.Events)
                Assert.AreNotEqual(SimEventKind.CommandDenied, e.Kind);
        }

        [Test]
        public void TrainCompletionEmitsTrainComplete()
        {
            var sim = Boot(BaseMap());
            var none = new List<GameCommand>();
            int hall = SlotOf(sim, UnitTypeId.TownHall);
            sim.Advance(new List<GameCommand> { Cmd(sim, CommandOp.Train, hall, (ushort)UnitTypeId.Peasant) });

            bool sawTrainComplete = false;
            for (int t = 0; t < 2000 && !sawTrainComplete; t++)
            {
                sim.Advance(none);
                foreach (var e in sim.State.Events)
                    if (e.Kind == SimEventKind.TrainComplete && e.B == (ushort)UnitTypeId.Peasant)
                        sawTrainComplete = true;
            }
            Assert.IsTrue(sawTrainComplete, "training a peasant never reported completion");
        }
    }
}
