using System.Collections.Generic;
using Craftwar.Net;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The wire format. The loopback tests cover the protocol's behaviour; these
    /// cover the bytes, which is the other half — a peer on another machine only
    /// ever sees these.
    /// </summary>
    public class NetMessageTests
    {
        [Test]
        public unsafe void TurnInput_RoundTripsThroughBytes()
        {
            var sent = new List<GameCommand>
            {
                new GameCommand { Op = CommandOp.Move, Player = 2, TargetX = 40, TargetY = 41, SelectionCount = 2 },
                new GameCommand { Op = CommandOp.Stop, Player = 2 },
            };
            var first = sent[0];
            first.Selection.Ids[0] = new UnitId(5, 1).Packed;
            first.Selection.Ids[1] = new UnitId(9, 3).Packed;
            sent[0] = first;

            var w = new ByteWriter(32);
            NetMessages.WriteTurnInput(ref w, slot: 2, turn: 77, sent, hashTurn: 75, stateHash: 0xFEEDFACE);

            var r = new ByteReader(w.ToArray());
            Assert.AreEqual((byte)NetMessageKind.TurnInput, r.ReadByte());
            var got = new List<GameCommand>();
            NetMessages.ReadTurnInput(ref r, out byte slot, out int turn, got, out int hashTurn, out uint hash);

            Assert.AreEqual(2, slot);
            Assert.AreEqual(77, turn);
            Assert.AreEqual(75, hashTurn);
            Assert.AreEqual(0xFEEDFACEu, hash);
            Assert.AreEqual(2, got.Count);
            Assert.AreEqual(CommandOp.Move, got[0].Op);
            var back = got[0]; // fixed buffers can't be read off an indexer's return value
            Assert.AreEqual(2, back.SelectionCount);
            Assert.AreEqual(new UnitId(5, 1).Packed, back.Selection.Ids[0]);
            Assert.AreEqual(new UnitId(9, 3).Packed, back.Selection.Ids[1]);
            Assert.AreEqual(CommandOp.Stop, got[1].Op);
        }

        [Test]
        public void TurnCommit_RoundTripsThroughBytes()
        {
            var sent = new List<GameCommand>
            {
                new GameCommand { Op = CommandOp.Train, Player = 0, Param = 12 },
                new GameCommand { Op = CommandOp.Research, Player = 1, Param = 7 },
            };
            var w = new ByteWriter(16);
            NetMessages.WriteTurnCommit(ref w, 5, sent);

            var r = new ByteReader(w.ToArray());
            Assert.AreEqual((byte)NetMessageKind.TurnCommit, r.ReadByte());
            var got = new List<GameCommand>();
            NetMessages.ReadTurnCommit(ref r, out int turn, got);

            Assert.AreEqual(5, turn);
            Assert.AreEqual(2, got.Count);
            Assert.AreEqual(12, got[0].Param);
            Assert.AreEqual(1, got[1].Player);
        }

        [Test]
        public void EmptyTurn_IsStillAValidMessage()
        {
            // Most turns carry no commands at all; the common case must not be
            // the one that breaks.
            var w = new ByteWriter(4);
            NetMessages.WriteTurnCommit(ref w, 0, new List<GameCommand>());
            var r = new ByteReader(w.ToArray());
            r.ReadByte();
            var got = new List<GameCommand> { new GameCommand { Op = CommandOp.Stop } };
            NetMessages.ReadTurnCommit(ref r, out int turn, got);
            Assert.AreEqual(0, turn);
            Assert.AreEqual(0, got.Count, "the reader clears whatever it was handed");
        }

        [Test]
        public void JoinRequest_RoundTripsIdentityAndName()
        {
            var identity = new BuildIdentity
            {
                ProtocolVersion = BuildIdentity.CurrentProtocolVersion,
                SimVersion = SimConstants.SimVersion,
                MapHash = 0x11112222,
                RulesHash = 0x33334444,
                AiProfileHash = 0x55556666,
            };
            var w = new ByteWriter(8);
            NetMessages.WriteJoinRequest(ref w, identity, "Grom");

            var r = new ByteReader(w.ToArray());
            Assert.AreEqual((byte)NetMessageKind.JoinRequest, r.ReadByte());
            NetMessages.ReadJoinRequest(ref r, out var back, out string name);

            Assert.AreEqual("Grom", name);
            Assert.AreEqual(JoinRejectReason.None, identity.CompareTo(back));
        }

        [Test]
        public void MismatchedBuilds_AreRejectedNamingTheFirstDifference()
        {
            var host = new BuildIdentity
            {
                ProtocolVersion = 1, SimVersion = 1,
                MapHash = 0xAAAA, RulesHash = 0xBBBB, AiProfileHash = 0xCCCC,
            };

            var differentMap = host; differentMap.MapHash = 0xDEAD;
            Assert.AreEqual(JoinRejectReason.MapMismatch, host.CompareTo(differentMap),
                "two players picking 'the same map' can easily load different files");

            var differentRules = host; differentRules.RulesHash = 0xDEAD;
            Assert.AreEqual(JoinRejectReason.RulesMismatch, host.CompareTo(differentRules));

            var oldBuild = host; oldBuild.SimVersion = 0; oldBuild.MapHash = 0xDEAD;
            Assert.AreEqual(JoinRejectReason.SimVersion, host.CompareTo(oldBuild),
                "the most fundamental mismatch is the one reported");

            Assert.AreEqual(JoinRejectReason.None, host.CompareTo(host));
        }

        [Test]
        public void RulesHash_ReactsToAMapsStatOverrides()
        {
            // The hash has to be taken from the LIVE ruleset, after map overrides,
            // or a custom-balance map would pass the handshake and then desync.
            var a = RuleSet.CreateDefault();
            var b = RuleSet.CreateDefault();
            Assert.AreEqual(a.Hash(), b.Hash(), "identical tables hash identically");

            b.UnitType(UnitTypeId.Footman).Hp += 1;
            Assert.AreNotEqual(a.Hash(), b.Hash(), "a one-value difference must be visible");
        }

        [Test]
        public void DesyncHalt_RoundTrips()
        {
            var report = new DesyncReport(412, 3, 0xABCDEF01, 0x0123ABCD);
            var w = new ByteWriter(8);
            NetMessages.WriteDesyncHalt(ref w, report);

            var r = new ByteReader(w.ToArray());
            Assert.AreEqual((byte)NetMessageKind.DesyncHalt, r.ReadByte());
            var back = NetMessages.ReadDesyncHalt(ref r);

            Assert.AreEqual(412, back.Turn);
            Assert.AreEqual(3, back.Slot);
            Assert.AreEqual(0xABCDEF01u, back.ExpectedHash);
            Assert.AreEqual(0x0123ABCDu, back.ActualHash);
        }

        [Test]
        public void PauseAndResume_AreIgnoredByTheSim()
        {
            // Pause is a driver concern. If the sim reacted to it at all, the tick
            // a replay resumed on would depend on when somebody paused.
            var pud = AiTestHarness.TwoBaseMap();
            var sim = AiTestHarness.Boot(pud, seed: 3);
            for (int i = 0; i < 20; i++)
                sim.Advance(new List<GameCommand>());

            uint before = sim.State.ComputeHash();
            sim.Advance(new List<GameCommand>
            {
                new GameCommand { Op = CommandOp.Pause, Player = 0 },
                new GameCommand { Op = CommandOp.Resume, Player = 1 },
            });
            var control = AiTestHarness.Boot(pud, seed: 3);
            for (int i = 0; i < 21; i++)
                control.Advance(new List<GameCommand>());

            Assert.AreNotEqual(before, sim.State.ComputeHash(), "the tick still advanced");
            Assert.AreEqual(control.State.ComputeHash(), sim.State.ComputeHash(),
                "a tick carrying Pause/Resume is indistinguishable from an empty one");
        }
    }
}
