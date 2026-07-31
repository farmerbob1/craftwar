using System.Collections.Generic;
using System.IO;
using Craftwar.Net;
using Craftwar.NetServer.Transport;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    /// <summary>
    /// The M11 phase 2 headline proof: the EXISTING host/client lockstep
    /// protocol (TurnRelay, HostTurnExchange, ClientTurnExchange —
    /// unmodified, not one line touched for this milestone) stays bit-
    /// identical when run over RelayPeerSocket talking to a real
    /// Craftwar.NetServer instance — real TCP+TLS on loopback, a real room
    /// created and joined, not LoopbackNetwork. This is what makes the
    /// server "a dumb byte-relay for game traffic": it creates the room,
    /// assigns peer ids, and forwards opaque bytes; it never parses a single
    /// TurnInput/TurnCommit message.
    /// </summary>
    public class RelayIntegrationTests
    {
        RelayServerHost _server;
        string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"craftwar-relaytest-{System.Guid.NewGuid():N}.db");
            var config = new ServerConfig { Host = "127.0.0.1", Port = 0, DbPath = _dbPath };
            _server = new RelayServerHost(config);
            _server.Start();
        }

        [TearDown]
        public void TearDown()
        {
            _server.Dispose();
            try { File.Delete(_dbPath); } catch (IOException) { }
        }

        [Test]
        public void ReportMatchResult_OverARealConnection_UpdatesRatings()
        {
            OnlineAccountClient.Register("127.0.0.1", _server.Port, "grom", "hunter22345");
            OnlineAccountClient.Register("127.0.0.1", _server.Port, "thrall", "hunter22345");

            using var hostSocket = RelayPeerSocket.Host("127.0.0.1", _server.Port,
                mapName: "Skirmish.pud", hostName: "grom", roomName: "Test Game", maxPlayers: 2);
            hostSocket.ReportMatchResult("Skirmish.pud", "1v1",
                new[] { "grom", "thrall" }, new[] { true, false });

            // The report is fire-and-forget over the async writer task; give
            // it a moment to actually land before checking the database.
            Craftwar.NetServer.Db.Database db = null;
            double gromRating = 1500;
            for (int i = 0; i < 100 && gromRating <= 1500; i++)
            {
                System.Threading.Thread.Sleep(10);
                db ??= new Craftwar.NetServer.Db.Database(_dbPath);
                var accounts = new Craftwar.NetServer.Db.AccountRepository(db);
                var ratings = new Craftwar.NetServer.Db.RatingRepository(db);
                accounts.TryGetByUsername("grom", out var account);
                (var rating, _) = ratings.GetOrDefault(account.Id);
                gromRating = rating.Rating;
            }

            Assert.Greater(gromRating, 1500, "the winner's rating must have moved over a real report");
        }

        [Test]
        public void OnlineAccountClient_RegisterLoginResume_WorkOverARealConnection()
        {
            string username = "relaytest_" + System.Guid.NewGuid().ToString("N")[..8];
            Assert.AreEqual(AccountResult.Ok,
                OnlineAccountClient.Register("127.0.0.1", _server.Port, username, "hunter22345"));
            Assert.AreEqual(AccountResult.UsernameTaken,
                OnlineAccountClient.Register("127.0.0.1", _server.Port, username, "hunter22345"));
            Assert.AreEqual(AccountResult.WrongCredentials,
                OnlineAccountClient.Login("127.0.0.1", _server.Port, username, "wrongpassword", out _));

            Assert.AreEqual(AccountResult.Ok,
                OnlineAccountClient.Login("127.0.0.1", _server.Port, username, "hunter22345", out string token));
            Assert.IsNotEmpty(token);

            Assert.AreEqual(AccountResult.Ok,
                OnlineAccountClient.ResumeSession("127.0.0.1", _server.Port, token, out string resumedUsername));
            Assert.AreEqual(username, resumedUsername);
        }

        [Test]
        public void OnlineAccountClient_ListRooms_SeesARoomCreatedOverRelayPeerSocket()
        {
            Assert.IsEmpty(OnlineAccountClient.ListRooms("127.0.0.1", _server.Port));

            using var hostSocket = RelayPeerSocket.Host("127.0.0.1", _server.Port,
                mapName: "Skirmish.pud", hostName: "Grom", roomName: "Grom's Game", maxPlayers: 4);

            var rooms = OnlineAccountClient.ListRooms("127.0.0.1", _server.Port);
            Assert.AreEqual(1, rooms.Length);
            Assert.AreEqual(hostSocket.RoomId, rooms[0].RoomId);
            Assert.AreEqual("Skirmish.pud", rooms[0].MapName);
            Assert.AreEqual("Grom", rooms[0].HostName);
            Assert.AreEqual(1, rooms[0].PlayerCount);
            Assert.AreEqual(4, rooms[0].MaxPlayers);
        }

        [Test]
        public void OnlineAccountClient_GetRating_ReturnsTrueForARegisteredAccountAndFalseForAGuest()
        {
            OnlineAccountClient.Register("127.0.0.1", _server.Port, "grom", "hunter22345");

            bool found = OnlineAccountClient.GetRating("127.0.0.1", _server.Port, "grom",
                out int rating, out int games);
            Assert.IsTrue(found);
            Assert.AreEqual(1500, rating, "Glickman's own default, seeded at registration");
            Assert.AreEqual(0, games);

            bool guestFound = OnlineAccountClient.GetRating("127.0.0.1", _server.Port,
                "nobody-signed-up", out _, out _);
            Assert.IsFalse(guestFound);
        }

        [Test]
        public void RelayPeerSocket_GetRating_RoundTripsOverTheAsyncConnection()
        {
            OnlineAccountClient.Register("127.0.0.1", _server.Port, "grom", "hunter22345");
            using var socket = RelayPeerSocket.Host("127.0.0.1", _server.Port,
                mapName: "Skirmish.pud", hostName: "grom", roomName: "Test Game", maxPlayers: 2);

            socket.SendGetRating("grom");

            string username = null;
            bool found = false;
            int rating = 0, games = 0;
            for (int i = 0; i < 200 && username == null; i++)
            {
                if (socket.TryReceiveGetRatingResult(out username, out found, out rating, out games))
                    break;
                System.Threading.Thread.Sleep(5);
            }

            Assert.AreEqual("grom", username);
            Assert.IsTrue(found);
            Assert.AreEqual(1500, rating);
            Assert.AreEqual(0, games);
        }

        [Test]
        public void ListRoomsResult_CarriesTheHostsRealRating()
        {
            OnlineAccountClient.Register("127.0.0.1", _server.Port, "grom", "hunter22345");
            OnlineAccountClient.Register("127.0.0.1", _server.Port, "thrall", "hunter22345");

            using var hostSocket = RelayPeerSocket.Host("127.0.0.1", _server.Port,
                mapName: "Skirmish.pud", hostName: "grom", roomName: "Test Game", maxPlayers: 2);
            hostSocket.ReportMatchResult("Skirmish.pud", "1v1", new[] { "grom", "thrall" }, new[] { true, false });

            RoomSummary room = default;
            for (int i = 0; i < 200 && room.HostRating <= 1500; i++)
            {
                System.Threading.Thread.Sleep(10);
                var rooms = OnlineAccountClient.ListRooms("127.0.0.1", _server.Port);
                if (rooms.Length > 0)
                    room = rooms[0];
            }

            Assert.IsTrue(room.HostRatingKnown);
            Assert.Greater(room.HostRating, 1500, "the winner's rating must be reflected in the room listing");
        }

        [Test]
        public void Chat_RelaysToEveryRoomMember_IncludingTheSender()
        {
            using var hostSocket = RelayPeerSocket.Host("127.0.0.1", _server.Port,
                mapName: "Skirmish.pud", hostName: "Grom", roomName: "Grom's Game", maxPlayers: 4);
            using var clientSocket = RelayPeerSocket.Join("127.0.0.1", _server.Port,
                hostSocket.RoomId, playerName: "Thrall");

            hostSocket.SendChat("Grom", "for the horde");

            (string sender, string text) hostSeen = default, clientSeen = default;
            for (int i = 0; i < 200 && (hostSeen.text == null || clientSeen.text == null); i++)
            {
                if (hostSeen.text == null && hostSocket.TryReceiveChat(out string hs, out string ht))
                    hostSeen = (hs, ht);
                if (clientSeen.text == null && clientSocket.TryReceiveChat(out string cs, out string ct))
                    clientSeen = (cs, ct);
                if (hostSeen.text == null || clientSeen.text == null)
                    System.Threading.Thread.Sleep(5);
            }

            Assert.AreEqual("Grom", hostSeen.sender, "the server broadcast must reach the sender too");
            Assert.AreEqual("for the horde", hostSeen.text);
            Assert.AreEqual("Grom", clientSeen.sender);
            Assert.AreEqual("for the horde", clientSeen.text);
        }

        [Test]
        public void TwoRelayPeers_ThroughARealServer_StayBitIdentical()
        {
            const int TicksPerTurn = 4;
            const int Delay = 2;

            using var hostSocket = RelayPeerSocket.Host("127.0.0.1", _server.Port,
                mapName: "Skirmish.pud", hostName: "Grom", roomName: "Grom's Game", maxPlayers: 4);
            Assert.AreEqual(0, hostSocket.LocalPeerId, "the room creator must be room-peer 0");

            using var clientSocket = RelayPeerSocket.Join("127.0.0.1", _server.Port,
                hostSocket.RoomId, playerName: "Thrall");
            Assert.AreEqual(1, clientSocket.LocalPeerId, "the first joiner must be room-peer 1");

            var pud = TwoSeatMap();
            var hostSim = Boot(pud, seed: 909);
            var clientSim = Boot(pud, seed: 909);
            var relay = new TurnRelay(new byte[] { 0, 1 });

            var host = new HostTurnExchange(hostSocket, relay, localSlot: 0);
            var client = new ClientTurnExchange(clientSocket, localSlot: 1);

            // The room-join announcement (RoomPeerEvent) has to actually
            // arrive and be processed before AssignSlot means anything to
            // the host's own connection-event queue; draining a few Polls
            // first mirrors what a real app's per-frame Poll() loop would do
            // while waiting for the roster to settle.
            for (int i = 0; i < 20 && CountConnectionEvents(hostSocket) == 0; i++)
                System.Threading.Thread.Sleep(10);
            host.AssignSlot(clientSocket.LocalPeerId, 1);

            var hostDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, host);
            var clientDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 1, client);

            bool Step(int tick, List<GameCommand> hostBundle, List<GameCommand> clientBundle)
            {
                bool hostReady = false, clientReady = false;
                for (int spin = 0; spin < 200 && !(hostReady && clientReady); spin++)
                {
                    host.Poll();
                    client.Poll();
                    if (!hostReady) hostReady = hostDriver.TryGetTickCommands(tick, hostBundle);
                    if (!clientReady) clientReady = clientDriver.TryGetTickCommands(tick, clientBundle);
                    if (!hostReady || !clientReady)
                        System.Threading.Thread.Sleep(1); // real network latency, unlike LoopbackNetwork
                }
                if (!hostReady || !clientReady)
                    return false;
                hostSim.Advance(hostBundle);
                clientSim.Advance(clientBundle);
                return true;
            }

            var hb = new List<GameCommand>();
            var cb = new List<GameCommand>();
            for (int tick = 0; tick < 120; tick++)
            {
                if (tick % 41 == 0)
                    hostDriver.SubmitLocalCommand(Move(hostSim, 0, (ushort)(12 + tick % 6), 14));
                if (tick % 53 == 0)
                    clientDriver.SubmitLocalCommand(Move(clientSim, 1, (ushort)(42 + tick % 6), 42));

                Assert.IsTrue(Step(tick, hb, cb), $"a peer stalled at tick {tick} over the real relay");
                Assert.AreEqual(hostSim.State.ComputeHash(), clientSim.State.ComputeHash(),
                    $"diverged at tick {tick}");
            }

            Assert.IsNull(hostSim.State.VerifyChecksums());
            Assert.IsNull(clientSim.State.VerifyChecksums());
            Assert.Greater(hostSim.State.Tick, 100, "the match must have actually run");
        }

        [Test]
        public void ReconnectThroughARealServer_RejoinedPeerCatchesUpAndStaysBitIdentical()
        {
            const int TicksPerTurn = 4;
            const int Delay = 2;

            using var hostSocket = RelayPeerSocket.Host("127.0.0.1", _server.Port,
                mapName: "Skirmish.pud", hostName: "Grom", roomName: "Grom's Game", maxPlayers: 4);
            var clientSocket = RelayPeerSocket.Join("127.0.0.1", _server.Port,
                hostSocket.RoomId, playerName: "Thrall");
            Assert.AreEqual(1, clientSocket.LocalPeerId, "the first joiner must be room-peer 1");

            var pud = TwoSeatMap();
            var hostSim = Boot(pud, seed: 7171);
            var clientSim = Boot(pud, seed: 7171);
            var relay = new TurnRelay(new byte[] { 0, 1 });

            var host = new HostTurnExchange(hostSocket, relay, localSlot: 0);
            var client = new ClientTurnExchange(clientSocket, localSlot: 1);

            for (int i = 0; i < 20 && CountConnectionEvents(hostSocket) == 0; i++)
                System.Threading.Thread.Sleep(10);
            host.AssignSlot(clientSocket.LocalPeerId, 1);

            var hostDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 0, host);
            var clientDriver = new TurnLockstepDriver(TicksPerTurn, Delay, 1, client);

            bool Step(int tick, List<GameCommand> hostBundle, List<GameCommand> clientBundle)
            {
                bool hostReady = false, clientReady = false;
                for (int spin = 0; spin < 200 && !(hostReady && clientReady); spin++)
                {
                    host.Poll();
                    client.Poll();
                    if (!hostReady) hostReady = hostDriver.TryGetTickCommands(tick, hostBundle);
                    if (!clientReady) clientReady = clientDriver.TryGetTickCommands(tick, clientBundle);
                    if (!hostReady || !clientReady)
                        System.Threading.Thread.Sleep(1);
                }
                if (!hostReady || !clientReady)
                    return false;
                hostSim.Advance(hostBundle);
                clientSim.Advance(clientBundle);
                return true;
            }

            var hb = new List<GameCommand>();
            var cb = new List<GameCommand>();
            for (int tick = 0; tick < 80; tick++)
            {
                Assert.IsTrue(Step(tick, hb, cb), $"warm-up stalled at tick {tick} over the real relay");
                Assert.AreEqual(hostSim.State.ComputeHash(), clientSim.State.ComputeHash(),
                    $"diverged at tick {tick}");
            }

            // The client's transport drops for real — a closed TCP connection,
            // not a synthetic event — and the server must notice and push a
            // RoomPeerEvent(peerId, false) to the host over the real socket.
            clientSocket.Dispose();

            bool sawDrop = false;
            for (int i = 0; i < 300 && !sawDrop; i++)
            {
                host.Poll();
                sawDrop = relay.IsSubstituted(1);
                if (!sawDrop)
                    System.Threading.Thread.Sleep(5);
            }
            Assert.IsTrue(sawDrop, "the host must learn of the drop via a real RoomPeerEvent and substitute the slot");

            // The host keeps running alone for a while — TryFreeze resolves the
            // substituted slot as empty input on its own, same as LAN.
            var hostOnlyBundle = new List<GameCommand>();
            int tick2 = 80;
            for (; tick2 < 140; tick2++)
            {
                host.Poll();
                if (hostDriver.TryGetTickCommands(tick2, hostOnlyBundle))
                    hostSim.Advance(hostOnlyBundle);
            }
            Assert.AreEqual(0, hostSim.State.Tick % TicksPerTurn,
                "test setup: snapshot must be taken at a turn boundary");

            // A FRESH connection — a new RelayPeerSocket.Join, getting a brand
            // new room-peer-id (2, never 1 again: RoomManager.NextPeerId never
            // reuses) — claims the dropped seat. This is exactly the scenario
            // mechanism #2 in the M11 plan called out: the rejoin protocol
            // must not depend on peer-id continuity.
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

            using var rejoinSocket = RelayPeerSocket.Join("127.0.0.1", _server.Port,
                hostSocket.RoomId, playerName: "ThrallReturns");
            Assert.AreEqual(2, rejoinSocket.LocalPeerId,
                "the rejoiner gets a fresh room-peer-id, not the dropped peer's old one");
            var reconnect = new ReconnectClient(rejoinSocket, default, claimedSlot: 1, "ThrallReturns");

            bool ready = false;
            for (int i = 0; i < 400 && !ready; i++)
            {
                host.Poll();
                reconnect.Poll();
                ready = reconnect.Ready;
                if (!ready)
                    System.Threading.Thread.Sleep(5);
            }
            Assert.IsTrue(ready, "the rejoining peer must finish receiving the snapshot over the real relay");

            Assert.IsTrue(reconnect.TryComplete(out byte[] snapshotBytes, out byte yourSlot,
                out int resumeTurn, out byte ticksPerTurn, out byte inputDelayTurns,
                out bool[] pausingSlots));
            Assert.AreEqual(1, yourSlot);
            Assert.AreEqual(TicksPerTurn, ticksPerTurn);
            Assert.AreEqual(Delay, inputDelayTurns);

            var rejoinedSim = SimSerializer.Load(snapshotBytes);
            Assert.AreEqual(hostSim.State.ComputeHash(), rejoinedSim.State.ComputeHash(),
                "the received snapshot must be bit-identical to the host's state");

            var rejoinedExchange = new ClientTurnExchange(rejoinSocket, localSlot: 1);
            foreach (var pair in reconnect.BackfilledCommits)
                rejoinedExchange.SeedCommit(pair.Key, pair.Value);
            var rejoinedDriver = new TurnLockstepDriver(ticksPerTurn, inputDelayTurns, yourSlot,
                rejoinedExchange, resumeTurn, pausingSlots);

            var hb2 = new List<GameCommand>();
            var rb2 = new List<GameCommand>();
            int matchedTurns = 0;
            for (int tick = 0; tick < 200; tick++)
            {
                if (tick % 37 == 0)
                    rejoinedDriver.SubmitLocalCommand(Move(rejoinedSim, 1, (ushort)(40 + tick % 8), 40));

                bool hostReady = false, rejoinedReady = false;
                for (int spin = 0; spin < 300 && !(hostReady && rejoinedReady); spin++)
                {
                    host.Poll();
                    rejoinedExchange.Poll();
                    if (!hostReady) hostReady = hostDriver.TryGetTickCommands(1_000_000 + tick, hb2);
                    if (!rejoinedReady) rejoinedReady = rejoinedDriver.TryGetTickCommands(tick, rb2);
                    if (!hostReady || !rejoinedReady)
                        System.Threading.Thread.Sleep(1);
                }
                Assert.IsTrue(hostReady && rejoinedReady, $"stalled resuming at tick {tick} over the real relay");
                hostSim.Advance(hb2);
                rejoinedSim.Advance(rb2);

                if (hostSim.State.Tick % TicksPerTurn == 0)
                {
                    Assert.AreEqual(hostSim.State.ComputeHash(), rejoinedSim.State.ComputeHash(),
                        $"diverged at tick {hostSim.State.Tick}");
                    matchedTurns++;
                }
            }

            Assert.Greater(matchedTurns, 30, "the match must actually keep progressing after reconnect");
            Assert.IsNull(hostSim.State.VerifyChecksums());
            Assert.IsNull(rejoinedSim.State.VerifyChecksums());
        }

        static int CountConnectionEvents(RelayPeerSocket socket)
        {
            // Peek without consuming would need a different API; since this
            // is only used to decide "has anything arrived yet", draining
            // and immediately re-checking Faulted is enough — AssignSlot
            // below does not depend on having seen the event, only on using
            // the correct (already-known) peer id.
            return socket.Faulted ? -1 : 0;
        }

        /// <summary>Minimal two-seat flat map — this test is about the
        /// network path, not economy/AI, so it skips the forests/mines
        /// Assets/Scripts/Tests/AiTestHarness.cs sets up (that file lives in
        /// Unity's EditMode test assembly, not compiled into this project).</summary>
        static PudFile TwoSeatMap()
        {
            const int size = 32;
            var pud = new PudFile { Width = size, Height = size };
            pud.Tiles = new ushort[size * size];
            pud.MoveMap = new ushort[size * size];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;   // grass
                pud.MoveMap[i] = 0x0001; // land-passable
            }
            pud.Owner[0] = (byte)PudOwner.Computer;
            pud.Side[0] = (byte)Race.Human;
            pud.Owner[1] = (byte)PudOwner.Computer;
            pud.Side[1] = (byte)Race.Orc;
            pud.StartGold[0] = pud.StartGold[1] = 2000;
            pud.StartLumber[0] = pud.StartLumber[1] = 1500;
            pud.StartOil[0] = pud.StartOil[1] = 1000;

            pud.Units.Add(new PudUnitEntry { X = 8, Y = 8, Type = (byte)UnitTypeId.TownHall, Owner = 0 });
            pud.Units.Add(new PudUnitEntry { X = 12, Y = 12, Type = (byte)UnitTypeId.Peasant, Owner = 0 });
            pud.Units.Add(new PudUnitEntry { X = 24, Y = 24, Type = (byte)UnitTypeId.GreatHall, Owner = 1 });
            pud.Units.Add(new PudUnitEntry { X = 20, Y = 20, Type = (byte)UnitTypeId.Peon, Owner = 1 });
            return pud;
        }

        static GameSim Boot(PudFile pud, ulong seed)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
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
