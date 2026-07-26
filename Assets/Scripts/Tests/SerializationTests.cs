using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class SerializationTests
    {
        [Test]
        public unsafe void GameCommand_RoundTripsThroughBytes()
        {
            var cmd = new GameCommand
            {
                Op = CommandOp.AttackMove,
                Player = 3,
                TargetX = 100,
                TargetY = 77,
                TargetUnit = new UnitId(250, 7).Packed,
                Param = 0x0042,
                SelectionCount = 3,
            };
            cmd.Selection.Ids[0] = new UnitId(1, 1).Packed;
            cmd.Selection.Ids[1] = new UnitId(2, 5).Packed;
            cmd.Selection.Ids[2] = new UnitId(900, 2).Packed;

            var buffer = new byte[256];
            var w = new ByteWriter(buffer);
            cmd.Write(ref w);

            var r = new ByteReader(buffer, w.Position);
            var back = GameCommand.Read(ref r);

            Assert.AreEqual(cmd.Op, back.Op);
            Assert.AreEqual(cmd.Player, back.Player);
            Assert.AreEqual(cmd.TargetX, back.TargetX);
            Assert.AreEqual(cmd.TargetY, back.TargetY);
            Assert.AreEqual(cmd.TargetUnit, back.TargetUnit);
            Assert.AreEqual(cmd.Param, back.Param);
            Assert.AreEqual(cmd.SelectionCount, back.SelectionCount);
            for (int i = 0; i < cmd.SelectionCount; i++)
                Assert.AreEqual(cmd.Selection.Ids[i], back.Selection.Ids[i]);
            Assert.AreEqual(w.Position, r.Position, "reader must consume exactly what writer produced");
        }

        [Test]
        public void ByteReader_ThrowsOnOverrun()
        {
            var r = new ByteReader(new byte[2]);
            r.ReadUShort();
            Assert.Throws<System.IO.EndOfStreamException>(() => r.ReadByte());
        }

        [Test]
        public void ByteWriter_GrowsPastItsInitialBuffer()
        {
            // One byte of capacity, then far more than that written: the writer
            // must reallocate rather than throw. Variable-size payloads (state
            // snapshots, turn packets) depend on this.
            var w = new ByteWriter(new byte[1]);
            for (int i = 0; i < 100; i++)
                w.WriteUInt((uint)i);

            Assert.AreEqual(400, w.Position);
            var bytes = w.ToArray();
            Assert.AreEqual(400, bytes.Length);

            var r = new ByteReader(bytes);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual((uint)i, r.ReadUInt(), $"value {i} survived the growth");
            Assert.AreEqual(bytes.Length, r.Position);
        }

        [Test]
        public void ByteWriter_BlockRoundTrips()
        {
            var payload = new byte[300];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i * 7);

            var w = new ByteWriter(8);
            w.WriteUInt(0xDEADBEEF);
            w.WriteBytes(payload, 0, payload.Length);
            w.WriteByte(0x5A);

            var r = new ByteReader(w.ToArray());
            Assert.AreEqual(0xDEADBEEF, r.ReadUInt());
            Assert.AreEqual(payload, r.ReadBytes());
            Assert.AreEqual(0x5A, r.ReadByte());
        }
    }
}
