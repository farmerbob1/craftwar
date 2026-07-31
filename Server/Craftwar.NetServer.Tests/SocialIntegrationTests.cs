using System;
using System.IO;
using System.Threading;
using Craftwar.Net;
using Craftwar.NetServer.Transport;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    /// <summary>
    /// The M12 phase 1 headline proof: SocialClient (a persistent,
    /// account-bound connection entirely separate from RelayPeerSocket/
    /// rooms — see the M12 plan's mechanism notes 1-2) actually works over
    /// a real Craftwar.NetServer instance — real TCP+TLS on loopback, real
    /// accounts, not mocks. This is what proves the account-binding fix in
    /// ClientConnection (Login/ResumeSession now store AccountId/Username
    /// and register PresenceDirectory) is load-bearing, not decorative.
    /// </summary>
    public class SocialIntegrationTests
    {
        RelayServerHost _server;
        string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"craftwar-socialtest-{Guid.NewGuid():N}.db");
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

        string LoginNewAccount(string usernamePrefix)
        {
            string username = usernamePrefix + "_" + Guid.NewGuid().ToString("N")[..8];
            Assert.AreEqual(AccountResult.Ok,
                OnlineAccountClient.Register("127.0.0.1", _server.Port, username, "hunter22345"));
            Assert.AreEqual(AccountResult.Ok,
                OnlineAccountClient.Login("127.0.0.1", _server.Port, username, "hunter22345", out string token));
            return token;
        }

        [Test]
        public void Connect_AutoJoinsTheDefaultChannel()
        {
            using var client = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));

            var join = WaitForJoinResult(client);
            Assert.IsTrue(join.ok, join.reason);
            Assert.AreEqual(SocialClient.DefaultChannelName, join.channelName);
            CollectionAssert.AreEqual(new[] { client.Username }, join.members);
            Assert.AreEqual(client.Username, join.opUsername);
        }

        [Test]
        public void SecondJoiner_SeesFirstMember_AndFirstMemberIsNotified()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);

            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            var joinB = WaitForJoinResult(clientB);
            CollectionAssert.AreEquivalent(new[] { clientA.Username, clientB.Username }, joinB.members);

            var eventOnA = WaitForMemberEvent(clientA);
            Assert.AreEqual(clientB.Username, eventOnA.username);
            Assert.IsTrue(eventOnA.joined);
        }

        [Test]
        public void ChannelChat_ReachesEveryMember_IncludingTheSender()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA); // drain "B joined" before asserting on chat

            clientA.SendChannelChat("for the horde");

            var seenByA = WaitForChat(clientA);
            var seenByB = WaitForChat(clientB);
            Assert.AreEqual("for the horde", seenByA.text);
            Assert.AreEqual(clientA.Username, seenByA.senderUsername);
            Assert.AreEqual("for the horde", seenByB.text);
            Assert.AreEqual(clientA.Username, seenByB.senderUsername);
        }

        [Test]
        public void Joining_ANewChannel_AnnouncesDepartureToTheOldOne()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA); // B joining Town Hall

            clientB.JoinChannel("Clan/WC2");
            var joinResult = WaitForJoinResult(clientB);
            Assert.AreEqual("Clan/WC2", joinResult.channelName);

            var departure = WaitForMemberEvent(clientA);
            Assert.AreEqual(clientB.Username, departure.username);
            Assert.IsFalse(departure.joined);
        }

        [Test]
        public void OperatorCanKick_AndTheKickedUserIsNotifiedDistinctly()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom")); // op — first to join
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA);

            clientA.KickFromChannel(clientB.Username);

            var kickResult = WaitForKickResult(clientA);
            Assert.IsTrue(kickResult.ok, kickResult.reason);

            var kicked = WaitForKicked(clientB);
            Assert.AreEqual(SocialClient.DefaultChannelName, kicked.channelName);
            Assert.AreEqual(clientA.Username, kicked.byUsername);
        }

        [Test]
        public void NonOperatorCannotKick()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA);

            clientB.KickFromChannel(clientA.Username); // B is not the operator

            var kickResult = WaitForKickResult(clientB);
            Assert.IsFalse(kickResult.ok);
        }

        [Test]
        public void OperatorCanSetMotd_ExistingMembersSeeItChange()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom")); // op
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            var joinB = WaitForJoinResult(clientB);
            Assert.AreEqual("", joinB.motd, "a fresh channel has no MOTD yet");
            WaitForMemberEvent(clientA);

            clientA.SetChannelMotd("Welcome to the horde");

            var setResult = WaitForMotdSetResult(clientA);
            Assert.IsTrue(setResult.ok, setResult.reason);

            var changedOnB = WaitForMotdChanged(clientB);
            Assert.AreEqual(SocialClient.DefaultChannelName, changedOnB.channelName);
            Assert.AreEqual("Welcome to the horde", changedOnB.motd);
        }

        [Test]
        public void NonOperatorCannotSetMotd()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA);

            clientB.SetChannelMotd("hijacked");

            var result = WaitForMotdSetResult(clientB);
            Assert.IsFalse(result.ok);
        }

        [Test]
        public void Motd_SurvivesEveryoneLoggingOutAndTheDefaultChannelBeingRecreated()
        {
            // The real bug report: the user set a MOTD, everyone (including
            // them) disconnected — which destroys the in-memory ChatChannel
            // once it has no members left (ChannelManager.LeaveInternal) —
            // and logging back in auto-joins a BRAND NEW "Town Hall" object
            // with no MOTD, unless it's actually persisted server-side.
            string token = LoginNewAccount("grom");
            using (var first = SocialClient.Connect("127.0.0.1", _server.Port, token))
            {
                WaitForJoinResult(first);
                first.SetChannelMotd("Welcome to the horde");
                var setResult = WaitForMotdSetResult(first);
                Assert.IsTrue(setResult.ok, setResult.reason);
            } // Dispose — the only member disconnects, the server destroys the channel

            // Give the server a moment to actually process the disconnect
            // (ClientConnection.RunAsync's finally block) before rejoining.
            Thread.Sleep(200);

            using var second = SocialClient.Connect("127.0.0.1", _server.Port, token);
            var join = WaitForJoinResult(second);
            Assert.IsTrue(join.ok, join.reason);
            Assert.AreEqual("Welcome to the horde", join.motd,
                "a fresh connection rejoining Town Hall must still see the MOTD");
        }

        [Test]
        public void FriendRequest_ThenAccept_CreatesASymmetricFriendshipWithPresence()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA);

            clientA.SendFriendRequest(clientB.Username);
            var reqResult = WaitForFriendRequestResult(clientA);
            Assert.IsTrue(reqResult.ok, reqResult.reason);
            Assert.IsFalse(reqResult.becameFriends);

            string received = WaitForFriendRequestReceived(clientB);
            Assert.AreEqual(clientA.Username, received);

            clientB.RespondToFriendRequest(clientA.Username, accept: true);
            var respResult = WaitForFriendRespondResult(clientB);
            Assert.IsTrue(respResult.ok, respResult.reason);

            var answered = WaitForFriendRequestAnswered(clientA);
            Assert.AreEqual(clientB.Username, answered.byUsername);
            Assert.IsTrue(answered.accepted);

            clientA.RequestFriendList();
            var listA = WaitForFriendListResult(clientA);
            CollectionAssert.AreEqual(new[] { clientB.Username }, listA.friendUsernames);
            Assert.IsTrue(listA.friendOnline[0], "the friend is connected right now");
        }

        [Test]
        public void FriendRequest_MutualBothWays_BecomesFriendsImmediately()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA);

            clientB.SendFriendRequest(clientA.Username);
            WaitForFriendRequestResult(clientB);
            WaitForFriendRequestReceived(clientA);

            clientA.SendFriendRequest(clientB.Username);
            var result = WaitForFriendRequestResult(clientA);

            Assert.IsTrue(result.ok, result.reason);
            Assert.IsTrue(result.becameFriends, "a request answering an existing one IS the friendship");
        }

        [Test]
        public void RemoveFriend_NotifiesTheOtherSide()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA);

            clientA.SendFriendRequest(clientB.Username);
            WaitForFriendRequestResult(clientA);
            WaitForFriendRequestReceived(clientB);
            clientB.RespondToFriendRequest(clientA.Username, true);
            WaitForFriendRespondResult(clientB);
            WaitForFriendRequestAnswered(clientA);

            clientA.RemoveFriend(clientB.Username);
            var removeResult = WaitForFriendRemoveResult(clientA);
            Assert.IsTrue(removeResult.ok, removeResult.reason);

            string removedBy = WaitForFriendRemoved(clientB);
            Assert.AreEqual(clientA.Username, removedBy);
        }

        [Test]
        public void Whisper_DeliversToRecipientAndEchoesToSender()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);
            using var clientB = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("thrall"));
            WaitForJoinResult(clientB);
            WaitForMemberEvent(clientA);

            clientA.SendWhisper(clientB.Username, "for the horde, quietly");

            var whisperResult = WaitForWhisperResult(clientA);
            Assert.IsTrue(whisperResult.ok, whisperResult.reason);

            var seenByB = WaitForWhisperReceived(clientB);
            Assert.AreEqual(clientA.Username, seenByB.fromUsername);
            Assert.AreEqual(clientB.Username, seenByB.toUsername);
            Assert.AreEqual("for the horde, quietly", seenByB.text);

            var echoedToA = WaitForWhisperReceived(clientA);
            Assert.AreEqual(clientA.Username, echoedToA.fromUsername, "the sender's own echo");
            Assert.AreEqual("for the horde, quietly", echoedToA.text);
        }

        [Test]
        public void Whisper_ToUnknownUser_Fails()
        {
            using var clientA = SocialClient.Connect("127.0.0.1", _server.Port, LoginNewAccount("grom"));
            WaitForJoinResult(clientA);

            clientA.SendWhisper("nobody-signed-up", "hello?");

            var result = WaitForWhisperResult(clientA);
            Assert.IsFalse(result.ok);
        }

        // --- polling helpers — real async I/O over a real socket, same shape as RelayIntegrationTests ---

        static (bool ok, string reason, string channelName, string[] members, string opUsername, string motd)
            WaitForJoinResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelJoinResult(out bool ok, out string reason, out string channelName,
                        out string[] members, out string opUsername, out string motd))
                    return (ok, reason, channelName, members, opUsername, motd);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a channel join result");
            return default;
        }

        static (string channelName, string username, bool joined, string opUsername) WaitForMemberEvent(
            SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelMemberEvent(out string channelName, out string username, out bool joined,
                        out string opUsername))
                    return (channelName, username, joined, opUsername);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a channel member event");
            return default;
        }

        static (string channelName, string senderUsername, string text) WaitForChat(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelChat(out string channelName, out string senderUsername, out string text))
                    return (channelName, senderUsername, text);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a channel chat message");
            return default;
        }

        static (bool ok, string reason) WaitForKickResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelKickResult(out bool ok, out string reason))
                    return (ok, reason);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a channel kick result");
            return default;
        }

        static (string channelName, string byUsername) WaitForKicked(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelKicked(out string channelName, out string byUsername))
                    return (channelName, byUsername);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting to be kicked");
            return default;
        }

        static (bool ok, string reason) WaitForMotdSetResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelSetMotdResult(out bool ok, out string reason))
                    return (ok, reason);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a MOTD set result");
            return default;
        }

        static (string channelName, string motd) WaitForMotdChanged(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelMotdChanged(out string channelName, out string motd))
                    return (channelName, motd);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a MOTD change notification");
            return default;
        }

        static (bool ok, string reason, bool becameFriends) WaitForFriendRequestResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveFriendRequestResult(out bool ok, out string reason, out bool becameFriends))
                    return (ok, reason, becameFriends);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a friend request result");
            return default;
        }

        static string WaitForFriendRequestReceived(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveFriendRequestReceived(out string fromUsername))
                    return fromUsername;
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a friend request to arrive");
            return default;
        }

        static (bool ok, string reason) WaitForFriendRespondResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveFriendRespondResult(out bool ok, out string reason))
                    return (ok, reason);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a friend respond result");
            return default;
        }

        static (string byUsername, bool accepted) WaitForFriendRequestAnswered(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveFriendRequestAnswered(out string byUsername, out bool accepted))
                    return (byUsername, accepted);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a friend request answer");
            return default;
        }

        static (bool ok, string reason) WaitForFriendRemoveResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveFriendRemoveResult(out bool ok, out string reason))
                    return (ok, reason);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a friend remove result");
            return default;
        }

        static string WaitForFriendRemoved(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveFriendRemoved(out string byUsername))
                    return byUsername;
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a friend-removed notification");
            return default;
        }

        static (string[] friendUsernames, bool[] friendOnline, string[] incoming, string[] outgoing)
            WaitForFriendListResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveFriendListResult(out string[] friendUsernames, out bool[] friendOnline,
                        out string[] incoming, out string[] outgoing))
                    return (friendUsernames, friendOnline, incoming, outgoing);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a friend list result");
            return default;
        }

        static (bool ok, string reason) WaitForWhisperResult(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveWhisperResult(out bool ok, out string reason))
                    return (ok, reason);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a whisper result");
            return default;
        }

        static (string fromUsername, string toUsername, string text) WaitForWhisperReceived(SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveWhisperReceived(out string fromUsername, out string toUsername, out string text))
                    return (fromUsername, toUsername, text);
                Thread.Sleep(10);
            }
            Assert.Fail("timed out waiting for a whisper to arrive");
            return default;
        }
    }
}
