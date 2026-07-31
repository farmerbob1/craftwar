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

        // --- polling helpers — real async I/O over a real socket, same shape as RelayIntegrationTests ---

        static (bool ok, string reason, string channelName, string[] members, string opUsername) WaitForJoinResult(
            SocialClient client)
        {
            for (int i = 0; i < 200; i++)
            {
                if (client.TryReceiveChannelJoinResult(out bool ok, out string reason, out string channelName,
                        out string[] members, out string opUsername))
                    return (ok, reason, channelName, members, opUsername);
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
    }
}
