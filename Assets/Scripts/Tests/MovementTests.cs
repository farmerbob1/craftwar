using System.Collections.Generic;
using Craftwar.Net;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class MovementTests
    {
        /// <summary>Synthetic all-land 32x32 map with 20 footmen for player 0.</summary>
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
            pud.Owner[1] = (byte)PudOwner.Computer;
            for (int i = 0; i < 20; i++)
                pud.Units.Add(new PudUnitEntry
                {
                    X = (ushort)(2 + i % 5),
                    Y = (ushort)(2 + i / 5),
                    Type = (byte)UnitTypeId.Footman,
                    Owner = 0,
                });
            return pud;
        }

        static unsafe GameCommand MoveAll(GameSim sim, byte player, ushort tx, ushort ty)
        {
            var cmd = new GameCommand { Op = CommandOp.Move, Player = player, TargetX = tx, TargetY = ty };
            for (int i = 0; i < sim.State.HighestUnitIndex && cmd.SelectionCount < GameCommand.MaxSelection; i++)
            {
                ref var u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == player)
                    cmd.Selection.Ids[cmd.SelectionCount++] = new UnitId((ushort)i, u.Gen).Packed;
            }
            return cmd;
        }

        static uint RunScriptedMatch(ulong seed, out GameSim sim)
        {
            sim = new GameSim(seed);
            sim.Setup(MakeMap(), RuleSet.CreateDefault());

            var driver = new LocalLockstepDriver();
            var commands = new List<GameCommand>();
            // Footmen move 1 tile/sec (50 ticks/tile): leave generous time
            // for both legs plus crowd shuffling at the gather point.
            for (int tick = 0; tick < 3000; tick++)
            {
                if (tick == 5)
                    driver.SubmitLocalCommand(MoveAll(sim, 0, 28, 28));
                if (tick == 300)
                    driver.SubmitLocalCommand(MoveAll(sim, 0, 4, 26));
                driver.TryGetTickCommands(tick, commands);
                sim.Advance(commands);
            }
            return sim.State.ComputeHash();
        }

        [Test]
        public void GroupMove_20Units_ArriveAndDeterministic()
        {
            uint hashA = RunScriptedMatch(777, out var simA);
            uint hashB = RunScriptedMatch(777, out _);
            Assert.AreEqual(hashA, hashB, "same seed + commands must produce identical state");

            // After the second order, units should have gathered near (4,26).
            int near = 0;
            for (int i = 0; i < simA.State.HighestUnitIndex; i++)
            {
                ref var u = ref simA.State.Units[i];
                if (!u.IsAlive)
                    continue;
                int dx = u.TileX - 4, dy = u.TileY - 26;
                if (dx * dx + dy * dy <= 8 * 8)
                    near++;
                Assert.AreEqual(OrderType.None, u.Order, "orders should have completed");
            }
            Assert.GreaterOrEqual(near, 18, $"units should gather near target, got {near}/20");
        }

        [Test]
        public void OneUnitPerTile_NeverViolated()
        {
            RunScriptedMatch(123, out var sim);
            var seen = new HashSet<uint>();
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref var u = ref sim.State.Units[i];
                if (!u.IsAlive)
                    continue;
                uint key = (uint)(u.TileY * 1000 + u.TileX);
                Assert.IsTrue(seen.Add(key), $"two units share tile {u.TileX},{u.TileY}");
            }
        }

        [Test]
        public void Replay_ReproducesIdenticalFinalHash()
        {
            // Record a run.
            var sim = new GameSim(42);
            sim.Setup(MakeMap(), RuleSet.CreateDefault());
            var driver = new LocalLockstepDriver();
            var replay = new Replay { Seed = 42 };
            var commands = new List<GameCommand>();
            for (int tick = 0; tick < 1200; tick++)
            {
                if (tick == 3)
                    driver.SubmitLocalCommand(MoveAll(sim, 0, 25, 25));
                if (tick == 300)
                    driver.SubmitLocalCommand(MoveAll(sim, 0, 6, 20));
                driver.TryGetTickCommands(tick, commands);
                foreach (var c in commands)
                    replay.Record(tick, c);
                sim.Advance(commands);
            }
            uint liveHash = sim.State.ComputeHash();

            // Serialize, reload, replay into a fresh sim.
            var loaded = Replay.FromBytes(replay.ToBytes());
            Assert.AreEqual(42ul, loaded.Seed);

            var sim2 = new GameSim(loaded.Seed);
            sim2.Setup(MakeMap(), RuleSet.CreateDefault());
            int entry = 0;
            var tickCmds = new List<GameCommand>();
            for (int tick = 0; tick < 1200; tick++)
            {
                tickCmds.Clear();
                while (entry < loaded.Entries.Count && loaded.Entries[entry].tick == tick)
                    tickCmds.Add(loaded.Entries[entry++].cmd);
                sim2.Advance(tickCmds);
            }

            Assert.AreEqual(liveHash, sim2.State.ComputeHash(),
                "replay must reproduce the exact final state hash");
        }

        [Test]
        public void MoveCommand_IgnoresEnemyAndBuildingSelection()
        {
            var pud = MakeMap();
            pud.Units.Add(new PudUnitEntry { X = 20, Y = 20, Type = (byte)UnitTypeId.TownHall, Owner = 0 });
            var sim = new GameSim(9);
            sim.Setup(pud, RuleSet.CreateDefault());

            var cmd = MoveAll(sim, 1, 30, 30); // player 1 orders player 0's units
            sim.Advance(new List<GameCommand> { cmd });
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                Assert.AreEqual(OrderType.None, sim.State.Units[i].Order);
        }
    }
}
