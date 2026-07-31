using System;
using System.IO;
using Craftwar.NetServer.Db;
using Craftwar.NetServer.Protocol;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    public class FriendsServiceTests
    {
        string _dbPath;
        AccountRepository _accounts;
        FriendsService _service;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"craftwar-friendstest-{Guid.NewGuid():N}.db");
            var db = new Database(_dbPath);
            db.EnsureSchema();
            _accounts = new AccountRepository(db);
            _service = new FriendsService(_accounts, new FriendsRepository(db));
        }

        [TearDown]
        public void TearDown()
        {
            try { File.Delete(_dbPath); } catch (IOException) { }
        }

        long Create(string username)
        {
            _accounts.TryCreate(username, "hash", out long id);
            return id;
        }

        [Test]
        public void SendRequest_ToRegisteredUser_Succeeds_AndIsPendingNotYetFriends()
        {
            long gromId = Create("grom");
            long thrallId = Create("thrall");

            string failure = _service.SendRequest(gromId, "thrall", out bool becameFriends);

            Assert.IsNull(failure);
            Assert.IsFalse(becameFriends);
            Assert.IsEmpty(_service.ListFriends(gromId), "a pending request is not a friendship yet");
            CollectionAssert.AreEqual(new[] { "grom" }, _service.ListIncomingRequests(thrallId));
        }

        [Test]
        public void SendRequest_ToUnregisteredUser_Fails()
        {
            long gromId = Create("grom");
            string failure = _service.SendRequest(gromId, "nobody", out bool becameFriends);
            Assert.IsNotNull(failure);
            Assert.IsFalse(becameFriends);
        }

        [Test]
        public void SendRequest_ToSelf_Fails()
        {
            long gromId = Create("grom");
            string failure = _service.SendRequest(gromId, "grom", out _);
            Assert.IsNotNull(failure);
        }

        [Test]
        public void SendRequest_Duplicate_Fails()
        {
            long gromId = Create("grom");
            Create("thrall");
            _service.SendRequest(gromId, "thrall", out _);

            string failure = _service.SendRequest(gromId, "thrall", out _);
            Assert.IsNotNull(failure);
        }

        [Test]
        public void SendRequest_WhenTheOtherSideAlreadyAsked_BecomesAMutualFriendshipImmediately()
        {
            long gromId = Create("grom");
            long thrallId = Create("thrall");
            _service.SendRequest(thrallId, "grom", out _); // thrall asks grom first

            string failure = _service.SendRequest(gromId, "thrall", out bool becameFriends);

            Assert.IsNull(failure);
            Assert.IsTrue(becameFriends);
            CollectionAssert.AreEqual(new[] { "thrall" },
                Names(_service.ListFriends(gromId)));
            CollectionAssert.AreEqual(new[] { "grom" },
                Names(_service.ListFriends(thrallId)));
            // No leftover pending requests either direction.
            Assert.IsEmpty(_service.ListIncomingRequests(gromId));
            Assert.IsEmpty(_service.ListIncomingRequests(thrallId));
        }

        [Test]
        public void Respond_Accept_CreatesASymmetricFriendship()
        {
            long gromId = Create("grom");
            long thrallId = Create("thrall");
            _service.SendRequest(gromId, "thrall", out _);

            string failure = _service.Respond(thrallId, "grom", accept: true);

            Assert.IsNull(failure);
            CollectionAssert.AreEqual(new[] { "thrall" }, Names(_service.ListFriends(gromId)));
            CollectionAssert.AreEqual(new[] { "grom" }, Names(_service.ListFriends(thrallId)));
        }

        [Test]
        public void Respond_Decline_LeavesNoFriendshipAndNoPendingRequest()
        {
            long gromId = Create("grom");
            long thrallId = Create("thrall");
            _service.SendRequest(gromId, "thrall", out _);

            string failure = _service.Respond(thrallId, "grom", accept: false);

            Assert.IsNull(failure);
            Assert.IsEmpty(_service.ListFriends(gromId));
            Assert.IsEmpty(_service.ListIncomingRequests(thrallId));
        }

        [Test]
        public void Respond_WithNoPendingRequest_Fails()
        {
            long gromId = Create("grom");
            Create("thrall");
            string failure = _service.Respond(gromId, "thrall", accept: true);
            Assert.IsNotNull(failure);
        }

        [Test]
        public void RemoveFriend_RemovesBothDirections()
        {
            long gromId = Create("grom");
            long thrallId = Create("thrall");
            _service.SendRequest(gromId, "thrall", out _);
            _service.Respond(thrallId, "grom", accept: true);

            string failure = _service.RemoveFriend(gromId, "thrall");

            Assert.IsNull(failure);
            Assert.IsEmpty(_service.ListFriends(gromId));
            Assert.IsEmpty(_service.ListFriends(thrallId));
        }

        [Test]
        public void RemoveFriend_WhenNotFriends_Fails()
        {
            long gromId = Create("grom");
            Create("thrall");
            string failure = _service.RemoveFriend(gromId, "thrall");
            Assert.IsNotNull(failure);
        }

        [Test]
        public void ListOutgoingRequests_ShowsThePendingRequestFromTheSender_Side()
        {
            long gromId = Create("grom");
            long thrallId = Create("thrall");
            _service.SendRequest(gromId, "thrall", out _);

            CollectionAssert.AreEqual(new[] { "thrall" }, _service.ListOutgoingRequests(gromId));
            CollectionAssert.AreEqual(new[] { "grom" }, _service.ListIncomingRequests(thrallId));
        }

        static string[] Names(System.Collections.Generic.List<(long accountId, string username)> friends)
        {
            var names = new string[friends.Count];
            for (int i = 0; i < friends.Count; i++)
                names[i] = friends[i].username;
            return names;
        }

    }
}
