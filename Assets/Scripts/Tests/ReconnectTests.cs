using System.Collections.Generic;
using Craftwar.Net;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The headline reconnect proof: a dropped client's seat is picked up by
    /// a FRESH connection via the rejoin handshake (RejoinRequest → snapshot
    /// chunks → resumed driver), and the resulting sim stays bit-identical to
    /// the host's, which never stopped running. This is what M10 phase 6's
    /// "still to do" reconnect item, and M11 phase 0.3, were both about —
    /// SimSerializer alone only proves a snapshot round-trips; this proves a
    /// second peer can rejoin the SAME live match from one.
    /// </summary>
    public class ReconnectTests
    {
        const int TicksPerTurn = 4;
        const int Delay = 2;

        [Test]
        public void ARejoiningPeer_CatchesUpAndStaysBitIdentical()
        {
            var pud = AiTestHarness.TwoBaseMap();
            var network = new LoopbackNetwork();
            var hostSim = AiTestHarness.Boot(pud, seed: 4242);
            var clientSim = AiTestHarness.Boot(pud, seed: 4242);
            var relay = new TurnRelay(new byte[] { 0, 1 });

            var hostPeer = network.CreatePeer();
            var clientPeer = network.CreatePeer();

            var host = new HostTurnExchange(hostPeer, relay, localSlot: 0);
            host.AssignSlot(clientPeer.LocalPeerId, 1);
            var client = new ClientTurnExchange(clientPeer, localSlot: 1);

            var hostDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, host);
            var clientDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 1, client);

            bool Step(int tick, List<GameCommand> hostBundle, List<GameCommand> clientBundle)
            {
                host.Poll();
                client.Poll();
                bool hostReady = false, clientReady = false;
                for (int spin = 0; spin < 16 && !(hostReady && clientReady); spin++)
                {
                    host.Poll();
                    client.Poll();
                    if (!hostReady) hostReady = hostDriver.TryGetTickCommands(tick, hostBundle);
                    if (!clientReady) clientReady = clientDriver.TryGetTickCommands(tick, clientBundle);
                }
                if (!hostReady || !clientReady)
                    return false;
                hostSim.Advance(hostBundle);
                clientSim.Advance(clientBundle);
                return true;
            }

            var hb = new List<GameCommand>();
            var cb = new List<GameCommand>();
            for (int tick = 0; tick < 200; tick++)
            {
                Assert.IsTrue(Step(tick, hb, cb), $"warm-up stalled at tick {tick}");
                Assert.AreEqual(hostSim.State.ComputeHash(), clientSim.State.ComputeHash());
            }

            // The client drops. The host keeps running on its own, same as
            // AfterAClientDrops_TheMatchKeepsRunningInsteadOfFreezing.
            network.Disconnect(clientPeer.LocalPeerId);
            host.Poll();
            Assert.IsTrue(relay.IsSubstituted(1));

            var hostOnlyBundle = new List<GameCommand>();
            for (int tick = 200; tick < 260; tick++)
            {
                host.Poll();
                if (hostDriver.TryGetTickCommands(tick, hostOnlyBundle))
                    hostSim.Advance(hostOnlyBundle);
            }
            Assert.AreEqual(0, hostSim.State.Tick % TicksPerTurn,
                "test setup: snapshot must be taken at a turn boundary");

            // A fresh connection claims seat 1. The app layer (simulated here
            // directly, since GameLoopRunner is not testable outside Unity)
            // validates the claim and hands back a snapshot.
            byte[] rawSnapshot = null;
            host.RejoinRequested += attempt =>
            {
                Assert.AreEqual(1, attempt.ClaimedSlot);
                Assert.IsTrue(host.IsSubstituted(attempt.ClaimedSlot));
                rawSnapshot = SimSerializer.Save(hostSim);
                host.AcceptRejoin(attempt.FromPeerId, attempt.ClaimedSlot,
                    resumeTurn: hostSim.State.Tick / TicksPerTurn,
                    ticksPerTurn: TicksPerTurn, inputDelayTurns: Delay,
                    pausingSlots: null, rawSnapshot: rawSnapshot);
            };

            var rejoinPeer = network.CreatePeer();
            var reconnect = new ReconnectClient(rejoinPeer, default, claimedSlot: 1, "rejoined");

            bool ready = false;
            for (int spin = 0; spin < 64 && !ready; spin++)
            {
                host.Poll();
                reconnect.Poll();
                ready = reconnect.Ready;
            }
            Assert.IsTrue(ready, "the rejoining peer must finish receiving the snapshot");

            Assert.IsTrue(reconnect.TryComplete(out byte[] snapshotBytes, out byte yourSlot,
                out int resumeTurn, out byte ticksPerTurn, out byte inputDelayTurns,
                out bool[] pausingSlots));
            Assert.AreEqual(1, yourSlot);
            Assert.AreEqual(TicksPerTurn, ticksPerTurn);
            Assert.AreEqual(Delay, inputDelayTurns);

            var rejoinedSim = SimSerializer.Load(snapshotBytes);
            Assert.AreEqual(hostSim.State.ComputeHash(), rejoinedSim.State.ComputeHash(),
                "the received snapshot must be bit-identical to the host's state");

            var rejoinedExchange = new ClientTurnExchange(rejoinPeer, localSlot: 1);
            // Turns already decided between the snapshot's tick and the
            // moment the rejoin was accepted (input delay lets a commit run
            // ahead of the sim that produced it) never arrive via the normal
            // broadcast — it is fire-and-forget to whoever was already
            // connected. AcceptRejoin unicasts them; ReconnectClient captured
            // them since it was still the one polling this connection then.
            foreach (var pair in reconnect.BackfilledCommits)
                rejoinedExchange.SeedCommit(pair.Key, pair.Value);
            var rejoinedDriver = new TurnLockstepDriver(ticksPerTurn, inputDelayTurns, yourSlot,
                rejoinedExchange, resumeTurn, pausingSlots);

            // Both sides must keep agreeing from here — including the
            // rejoined peer actually issuing orders, not just idling.
            var hb2 = new List<GameCommand>();
            var rb2 = new List<GameCommand>();
            int matchedTurns = 0;
            for (int tick = 0; tick < 400; tick++)
            {
                if (tick % 37 == 0)
                    rejoinedDriver.SubmitLocalCommand(Move(rejoinedSim, 1, (ushort)(40 + tick % 8), 40));

                host.Poll();
                rejoinedExchange.Poll();
                bool hostReady = false, rejoinedReady = false;
                for (int spin = 0; spin < 16 && !(hostReady && rejoinedReady); spin++)
                {
                    host.Poll();
                    rejoinedExchange.Poll();
                    if (!hostReady) hostReady = hostDriver.TryGetTickCommands(1_000_000 + tick, hb2);
                    if (!rejoinedReady) rejoinedReady = rejoinedDriver.TryGetTickCommands(tick, rb2);
                }
                Assert.IsTrue(hostReady && rejoinedReady, $"stalled resuming at tick {tick}");
                hostSim.Advance(hb2);
                rejoinedSim.Advance(rb2);

                if (hostSim.State.Tick % TicksPerTurn == 0)
                {
                    Assert.AreEqual(hostSim.State.ComputeHash(), rejoinedSim.State.ComputeHash(),
                        $"diverged at tick {hostSim.State.Tick}");
                    matchedTurns++;
                }
            }

            Assert.Greater(matchedTurns, 50, "the match must actually keep progressing after reconnect");
            Assert.IsNull(hostSim.State.VerifyChecksums());
            Assert.IsNull(rejoinedSim.State.VerifyChecksums());
        }

        static unsafe GameCommand Move(GameSim sim, byte player, ushort x, ushort y)
        {
            var cmd = new GameCommand { Op = CommandOp.Move, Player = player, TargetX = x, TargetY = y };
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == player && (u.Flags & UnitFlags.Building) == 0)
                {
                    cmd.SelectionCount = 1;
                    cmd.Selection.Ids[0] = new UnitId((ushort)i, u.Gen).Packed;
                    break;
                }
            }
            return cmd;
        }
    }
}
