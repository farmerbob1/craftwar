using System.Collections.Generic;
using Craftwar.Net;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The bug this guards: a Computer player the HOST runs locally (not a
    /// remote client's seat) submits through the same TurnLockstepDriver as
    /// the host's own human input — GameLoopRunner.CreateAis()'s whole design
    /// is that a computer player's commands "travel the wire inside the
    /// host's own input block". Before HostTurnExchange.AddLocalSlot existed,
    /// HostTurnExchange.SendInput tagged the ENTIRE local bundle with just
    /// _localSlot, so TurnRelay.SubmitInput's per-command ownership filter
    /// silently discarded every command belonging to the AI's slot, AND that
    /// slot never received its own SubmitInput call — so its turn bucket
    /// could never fill, freezing the whole match until the (unrelated)
    /// drop-grace timer eventually force-substituted it ~10s later.
    /// </summary>
    public class HostLocalAiExchangeTests
    {
        const int TicksPerTurn = 4;
        const int Delay = 2;

        [Test]
        public void ALocallyRunComputerPlayers_CommandsReachItsOwnTurnBucket_WithoutSubstitution()
        {
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var network = new LoopbackNetwork();
            var hostPeer = network.CreatePeer();
            var host = new HostTurnExchange(hostPeer, relay, localSlot: 0);
            host.AddLocalSlot(1); // slot 1 is a computer player the host runs itself

            var driver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, host);
            var bundle = new List<GameCommand>();

            // Slot 0 (human) and slot 1 (computer) both submit through the
            // SAME driver, exactly like GameLoopRunner's Update loop does for
            // the human and every CreateAis()-produced AiPlayer.
            driver.SubmitLocalCommand(new GameCommand { Op = CommandOp.Move, Player = 0, TargetX = 5, TargetY = 5 });
            driver.SubmitLocalCommand(new GameCommand { Op = CommandOp.Move, Player = 1, TargetX = 9, TargetY = 9 });

            bool everFroze = false;
            for (int tick = 0; tick < 40 && !everFroze; tick++)
            {
                host.Poll();
                driver.TryGetTickCommands(tick, bundle);
                everFroze = relay.HighestCommittedTurn >= 0;
            }

            Assert.IsTrue(everFroze, "a turn must freeze without any drop-grace substitution kicking in");
            Assert.IsFalse(relay.IsSubstituted(1),
                "the AI's own slot must never need substituting — it has a real local source of input");

            bool sawSlot0 = false, sawSlot1 = false;
            var committed = new List<GameCommand>();
            for (int turn = 0; turn <= relay.HighestCommittedTurn; turn++)
            {
                committed.Clear();
                if (!relay.TryGetCommitted(turn, committed))
                    continue;
                foreach (var c in committed)
                {
                    if (c.Player == 0 && c.Op == CommandOp.Move) sawSlot0 = true;
                    if (c.Player == 1 && c.Op == CommandOp.Move) sawSlot1 = true;
                }
            }

            Assert.IsTrue(sawSlot0, "the human's own command must still arrive");
            Assert.IsTrue(sawSlot1, "the locally-run computer player's command must arrive — this is the bug");
        }

        [Test]
        public void WithoutAddLocalSlot_TheAiSeatStallsForever_UntilSubstituted()
        {
            // Documents the OLD broken behavior for the same setup, minus the
            // AddLocalSlot call — proves the fix is what actually changed it,
            // not some other difference in the harness.
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var network = new LoopbackNetwork();
            var hostPeer = network.CreatePeer();
            var host = new HostTurnExchange(hostPeer, relay, localSlot: 0);
            // No AddLocalSlot(1) here.

            var driver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, host);
            var bundle = new List<GameCommand>();
            driver.SubmitLocalCommand(new GameCommand { Op = CommandOp.Move, Player = 0, TargetX = 5, TargetY = 5 });
            driver.SubmitLocalCommand(new GameCommand { Op = CommandOp.Move, Player = 1, TargetX = 9, TargetY = 9 });

            var blocking = new List<byte>();
            bool blockedOnSlot1 = false;
            for (int tick = 0; tick < 40; tick++)
            {
                host.Poll();
                driver.TryGetTickCommands(tick, bundle);
                if (host.TryGetOldestBlockedTurn(out _, blocking) && blocking.Contains((byte)1))
                    blockedOnSlot1 = true;
            }

            Assert.IsTrue(blockedOnSlot1, "without registering the AI's slot, it stalls the match");
            Assert.AreEqual(-1, relay.HighestCommittedTurn, "no turn can freeze while slot 1 is unfulfilled");
        }

        [Test]
        public void AddLocalSlot_AfterTheDriverAlreadyExists_StillStallsOnTurnZero()
        {
            // The second bug: registering the AI's slot AFTER constructing
            // TurnLockstepDriver (as a post-hoc GameLoopRunner.CreateAis()
            // call did) is too late — the driver's constructor bootstraps
            // turn 0's submission immediately, before AddLocalSlot ever runs,
            // so turn 0 specifically omits slot 1 forever regardless of any
            // later registration. This is why the AddLocalSlot fix alone
            // didn't resolve the reported bug: NetSession.CreateDriver must
            // learn the AI's slots BEFORE building the driver, not after.
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var network = new LoopbackNetwork();
            var hostPeer = network.CreatePeer();
            var host = new HostTurnExchange(hostPeer, relay, localSlot: 0);

            // Driver constructed — and its constructor's bootstrap SendInput
            // for turn 0 already fired — BEFORE AddLocalSlot(1) runs.
            var driver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, host);
            host.AddLocalSlot(1);

            var bundle = new List<GameCommand>();
            driver.SubmitLocalCommand(new GameCommand { Op = CommandOp.Move, Player = 0, TargetX = 5, TargetY = 5 });
            driver.SubmitLocalCommand(new GameCommand { Op = CommandOp.Move, Player = 1, TargetX = 9, TargetY = 9 });

            var blocking = new List<byte>();
            bool blockedOnSlot1 = false;
            for (int tick = 0; tick < 40; tick++)
            {
                host.Poll();
                driver.TryGetTickCommands(tick, bundle);
                if (host.TryGetOldestBlockedTurn(out _, blocking) && blocking.Contains((byte)1))
                    blockedOnSlot1 = true;
            }

            Assert.IsTrue(blockedOnSlot1,
                "turn 0's bootstrap already happened without slot 1 — a late AddLocalSlot cannot retroactively fix it");
        }

        [Test]
        public void KnowingTheAiSlot_BeforeConstructingTheDriver_NeverStalls()
        {
            // The actual fix: NetSession.CreateDriver now takes the local
            // computer slots and calls AddLocalSlot BEFORE constructing the
            // TurnLockstepDriver, so its bootstrap submission for turn 0
            // already includes every locally-run slot. This is the ordering
            // GameLoopRunner.BuildSim now uses.
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var network = new LoopbackNetwork();
            var hostPeer = network.CreatePeer();
            var host = new HostTurnExchange(hostPeer, relay, localSlot: 0);
            host.AddLocalSlot(1); // known up front, exactly like localComputerSlots

            var driver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, host);
            var bundle = new List<GameCommand>();
            driver.SubmitLocalCommand(new GameCommand { Op = CommandOp.Move, Player = 1, TargetX = 9, TargetY = 9 });

            var blocking = new List<byte>();
            for (int tick = 0; tick < 40; tick++)
            {
                host.Poll();
                driver.TryGetTickCommands(tick, bundle);
                Assert.IsFalse(host.TryGetOldestBlockedTurn(out _, blocking) && blocking.Contains((byte)1),
                    $"slot 1 must never block at tick {tick} when it was registered before the driver existed");
            }
            Assert.IsFalse(relay.IsSubstituted(1), "no substitution should ever have been needed");
        }
    }
}
