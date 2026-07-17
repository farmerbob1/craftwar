using System.Collections.Generic;

namespace Craftwar.Sim
{
    /// <summary>
    /// A match is fully described by (map, seed, command log). The recorder
    /// captures tick-stamped commands; playback feeds them back at the same
    /// ticks, and the final StateHash must match — that equality is the
    /// project's core end-to-end determinism check.
    /// Byte format: "CWRP", u16 version, u64 seed, u32 mapHash, u32 count,
    /// then per entry: u32 tick + serialized GameCommand.
    /// </summary>
    public sealed class Replay
    {
        public const uint Magic = 0x50525743; // "CWRP" little-endian
        public const ushort Version = 1;

        public ulong Seed;
        public uint MapHash;
        public readonly List<(int tick, GameCommand cmd)> Entries = new List<(int, GameCommand)>();

        public void Record(int tick, in GameCommand cmd) => Entries.Add((tick, cmd));

        public byte[] ToBytes()
        {
            // Generous sizing: header + max command footprint per entry.
            var buffer = new byte[18 + Entries.Count * (16 + GameCommand.MaxSelection * 4 + 4)];
            var w = new ByteWriter(buffer);
            w.WriteUInt(Magic);
            w.WriteUShort(Version);
            w.WriteULong(Seed);
            w.WriteUInt(MapHash);
            w.WriteUInt((uint)Entries.Count);
            foreach (var (tick, cmd) in Entries)
            {
                w.WriteInt(tick);
                cmd.Write(ref w);
            }
            var result = new byte[w.Position];
            System.Array.Copy(buffer, result, w.Position);
            return result;
        }

        public static Replay FromBytes(byte[] data)
        {
            var r = new ByteReader(data);
            if (r.ReadUInt() != Magic)
                throw new System.IO.InvalidDataException("Not a Craftwar replay");
            ushort version = r.ReadUShort();
            if (version != Version)
                throw new System.IO.InvalidDataException($"Unsupported replay version {version}");
            var replay = new Replay
            {
                Seed = r.ReadULong(),
                MapHash = r.ReadUInt(),
            };
            uint count = r.ReadUInt();
            for (uint i = 0; i < count; i++)
            {
                int tick = r.ReadInt();
                replay.Entries.Add((tick, GameCommand.Read(ref r)));
            }
            return replay;
        }

        /// <summary>FNV-1a over raw map bytes, to refuse replays on the wrong map.</summary>
        public static uint HashMapBytes(byte[] mapBytes)
        {
            var h = StateHash.Begin();
            for (int i = 0; i < mapBytes.Length; i++)
                h.Add(mapBytes[i]);
            return h.Value;
        }
    }
}
