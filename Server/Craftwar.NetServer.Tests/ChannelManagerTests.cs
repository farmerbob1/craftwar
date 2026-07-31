using System;
using System.IO;
using Craftwar.NetServer.Db;
using Craftwar.NetServer.Protocol;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    public class ChannelManagerTests
    {
        [Test]
        public void Join_CreatesChannel_FirstMemberIsOp()
        {
            var channels = new ChannelManager();
            var channel = channels.Join(1, "grom", "Town Hall", out var previous);

            Assert.IsNull(previous);
            Assert.AreEqual("Town Hall", channel.Name);
            Assert.AreEqual(1, channel.OpAccountId);
            Assert.AreEqual("grom", channel.OpUsername);
            CollectionAssert.AreEqual(new[] { "grom" }, channel.MemberUsernamesSnapshot());
        }

        [Test]
        public void Join_ExistingChannel_AddsMember_OpUnchanged()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);
            var channel = channels.Join(2, "thrall", "Town Hall", out _);

            Assert.AreEqual(1, channel.OpAccountId);
            CollectionAssert.AreEqual(new[] { "grom", "thrall" }, channel.MemberUsernamesSnapshot());
        }

        [Test]
        public void Join_AnotherChannel_LeavesThePreviousOne()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);

            var newChannel = channels.Join(1, "grom", "Clan/WC2", out var previous);

            Assert.AreEqual("Town Hall", previous.Name);
            Assert.IsEmpty(previous.MemberUsernamesSnapshot());
            Assert.AreEqual("Clan/WC2", newChannel.Name);
            Assert.IsTrue(channels.TryGetChannelOf(1, out var current));
            Assert.AreEqual("Clan/WC2", current.Name);
        }

        [Test]
        public void Leave_LastMember_DestroysTheChannel()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);

            var left = channels.Leave(1);

            Assert.AreEqual("Town Hall", left.Name);
            Assert.IsFalse(channels.TryGetChannelOf(1, out _));
            // Rejoining creates it fresh, proving the old one is really gone.
            var recreated = channels.Join(2, "thrall", "Town Hall", out _);
            Assert.AreEqual(2, recreated.OpAccountId);
        }

        [Test]
        public void Leave_NonLastMember_ChannelSurvives()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);
            channels.Join(2, "thrall", "Town Hall", out _);

            channels.Leave(2);

            Assert.IsTrue(channels.TryGetChannelOf(1, out var channel));
            CollectionAssert.AreEqual(new[] { "grom" }, channel.MemberUsernamesSnapshot());
        }

        [Test]
        public void OpMigrates_WhenTheOperatorLeaves()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);
            channels.Join(2, "thrall", "Town Hall", out _);
            channels.Join(3, "jaina", "Town Hall", out _);

            channels.Leave(1);

            Assert.IsTrue(channels.TryGetChannelOf(2, out var channel));
            Assert.AreEqual(2, channel.OpAccountId);
            Assert.AreEqual("thrall", channel.OpUsername);
        }

        [Test]
        public void TryKick_ByNonOperator_Fails()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);
            channels.Join(2, "thrall", "Town Hall", out _);
            channels.Join(3, "jaina", "Town Hall", out _);

            string failure = channels.TryKick(2, "jaina", out _, out _);

            Assert.IsNotNull(failure);
            Assert.IsTrue(channels.TryGetChannelOf(3, out _)); // jaina was not actually removed
        }

        [Test]
        public void TryKick_ByOperator_RemovesTheTarget()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);
            channels.Join(2, "thrall", "Town Hall", out _);

            string failure = channels.TryKick(1, "thrall", out var channel, out long targetAccountId);

            Assert.IsNull(failure);
            Assert.AreEqual(2, targetAccountId);
            Assert.IsFalse(channels.TryGetChannelOf(2, out _));
            CollectionAssert.AreEqual(new[] { "grom" }, channel.MemberUsernamesSnapshot());
        }

        [Test]
        public void TryKick_TargetNotInChannel_Fails()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);

            string failure = channels.TryKick(1, "nobody", out _, out _);

            Assert.IsNotNull(failure);
        }

        [Test]
        public void TryKick_Self_Fails()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);

            string failure = channels.TryKick(1, "grom", out _, out _);

            Assert.IsNotNull(failure);
            Assert.IsTrue(channels.TryGetChannelOf(1, out _));
        }

        [Test]
        public void TrySetMotd_ByOperator_Succeeds()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);
            channels.Join(2, "thrall", "Town Hall", out _);

            string failure = channels.TrySetMotd(1, "Welcome to the horde", out var channel);

            Assert.IsNull(failure);
            Assert.AreEqual("Welcome to the horde", channel.Motd);
        }

        [Test]
        public void TrySetMotd_ByNonOperator_Fails()
        {
            var channels = new ChannelManager();
            channels.Join(1, "grom", "Town Hall", out _);
            channels.Join(2, "thrall", "Town Hall", out _);

            string failure = channels.TrySetMotd(2, "hijacked", out _);

            Assert.IsNotNull(failure);
            Assert.IsTrue(channels.TryGetChannelOf(1, out var channel));
            Assert.AreEqual("", channel.Motd);
        }

        [Test]
        public void TrySetMotd_NotInAChannel_Fails()
        {
            var channels = new ChannelManager();
            string failure = channels.TrySetMotd(99, "nope", out _);
            Assert.IsNotNull(failure);
        }

        [Test]
        public void FreshChannel_StartsWithNoMotd()
        {
            var channels = new ChannelManager();
            var channel = channels.Join(1, "grom", "Town Hall", out _);
            Assert.AreEqual("", channel.Motd);
        }

        [Test]
        public void Motd_SurvivesTheChannelEmptyingOutAndBeingRecreated_WhenBackedByARepository()
        {
            // The actual bug report this guards: a MOTD that only lives on
            // the in-memory ChatChannel vanishes the instant every member
            // leaves (ChannelManager destroys the object — see LeaveInternal)
            // and a fresh one is created on the next Join. A repository-
            // backed ChannelManager must survive exactly that.
            string dbPath = Path.Combine(Path.GetTempPath(), $"craftwar-motdtest-{Guid.NewGuid():N}.db");
            try
            {
                var db = new Database(dbPath);
                db.EnsureSchema();
                var channels = new ChannelManager(new ChannelMotdRepository(db));

                channels.Join(1, "grom", "Town Hall", out _);
                string failure = channels.TrySetMotd(1, "Welcome to the horde", out _);
                Assert.IsNull(failure);

                channels.Leave(1); // last member — the channel object is destroyed
                Assert.IsFalse(channels.TryGetChannelOf(1, out _));

                var recreated = channels.Join(2, "thrall", "Town Hall", out _);
                Assert.AreEqual("Welcome to the horde", recreated.Motd,
                    "a brand new ChatChannel object, but the same persisted MOTD");
            }
            finally
            {
                try { File.Delete(dbPath); } catch (IOException) { }
            }
        }

        [TestCase("Town Hall", true)]
        [TestCase("Clan/WC2", true)]
        [TestCase("a", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("this-name-is-definitely-longer-than-thirty-two-characters", false)]
        [TestCase("bad!name", false)]
        public void IsValidName_RejectsEmptyTooLongOrPunctuation(string name, bool expected)
        {
            Assert.AreEqual(expected, ChannelManager.IsValidName(name));
        }
    }
}
