using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Craftwar.Net
{
    /// <summary>
    /// TCP is a byte stream with no message boundaries — unlike the relay's
    /// packet-oriented <see cref="IPacketPeer"/>, a single <c>Send</c> is not
    /// guaranteed to arrive as a single <c>Receive</c>. A 4-byte length
    /// prefix restores "one write = one message" so every frame this reads
    /// back out is byte-identical to what was written, never coalesced or
    /// split. Shared by <see cref="RelayPeerSocket"/> (this assembly) and
    /// Craftwar.NetServer's connection handler (which compiles this file by
    /// source, the same way the standalone Sim test harness does).
    /// </summary>
    public static class StreamFraming
    {
        public const int MaxFrameBytes = 1 << 20; // 1 MiB — generous for control messages

        public static async Task WriteFrameAsync(Stream stream, byte[] payload, int length,
            CancellationToken ct = default)
        {
            var header = new byte[4];
            header[0] = (byte)length;
            header[1] = (byte)(length >> 8);
            header[2] = (byte)(length >> 16);
            header[3] = (byte)(length >> 24);
            await stream.WriteAsync(header, 0, 4, ct).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Null return means the stream closed cleanly between
        /// frames (0 bytes read on the header) — the caller's read loop
        /// should stop, not treat it as an error.</summary>
        public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken ct = default)
        {
            var header = new byte[4];
            if (!await ReadExactAsync(stream, header, 4, ct).ConfigureAwait(false))
                return null;
            int length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (length < 0 || length > MaxFrameBytes)
                throw new InvalidDataException($"frame length {length} out of range");
            var payload = new byte[length];
            if (!await ReadExactAsync(stream, payload, length, ct).ConfigureAwait(false))
                throw new EndOfStreamException("connection closed mid-frame");
            return payload;
        }

        /// <summary>False only if the connection closed before any byte of
        /// this read arrived — a clean "nothing more is coming" rather than
        /// a truncated frame.</summary>
        static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer, read, count - read, ct).ConfigureAwait(false);
                if (n == 0)
                    return read == 0 ? false : throw new EndOfStreamException("connection closed mid-frame");
                read += n;
            }
            return true;
        }
    }
}
