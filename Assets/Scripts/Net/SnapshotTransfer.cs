using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Craftwar.Net
{
    /// <summary>
    /// Turns a <see cref="Craftwar.Sim.SimSerializer"/> snapshot into a paced
    /// sequence of small chunks for the rejoin handshake, and back again.
    /// Deflate first: a running match's snapshot is mostly repetitive terrain
    /// and zeroed unit slots, so compressing before chunking meaningfully cuts
    /// the number of packets a slow link has to carry.
    /// </summary>
    public static class SnapshotTransfer
    {
        /// <summary>~1200 B keeps every chunk comfortably under typical MTU
        /// once framing overhead is added.</summary>
        public const int ChunkSize = 1200;

        public static byte[] Compress(byte[] raw)
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
                deflate.Write(raw, 0, raw.Length);
            return output.ToArray();
        }

        public static byte[] Decompress(byte[] compressed, int expectedLength)
        {
            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            var result = new byte[expectedLength];
            int total = 0;
            while (total < expectedLength)
            {
                int read = deflate.Read(result, total, expectedLength - total);
                if (read <= 0)
                    throw new EndOfStreamException(
                        $"snapshot decompressed short: got {total}, expected {expectedLength}");
                total += read;
            }
            return result;
        }

        /// <summary>Split already-compressed bytes into ChunkSize pieces.</summary>
        public static List<(int offset, int count)> PlanChunks(int totalLength)
        {
            var plan = new List<(int, int)>();
            for (int offset = 0; offset < totalLength; offset += ChunkSize)
                plan.Add((offset, Math.Min(ChunkSize, totalLength - offset)));
            if (plan.Count == 0)
                plan.Add((0, 0)); // an (unlikely) empty snapshot still gets one chunk
            return plan;
        }

        /// <summary>Reassembles chunks received out of order. Not thread-safe;
        /// one per in-flight rejoin.</summary>
        public sealed class Reassembler
        {
            readonly byte[][] _chunks;
            int _received;

            public Reassembler(int chunkCount) => _chunks = new byte[chunkCount][];

            public bool Complete => _received == _chunks.Length;

            public void Add(int index, byte[] data)
            {
                if (index < 0 || index >= _chunks.Length)
                    return; // stale or malformed — ignore rather than throw on an unreliable link
                if (_chunks[index] != null)
                    return; // duplicate
                _chunks[index] = data;
                _received++;
            }

            public byte[] Build()
            {
                int total = 0;
                for (int i = 0; i < _chunks.Length; i++)
                    total += _chunks[i].Length;
                var result = new byte[total];
                int pos = 0;
                for (int i = 0; i < _chunks.Length; i++)
                {
                    Array.Copy(_chunks[i], 0, result, pos, _chunks[i].Length);
                    pos += _chunks[i].Length;
                }
                return result;
            }
        }
    }
}
