using System.Collections.Generic;
using NUnit.Framework;
using Craftwar.Net;

namespace Craftwar.Sim.Tests
{
    public class LockstepDriverTests
    {
        static GameCommand Cmd(byte player, CommandOp op, ushort param = 0) =>
            new GameCommand { Op = op, Player = player, Param = param };

        [Test]
        public void TickCommands_GroupedByPlayer_StableWithinPlayer()
        {
            var driver = new LocalLockstepDriver();
            // Interleaved submissions, as a frame with human + AIs produces.
            driver.SubmitLocalCommand(Cmd(2, CommandOp.Move));
            driver.SubmitLocalCommand(Cmd(0, CommandOp.Build, 1));
            driver.SubmitLocalCommand(Cmd(1, CommandOp.Train));
            driver.SubmitLocalCommand(Cmd(0, CommandOp.Move, 2));
            driver.SubmitLocalCommand(Cmd(2, CommandOp.Stop));

            var got = new List<GameCommand>();
            Assert.IsTrue(driver.TryGetTickCommands(0, got));

            var players = new byte[got.Count];
            for (int i = 0; i < got.Count; i++)
                players[i] = got[i].Player;
            Assert.AreEqual(new byte[] { 0, 0, 1, 2, 2 }, players);

            // Player 0's Build-then-Move order must survive (stability).
            Assert.AreEqual(CommandOp.Build, got[0].Op);
            Assert.AreEqual(CommandOp.Move, got[1].Op);
            // Player 2's Move-then-Stop likewise.
            Assert.AreEqual(CommandOp.Move, got[3].Op);
            Assert.AreEqual(CommandOp.Stop, got[4].Op);
        }

        [Test]
        public void TickCommands_DrainsPendingOnce()
        {
            var driver = new LocalLockstepDriver();
            driver.SubmitLocalCommand(Cmd(0, CommandOp.Move));
            var got = new List<GameCommand>();
            driver.TryGetTickCommands(0, got);
            Assert.AreEqual(1, got.Count);
            driver.TryGetTickCommands(1, got);
            Assert.AreEqual(0, got.Count);
        }

        // --- ReplayLockstepDriver ------------------------------------------------

        [Test]
        public void ReplayDriver_HandsCommandsBackAtTheirRecordedTick()
        {
            var replay = new Replay();
            replay.Record(0, Cmd(0, CommandOp.Move));
            replay.Record(3, Cmd(1, CommandOp.Train));
            replay.Record(3, Cmd(1, CommandOp.Stop));

            var driver = new ReplayLockstepDriver(replay);
            var got = new List<GameCommand>();

            Assert.IsTrue(driver.TryGetTickCommands(0, got));
            Assert.AreEqual(1, got.Count);
            Assert.IsTrue(driver.TryGetTickCommands(1, got));
            Assert.AreEqual(0, got.Count, "no commands recorded at tick 1");
            Assert.IsTrue(driver.TryGetTickCommands(2, got));
            Assert.AreEqual(0, got.Count);
            Assert.IsTrue(driver.TryGetTickCommands(3, got));
            Assert.AreEqual(2, got.Count, "both tick-3 commands arrive together");
            Assert.AreEqual(CommandOp.Train, got[0].Op, "recorded order preserved");
            Assert.IsTrue(driver.Finished);
        }

        [Test]
        public void ReplayDriver_StarvesThenResumesWithoutLosingCommands()
        {
            var replay = new Replay();
            replay.Record(0, Cmd(0, CommandOp.Move));

            var driver = new ReplayLockstepDriver(replay);
            driver.StarveFor(3);

            var got = new List<GameCommand>();
            for (int i = 0; i < 3; i++)
                Assert.IsFalse(driver.TryGetTickCommands(0, got), $"starved call {i}");

            Assert.IsTrue(driver.TryGetTickCommands(0, got), "resumes once the starve budget runs out");
            Assert.AreEqual(1, got.Count, "the withheld command is still delivered");
        }

        [Test]
        public void ReplayDriver_LocalInputIsIgnoredDuringPlayback()
        {
            var driver = new ReplayLockstepDriver(new Replay());
            driver.SubmitLocalCommand(Cmd(0, CommandOp.Surrender));
            var got = new List<GameCommand>();
            driver.TryGetTickCommands(0, got);
            Assert.AreEqual(0, got.Count, "playback is authoritative; live input must not leak in");
        }

        // --- DelayedLockstepDriver -----------------------------------------------

        [Test]
        public void DelayedDriver_HoldsCommandsForTheConfiguredDelay()
        {
            var driver = new DelayedLockstepDriver(delayTicks: 8);
            var got = new List<GameCommand>();

            driver.TryGetTickCommands(0, got);            // establishes "now"
            driver.SubmitLocalCommand(Cmd(0, CommandOp.Move));

            for (int t = 1; t < 8; t++)
            {
                driver.TryGetTickCommands(t, got);
                Assert.AreEqual(0, got.Count, $"still held at tick {t}");
            }

            driver.TryGetTickCommands(8, got);
            Assert.AreEqual(1, got.Count, "executes at submit tick + delay");
        }

        [Test]
        public void DelayedDriver_PreservesSubmissionOrderWithinAPlayer()
        {
            var driver = new DelayedLockstepDriver(delayTicks: 2);
            var got = new List<GameCommand>();
            driver.TryGetTickCommands(0, got);

            driver.SubmitLocalCommand(Cmd(0, CommandOp.Build, 1));
            driver.SubmitLocalCommand(Cmd(0, CommandOp.Move, 2));
            driver.SubmitLocalCommand(Cmd(1, CommandOp.Train));

            driver.TryGetTickCommands(2, got);
            Assert.AreEqual(3, got.Count);
            Assert.AreEqual(new byte[] { 0, 0, 1 }, new[] { got[0].Player, got[1].Player, got[2].Player });
            Assert.AreEqual(CommandOp.Build, got[0].Op, "a worker's Build then Move must not swap");
            Assert.AreEqual(CommandOp.Move, got[1].Op);
        }

        [Test]
        public void ReplayDriver_ReproducesFinalHash_ThroughTheDriverSeam()
        {
            // The end-to-end determinism check, run through the same seam the app
            // uses rather than by feeding the sim directly.
            var pud = AiTestHarness.TwoBaseMap();
            var live = AiTestHarness.Boot(pud, seed: 1234);
            var ais = AiTestHarness.CreateAis(live);
            var replay = new Replay { Seed = 1234 };
            AiTestHarness.RunAiMatch(live, ais, maxTicks: 3000, replay: replay);
            uint expected = live.State.ComputeHash();

            var playback = AiTestHarness.Boot(pud, seed: 1234);
            var driver = new ReplayLockstepDriver(replay);
            var bundle = new List<GameCommand>();
            // Starve once partway through: a withheld tick must delay the sim,
            // never skip or duplicate a command. Arm it exactly once — re-arming
            // it every iteration would hold the tick forever.
            bool starveArmed = false;
            int starvedCalls = 0;
            while (playback.State.Tick < live.State.Tick)
            {
                if (!starveArmed && playback.State.Tick == 1500)
                {
                    starveArmed = true;
                    driver.StarveFor(2);
                }
                if (!driver.TryGetTickCommands(playback.State.Tick, bundle))
                {
                    starvedCalls++;
                    continue;
                }
                playback.Advance(bundle);
            }
            Assert.AreEqual(2, starvedCalls, "the starve budget was actually exercised");

            Assert.AreEqual(expected, playback.State.ComputeHash(),
                "replay through the driver seam must reproduce the live run");
        }
    }
}
