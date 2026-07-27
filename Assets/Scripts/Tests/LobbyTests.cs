using Craftwar.Net;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The pre-game handshake. The point of most of these is that a mismatched
    /// build is refused at join time: every one of them would otherwise become a
    /// desync hundreds of turns into a match, with nothing pointing at the cause.
    /// </summary>
    public class LobbyTests
    {
        static BuildIdentity Identity(uint mapHash = 0xABCD) => new BuildIdentity
        {
            ProtocolVersion = BuildIdentity.CurrentProtocolVersion,
            SimVersion = SimConstants.SimVersion,
            MapHash = mapHash,
            RulesHash = 0x1234,
            AiProfileHash = 0x5678,
        };

        static LobbyPayload TwoSeatPayload()
        {
            var payload = new LobbyPayload { MapPath = "Skirmish.pud", Seed = 7 };
            payload.Slots[0] = new LobbySlot
            {
                SeatStatus = (byte)LobbySeatStatus.Human, Name = "Host",
            };
            payload.Slots[1] = new LobbySlot { SeatStatus = (byte)LobbySeatStatus.Open };
            return payload;
        }

        sealed class Pair
        {
            public LoopbackNetwork Network;
            public LobbyHost Host;
            public LobbyClient Client;
            public void Pump(int rounds = 4)
            {
                for (int i = 0; i < rounds; i++)
                {
                    Client.Poll();
                    Host.Poll();
                    Client.Poll();
                }
            }
        }

        static Pair Connect(uint hostMap = 0xABCD, uint clientMap = 0xABCD)
        {
            var network = new LoopbackNetwork();
            var hostPeer = network.CreatePeer();
            var clientPeer = network.CreatePeer();
            return new Pair
            {
                Network = network,
                Host = new LobbyHost(hostPeer, Identity(hostMap), TwoSeatPayload()),
                Client = new LobbyClient(clientPeer, Identity(clientMap), "Grom"),
            };
        }

        [Test]
        public void AMatchingClient_IsSeatedAndAppearsInTheRoster()
        {
            var p = Connect();
            p.Pump();

            Assert.IsTrue(p.Client.Seated, "a matching build should be admitted");
            Assert.AreEqual(1, p.Client.MySlot, "the first open seat");
            Assert.AreEqual(JoinRejectReason.None, p.Client.Rejection);

            // The host's roster shows the newcomer as a person, not open/AI.
            Assert.AreEqual((byte)LobbySeatStatus.Human, p.Host.Payload.Slots[1].SeatStatus);
            Assert.AreEqual("Grom", p.Host.Payload.Slots[1].Name);
            Assert.AreEqual(2, p.Host.Payload.HumanCount());

            // And the client sees the same roster the host does.
            Assert.AreEqual("Host", p.Client.Payload.Slots[0].Name);
            Assert.AreEqual("Grom", p.Client.Payload.Slots[1].Name);
        }

        [Test]
        public void AJoinerIsSeatedBeforeItCanKnowTheMap_ThenConfirms()
        {
            // The handshake is two-phase for a real reason: a joiner cannot hash
            // the host's map until it has been told which map that is. The first
            // request therefore carries only the version fields.
            var p = Connect();
            p.Pump();

            Assert.IsTrue(p.Client.Seated, "seated on versions alone");
            Assert.AreEqual("Skirmish.pud", p.Client.Payload.MapPath,
                "and told the map, so it can now hash its own copy");

            p.Client.ConfirmIdentity(Identity());
            p.Pump();

            Assert.IsTrue(p.Client.Seated, "a matching copy keeps the seat");
            Assert.AreEqual(JoinRejectReason.None, p.Client.Rejection);
        }

        [Test]
        public void ADifferentCopyOfTheMap_LosesTheSeatOnConfirmation()
        {
            // The realistic case: map paths resolve either into StreamingAssets
            // or into each player's own Warcraft II install, so two players
            // picking "the same map" routinely load different bytes.
            var p = Connect();
            p.Pump();
            Assert.IsTrue(p.Client.Seated);

            p.Client.ConfirmIdentity(Identity(mapHash: 0xDEAD));
            p.Pump();

            Assert.IsFalse(p.Client.Seated, "the seat is taken back");
            Assert.AreEqual(JoinRejectReason.MapMismatch, p.Client.Rejection);
            Assert.AreEqual(1, p.Host.Payload.HumanCount(), "and freed on the host");
            Assert.AreEqual((byte)LobbySeatStatus.Open, p.Host.Payload.Slots[1].SeatStatus,
                "a lost seat goes back to waiting, not to the computer");
        }

        [Test]
        public void AnOlderBuild_IsRefusedOnSimVersionFirst()
        {
            var network = new LoopbackNetwork();
            var hostPeer = network.CreatePeer();
            var clientPeer = network.CreatePeer();
            var stale = Identity();
            stale.SimVersion = SimConstants.SimVersion + 1;
            stale.MapHash = 0xDEAD; // also wrong, but the deeper mismatch wins

            var p = new Pair
            {
                Network = network,
                Host = new LobbyHost(hostPeer, Identity(), TwoSeatPayload()),
                Client = new LobbyClient(clientPeer, stale, "Old"),
            };
            p.Pump();

            Assert.AreEqual(JoinRejectReason.SimVersion, p.Client.Rejection,
                "the most fundamental incompatibility is the one reported");
        }

        [Test]
        public void AFullGame_RefusesFurtherJoiners()
        {
            var p = Connect();
            p.Pump();
            Assert.IsTrue(p.Client.Seated);

            var thirdPeer = p.Network.CreatePeer();
            var third = new LobbyClient(thirdPeer, Identity(), "Latecomer");
            for (int i = 0; i < 4; i++)
            {
                third.Poll();
                p.Host.Poll();
                third.Poll();
            }

            Assert.IsFalse(third.Seated);
            Assert.AreEqual(JoinRejectReason.GameFull, third.Rejection);
        }

        [Test]
        public void AClientLeavingTheLobby_FreesItsSeat()
        {
            var p = Connect();
            p.Pump();
            Assert.AreEqual((byte)LobbySeatStatus.Human, p.Host.Payload.Slots[1].SeatStatus);

            p.Network.Disconnect(1);
            p.Host.Poll();

            Assert.AreEqual((byte)LobbySeatStatus.Open, p.Host.Payload.Slots[1].SeatStatus,
                "the seat is available again, not handed to the computer");
            Assert.AreEqual(1, p.Host.Payload.HumanCount());
        }

        [Test]
        public void TheHost_CanCycleAnUnoccupiedSeatButNotAnOccupiedOne()
        {
            var p = Connect();
            p.Pump();

            // Seat 1 is occupied (Grom) — the host cannot override it directly.
            Assert.IsFalse(p.Host.SetSeatStatus(1, LobbySeatStatus.Computer));
            Assert.AreEqual((byte)LobbySeatStatus.Human, p.Host.Payload.Slots[1].SeatStatus);

            // A third (open, but nonexistent here) seat isn't playable at all —
            // use seat 1 after the client leaves instead.
            p.Network.Disconnect(1);
            p.Host.Poll();
            Assert.IsTrue(p.Host.SetSeatStatus(1, LobbySeatStatus.Computer));
            Assert.AreEqual((byte)LobbySeatStatus.Computer, p.Host.Payload.Slots[1].SeatStatus);

            Assert.IsTrue(p.Host.SetSeatStatus(1, LobbySeatStatus.Closed));
            Assert.AreEqual((byte)LobbySeatStatus.Closed, p.Host.Payload.Slots[1].SeatStatus);
        }

        [Test]
        public void StartMatch_RefusesWhileASeatIsStillOpen()
        {
            var payload = TwoSeatPayload(); // slot 1 is Open, nobody joined
            var network = new LoopbackNetwork();
            var hostPeer = network.CreatePeer();
            var host = new LobbyHost(hostPeer, Identity(), payload);

            Assert.IsFalse(host.CanStart());
            Assert.IsFalse(host.StartMatch(), "must not be able to start with an unresolved seat");

            Assert.IsTrue(host.SetSeatStatus(1, LobbySeatStatus.Computer));
            Assert.IsTrue(host.CanStart());
            Assert.IsTrue(host.StartMatch());
        }

        [Test]
        public void TheHost_CanRegroupTeams()
        {
            var p = Connect();
            p.Pump();

            p.Host.SetSeatTeam(1, 0); // put the joiner on the host's team (0)
            Assert.AreEqual(0, p.Host.Payload.Slots[1].Team);
        }

        [Test]
        public void StartingTheMatch_DeliversTheRosterAndTheSeat()
        {
            var p = Connect();
            p.Pump();

            LobbyPayload started = null;
            byte startedSeat = 255;
            p.Client.Started += (payload, seat) => { started = payload; startedSeat = seat; };

            Assert.IsTrue(p.Host.StartMatch());
            p.Client.Poll();

            Assert.IsNotNull(started, "the client must be told to load the match");
            Assert.AreEqual(1, startedSeat);
            Assert.AreEqual("Skirmish.pud", started.MapPath);
            Assert.AreEqual(7UL, started.Seed, "everyone must simulate from the host's seed");
        }

        [Test]
        public void ParticipatingSlots_CoverEveryPlayingSeat_HumanOrComputer()
        {
            var payload = TwoSeatPayload();
            payload.Slots[1] = new LobbySlot { SeatStatus = (byte)LobbySeatStatus.Computer };
            payload.Slots[3] = new LobbySlot { SeatStatus = (byte)LobbySeatStatus.Computer };

            CollectionAssert.AreEqual(new byte[] { 0, 1, 3 }, payload.ParticipatingSlots(),
                "a turn waits on computer seats too — the host produces their input");
        }

        [Test]
        public void LobbyPayload_RoundTripsThroughBytes()
        {
            var payload = TwoSeatPayload();
            payload.Slots[1].Name = "Grom";
            payload.Slots[1].SeatStatus = (byte)LobbySeatStatus.Human;
            payload.InputDelayTurns = 3;

            var w = new ByteWriter(64);
            payload.Write(ref w);
            var r = new ByteReader(w.ToArray());
            var back = LobbyPayload.Read(ref r);

            Assert.AreEqual(payload.MapPath, back.MapPath);
            Assert.AreEqual(payload.Seed, back.Seed);
            Assert.AreEqual(3, back.InputDelayTurns);
            Assert.AreEqual("Grom", back.Slots[1].Name);
            Assert.AreEqual((byte)LobbySeatStatus.Human, back.Slots[1].SeatStatus);
            Assert.AreEqual(w.Position, r.Position, "reader consumes exactly what the writer produced");
        }
    }
}
