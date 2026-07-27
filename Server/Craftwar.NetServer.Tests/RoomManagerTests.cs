using Craftwar.Net;
using Craftwar.NetServer.Protocol;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    public class RoomManagerTests
    {
        [Test]
        public void Creator_IsAlwaysRoomPeerZero()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("conn-a", "Skirmish.pud", "Grom", maxPlayers: 4);
            Assert.AreEqual("conn-a", room.Members[0]);
        }

        [Test]
        public void Joiners_GetSequentialPeerIds()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("host", "Skirmish.pud", "Grom", maxPlayers: 4);

            Assert.AreEqual(RoomJoinFailure.None, rooms.TryJoinRoom(room.Id, "conn-b", out _, out int peerB));
            Assert.AreEqual(1, peerB);
            Assert.AreEqual(RoomJoinFailure.None, rooms.TryJoinRoom(room.Id, "conn-c", out _, out int peerC));
            Assert.AreEqual(2, peerC);
        }

        [Test]
        public void Join_UnknownRoom_Fails()
        {
            var rooms = new RoomManager();
            var failure = rooms.TryJoinRoom("no-such-room", "conn-a", out var room, out int peerId);
            Assert.AreEqual(RoomJoinFailure.RoomNotFound, failure);
            Assert.IsNull(room);
        }

        [Test]
        public void Join_FullRoom_Fails()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("host", "Skirmish.pud", "Grom", maxPlayers: 1);
            var failure = rooms.TryJoinRoom(room.Id, "conn-b", out _, out _);
            Assert.AreEqual(RoomJoinFailure.RoomFull, failure);
        }

        [Test]
        public void Join_WhileAlreadyInARoom_Fails()
        {
            var rooms = new RoomManager();
            var roomA = rooms.CreateRoom("host-a", "A.pud", "Grom", maxPlayers: 4);
            var roomB = rooms.CreateRoom("host-b", "B.pud", "Thrall", maxPlayers: 4);
            rooms.TryJoinRoom(roomA.Id, "conn-x", out _, out _);

            var failure = rooms.TryJoinRoom(roomB.Id, "conn-x", out _, out _);
            Assert.AreEqual(RoomJoinFailure.AlreadyInARoom, failure);
        }

        [Test]
        public void HostLeaving_RemovesTheWholeRoom()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("host", "Skirmish.pud", "Grom", maxPlayers: 4);
            rooms.TryJoinRoom(room.Id, "conn-b", out _, out _);

            Assert.IsTrue(rooms.RemoveMember("host", out var left, out int peerId));
            Assert.AreEqual(0, peerId);
            CollectionAssert.IsEmpty(rooms.ListRooms());
        }

        [Test]
        public void NonHostLeaving_LeavesTheRoomIntact()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("host", "Skirmish.pud", "Grom", maxPlayers: 4);
            rooms.TryJoinRoom(room.Id, "conn-b", out _, out int peerB);

            Assert.IsTrue(rooms.RemoveMember("conn-b", out _, out int removedPeer));
            Assert.AreEqual(peerB, removedPeer);
            Assert.AreEqual(1, rooms.ListRooms().Count);
            Assert.AreEqual(1, rooms.ListRooms()[0].PlayerCount);
        }

        [Test]
        public void ListRooms_ReflectsPlayerCount()
        {
            var rooms = new RoomManager();
            rooms.CreateRoom("host", "Skirmish.pud", "Grom", maxPlayers: 4);
            var list = rooms.ListRooms();
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(1, list[0].PlayerCount);
            Assert.AreEqual(4, list[0].MaxPlayers);
        }
    }
}
