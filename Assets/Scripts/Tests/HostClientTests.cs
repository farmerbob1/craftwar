using System.Collections.Generic;
using Craftwar.Net;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The full host/client protocol over an in-memory network: two independent
    /// sims, real serialization, real turn agreement. Everything a LAN match
    /// runs except the socket itself.
    /// </summary>
    public class HostClientTests
    {
        const int TicksPerTurn = 4;
        const int Delay = 2;

        sealed class Fixture
        {
            public LoopbackNetwork Network;
            public GameSim HostSim, ClientSim;
            public TurnLockstepDriver HostDriver, ClientDriver;
            public HostTurnExchange Host;
            public ClientTurnExchange Client;
            public TurnRelay Relay;
            public readonly List<GameCommand> HostBundle = new List<GameCommand>();
            public readonly List<GameCommand> ClientBundle = new List<GameCommand>();

            /// <summary>Advance both peers one tick, pumping until each has its
            /// bundle. Returns false if either stayed starved.</summary>
            public bool Step(int tick)
            {
                if (tick % TicksPerTurn == 0)
                {
                    HostDriver.RecordTurnHash(tick / TicksPerTurn, HostSim.State.ComputeHash());
                    ClientDriver.RecordTurnHash(tick / TicksPerTurn, ClientSim.State.ComputeHash());
                }

                bool hostReady = false, clientReady = false;
                for (int spin = 0; spin < 16 && !(hostReady && clientReady); spin++)
                {
                    Host.Poll();
                    Client.Poll();
                    if (!hostReady)
                        hostReady = HostDriver.TryGetTickCommands(tick, HostBundle);
                    if (!clientReady)
                        clientReady = ClientDriver.TryGetTickCommands(tick, ClientBundle);
                }
                if (!hostReady || !clientReady)
                    return false;

                HostSim.Advance(HostBundle);
                ClientSim.Advance(ClientBundle);
                return true;
            }
        }

        static Fixture Build(ulong seed = 909)
        {
            var pud = AiTestHarness.TwoBaseMap();
            var f = new Fixture
            {
                Network = new LoopbackNetwork(),
                HostSim = AiTestHarness.Boot(pud, seed),
                ClientSim = AiTestHarness.Boot(pud, seed),
                Relay = new TurnRelay(new byte[] { 0, 1 }),
            };
            var hostPeer = f.Network.CreatePeer();
            var clientPeer = f.Network.CreatePeer();

            f.Host = new HostTurnExchange(hostPeer, f.Relay, localSlot: 0);
            f.Host.AssignSlot(clientPeer.LocalPeerId, 1);
            f.Client = new ClientTurnExchange(clientPeer, localSlot: 1);

            f.HostDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, f.Host);
            f.ClientDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 1, f.Client);
            return f;
        }

        [Test]
        public void HostAndClient_StayBitIdentical_WhileBothPlay()
        {
            var f = Build();

            for (int tick = 0; tick < 1200; tick++)
            {
                if (tick % 89 == 0)
                    f.HostDriver.SubmitLocalCommand(Move(f.HostSim, 0, (ushort)(12 + tick % 10), 14));
                if (tick % 113 == 0)
                    f.ClientDriver.SubmitLocalCommand(Move(f.ClientSim, 1, (ushort)(42 + tick % 10), 42));

                Assert.IsTrue(f.Step(tick), $"a peer starved at tick {tick}");
                Assert.AreEqual(f.HostSim.State.ComputeHash(), f.ClientSim.State.ComputeHash(),
                    $"host and client diverged at tick {tick}");
            }

            Assert.IsNull(f.HostSim.State.VerifyChecksums());
            Assert.IsNull(f.ClientSim.State.VerifyChecksums());
            Assert.Greater(f.HostSim.State.Tick, 1000, "the match actually ran");
        }

        [Test]
        public void ClientOrders_ReachTheHostAndAffectItsWorld()
        {
            // Not just "the hashes match" — the client's intent has to actually
            // arrive. A protocol that dropped every command would keep both
            // worlds identical and prove nothing.
            var f = Build();
            for (int tick = 0; tick < 40; tick++)
                Assert.IsTrue(f.Step(tick));

            int slot = FindMovableUnit(f.HostSim, 1);
            Assert.GreaterOrEqual(slot, 0, "slot 1 should own a movable unit");
            ushort startX = f.HostSim.State.Units[slot].TileX;
            ushort startY = f.HostSim.State.Units[slot].TileY;
            ushort targetX = (ushort)(startX + 6);
            ushort targetY = startY;

            f.ClientDriver.SubmitLocalCommand(Move(f.ClientSim, 1, targetX, targetY));

            // Watch the HOST's copy of the unit accept the order. Asserting on
            // the order rather than on arrival keeps this a test of the protocol
            // instead of a test of pathfinding.
            bool hostAcceptedOrder = false;
            bool hostUnitMoved = false;
            for (int tick = 40; tick < 400; tick++)
            {
                Assert.IsTrue(f.Step(tick));
                ref Unit hostUnit = ref f.HostSim.State.Units[slot];
                if (hostUnit.OrderX == targetX && hostUnit.OrderY == targetY)
                    hostAcceptedOrder = true;
                if (hostUnit.TileX != startX || hostUnit.TileY != startY)
                    hostUnitMoved = true;
            }

            Assert.IsTrue(hostAcceptedOrder,
                "the client's order must arrive and be applied in the HOST's simulation");
            Assert.IsTrue(hostUnitMoved, "and the unit must actually act on it");
            Assert.AreEqual(f.HostSim.State.ComputeHash(), f.ClientSim.State.ComputeHash());
        }

        [Test]
        public void ACommandForwardedForAnotherSlot_IsRejectedByTheHost()
        {
            var f = Build();
            for (int tick = 0; tick < 8; tick++)
                Assert.IsTrue(f.Step(tick));

            // The client speaks for slot 0 — the host's own seat.
            var forged = new GameCommand { Op = CommandOp.Surrender, Player = 0 };
            f.ClientDriver.SubmitLocalCommand(forged);

            for (int tick = 8; tick < 60; tick++)
                Assert.IsTrue(f.Step(tick));

            Assert.AreEqual(PlayerOutcome.Playing, f.HostSim.State.Players[0].Outcome,
                "a peer cannot surrender somebody else's slot");
        }

        [Test]
        public void DivergentState_IsDetectedAndHaltsBothPeers()
        {
            var f = Build();
            for (int tick = 0; tick < 20; tick++)
                Assert.IsTrue(f.Step(tick));

            DesyncReport? hostSaw = null, clientSaw = null;
            f.Host.Desynced += r => hostSaw = r;
            f.Client.Desynced += r => clientSaw = r;

            // Corrupt the client's world behind the protocol's back — exactly
            // what a real determinism bug looks like from the outside.
            f.ClientSim.State.Players[1].Gold += 1;

            for (int tick = 20; tick < 60 && hostSaw == null; tick++)
                f.Step(tick);

            Assert.IsTrue(hostSaw.HasValue, "the host must notice the hashes disagree");
            Assert.AreEqual(NetStatus.Desynced, f.Host.Status);

            // And the halt must reach the client, or it plays on alone.
            for (int i = 0; i < 4; i++)
            {
                f.Host.Poll();
                f.Client.Poll();
            }
            Assert.IsTrue(clientSaw.HasValue, "the client must be told");
            Assert.AreEqual(NetStatus.Desynced, f.Client.Status);
        }

        [Test]
        public void LosingTheHost_MarksTheClientDisconnected()
        {
            var f = Build();
            for (int tick = 0; tick < 12; tick++)
                Assert.IsTrue(f.Step(tick));

            f.Network.Disconnect(0);
            f.Client.Poll();

            Assert.AreEqual(NetStatus.Disconnected, f.Client.Status);
        }

        [Test]
        public void ADroppedClient_IsReportedToTheHostByItsSlot()
        {
            var f = Build();
            for (int tick = 0; tick < 12; tick++)
                Assert.IsTrue(f.Step(tick));

            byte dropped = 255;
            f.Host.PeerDropped += slot => dropped = slot;
            f.Network.Disconnect(1);
            f.Host.Poll();

            Assert.AreEqual(1, dropped, "the host learns which SEAT went away, not just which socket");
        }

        [Test]
        public void AfterAClientDrops_TheMatchKeepsRunningInsteadOfFreezing()
        {
            // The failure this prevents is total: a lockstep turn needs every
            // participant's input, so a vanished peer stalls everyone forever
            // unless the host starts speaking for its seat.
            var f = Build();
            for (int tick = 0; tick < 40; tick++)
                Assert.IsTrue(f.Step(tick));

            byte dropped = 255;
            f.Host.PeerDropped += slot => dropped = slot;
            f.Network.Disconnect(1);
            f.Host.Poll();
            Assert.AreEqual(1, dropped);
            Assert.IsTrue(f.Relay.IsSubstituted(1), "the host now speaks for the empty seat");

            // The host must be able to keep simulating on its own.
            int hostTicks = 0;
            var bundle = new List<GameCommand>();
            for (int tick = 40; tick < 200; tick++)
            {
                f.Host.Poll();
                if (f.HostDriver.TryGetTickCommands(tick, bundle))
                {
                    f.HostSim.Advance(bundle);
                    hostTicks++;
                }
            }

            Assert.Greater(hostTicks, 100,
                "the host should keep advancing after the drop, not stall");
            Assert.IsNull(f.HostSim.State.VerifyChecksums());
        }

        [Test]
        public void ADroppedSlotsUnits_RemainInTheWorld()
        {
            // Substitution is about who supplies input, not about deleting the
            // player: their buildings are still standing and still a target.
            var f = Build();
            for (int tick = 0; tick < 20; tick++)
                Assert.IsTrue(f.Step(tick));

            int before = CountUnits(f.HostSim, 1);
            Assert.Greater(before, 0);

            f.Network.Disconnect(1);
            f.Host.Poll();

            var bundle = new List<GameCommand>();
            for (int tick = 20; tick < 120; tick++)
            {
                f.Host.Poll();
                if (f.HostDriver.TryGetTickCommands(tick, bundle))
                    f.HostSim.Advance(bundle);
            }

            Assert.AreEqual(before, CountUnits(f.HostSim, 1),
                "the abandoned player's units stay on the map");
        }

        static int CountUnits(GameSim sim, byte player)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].Player == player)
                    n++;
            return n;
        }

        static int FindMovableUnit(GameSim sim, byte player)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == player && (u.Flags & UnitFlags.Building) == 0)
                    return i;
            }
            return -1;
        }

        static unsafe GameCommand Move(GameSim sim, byte player, ushort x, ushort y)
        {
            var cmd = new GameCommand { Op = CommandOp.Move, Player = player, TargetX = x, TargetY = y };
            int slot = FindMovableUnit(sim, player);
            if (slot >= 0)
            {
                cmd.SelectionCount = 1;
                cmd.Selection.Ids[0] = new UnitId((ushort)slot, sim.State.Units[slot].Gen).Packed;
            }
            return cmd;
        }
    }
}
