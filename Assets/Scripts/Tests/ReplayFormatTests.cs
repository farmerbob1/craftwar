using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>Replay header serialization, including the v2 per-slot AI strategy
    /// hashes and backward-compatible reading of a v1 blob. Pure — runs standalone.</summary>
    public class ReplayFormatTests
    {
        [Test]
        public void V2_RoundTripsSeedMapHashAiHashesAndCommands()
        {
            var replay = new Replay { Seed = 0xDEADBEEFCAFEUL, MapHash = 0x12345678 };
            replay.SetAiStrategyHash(1, 0xAABBCCDD);
            replay.SetAiStrategyHash(3, 0x01020304);
            replay.Record(4, new GameCommand { Op = CommandOp.Stop, Player = 1, SelectionCount = 0 });
            replay.Record(8, new GameCommand { Op = CommandOp.Surrender, Player = 3, SelectionCount = 0 });

            var back = Replay.FromBytes(replay.ToBytes());

            Assert.AreEqual(replay.Seed, back.Seed);
            Assert.AreEqual(replay.MapHash, back.MapHash);
            CollectionAssert.AreEqual(replay.AiStrategyHashes, back.AiStrategyHashes);
            Assert.AreEqual(2, back.Entries.Count);
            Assert.AreEqual(4, back.Entries[0].tick);
            Assert.AreEqual(CommandOp.Stop, back.Entries[0].cmd.Op);
            Assert.AreEqual(8, back.Entries[1].tick);
        }

        [Test]
        public void V1Blob_ReadsWithEmptyAiHashes()
        {
            // Hand-build a legacy v1 header: magic, u16 1, u64 seed, u32 mapHash, u32 count=0.
            var buf = new byte[64];
            var w = new ByteWriter(buf);
            w.WriteUInt(Replay.Magic);
            w.WriteUShort(1);
            w.WriteULong(777);
            w.WriteUInt(0x99);
            w.WriteUInt(0); // command count
            var bytes = new byte[w.Position];
            System.Array.Copy(buf, bytes, w.Position);

            var back = Replay.FromBytes(bytes);
            Assert.AreEqual(777UL, back.Seed);
            Assert.AreEqual(0x99u, back.MapHash);
            Assert.AreEqual(0, back.Entries.Count);
            for (int p = 0; p < back.AiStrategyHashes.Length; p++)
                Assert.AreEqual(0u, back.AiStrategyHashes[p], "v1 has no AI hashes");
        }
    }
}
