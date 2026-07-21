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
    }
}
