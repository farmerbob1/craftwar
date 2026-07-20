using System;

namespace Craftwar.Import
{
    /// <summary>Decoded PCM, ready to hand to AudioClip.SetData after scaling.</summary>
    public struct WavData
    {
        public int SampleRate;
        public int Channels;

        /// <summary>Interleaved samples, normalised to [-1, 1].</summary>
        public float[] Samples;

        /// <summary>Per-channel frame count — what AudioClip.Create expects.</summary>
        public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;
    }

    /// <summary>
    /// RIFF/WAVE reader for the installation's loose audio.
    ///
    /// This is the whole of what was once scoped as "the real sound decoder".
    /// A sweep of maindat.war found only 5 WAVs and 51 XMI tracks — the SFX
    /// corpus was never in that archive — while the install ships all 456 sound
    /// effects and 33 music tracks as plain uncompressed PCM. So no format work
    /// is needed, only a correct chunk walk.
    ///
    /// Chunks are located by id rather than by assuming fmt is followed by data:
    /// LIST/INFO and fact chunks legitimately sit between them, and the original
    /// game's own RIFF.C walks the same way.
    ///
    /// UnityEngine-free on purpose, so it runs in the standalone test harness.
    /// </summary>
    public static class RiffWav
    {
        const ushort FormatPcm = 1;
        const ushort FormatFloat = 3;

        public static WavData Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new WavFormatException("too short to be a RIFF file");
            if (Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE")
                throw new WavFormatException("not a RIFF/WAVE file");

            int fmtOffset = -1, fmtSize = 0;
            int dataOffset = -1, dataSize = 0;

            int p = 12;
            while (p + 8 <= bytes.Length)
            {
                string id = Tag(bytes, p);
                int size = ReadI32(bytes, p + 4);
                int body = p + 8;
                if (size < 0 || body + size > bytes.Length)
                {
                    // Truncated final chunk: salvage what is actually there
                    // rather than discarding an otherwise playable file.
                    size = bytes.Length - body;
                    if (size <= 0)
                        break;
                }

                if (id == "fmt ") { fmtOffset = body; fmtSize = size; }
                else if (id == "data") { dataOffset = body; dataSize = size; }

                // Chunks are word-aligned: odd sizes carry a pad byte.
                p = body + size + (size & 1);
            }

            if (fmtOffset < 0) throw new WavFormatException("no fmt chunk");
            if (dataOffset < 0) throw new WavFormatException("no data chunk");
            if (fmtSize < 16) throw new WavFormatException("fmt chunk too small");

            ushort format = ReadU16(bytes, fmtOffset);
            int channels = ReadU16(bytes, fmtOffset + 2);
            int sampleRate = ReadI32(bytes, fmtOffset + 4);
            int bits = ReadU16(bytes, fmtOffset + 14);

            if (channels <= 0) throw new WavFormatException($"bad channel count {channels}");
            if (sampleRate <= 0) throw new WavFormatException($"bad sample rate {sampleRate}");

            float[] samples = format switch
            {
                FormatPcm => DecodePcm(bytes, dataOffset, dataSize, bits),
                FormatFloat when bits == 32 => DecodeFloat32(bytes, dataOffset, dataSize),
                _ => throw new WavFormatException($"unsupported format {format} at {bits} bits"),
            };

            // Drop a trailing partial frame rather than emitting a lopsided
            // interleaved buffer that would desynchronise the channels.
            int usable = samples.Length - samples.Length % channels;
            if (usable != samples.Length)
                Array.Resize(ref samples, usable);

            return new WavData { SampleRate = sampleRate, Channels = channels, Samples = samples };
        }

        static float[] DecodePcm(byte[] b, int offset, int size, int bits)
        {
            switch (bits)
            {
                case 8:
                {
                    // 8-bit PCM is unsigned, centred on 128 — unlike every wider size.
                    var outp = new float[size];
                    for (int i = 0; i < size; i++)
                        outp[i] = (b[offset + i] - 128) / 128f;
                    return outp;
                }
                case 16:
                {
                    int n = size / 2;
                    var outp = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        short s = (short)(b[offset + i * 2] | (b[offset + i * 2 + 1] << 8));
                        outp[i] = s / 32768f;
                    }
                    return outp;
                }
                case 24:
                {
                    int n = size / 3;
                    var outp = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int o = offset + i * 3;
                        int s = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
                        if ((s & 0x800000) != 0)
                            s |= unchecked((int)0xFF000000); // sign-extend
                        outp[i] = s / 8388608f;
                    }
                    return outp;
                }
                case 32:
                {
                    int n = size / 4;
                    var outp = new float[n];
                    for (int i = 0; i < n; i++)
                        outp[i] = ReadI32(b, offset + i * 4) / 2147483648f;
                    return outp;
                }
                default:
                    throw new WavFormatException($"unsupported PCM bit depth {bits}");
            }
        }

        static float[] DecodeFloat32(byte[] b, int offset, int size)
        {
            int n = size / 4;
            var outp = new float[n];
            for (int i = 0; i < n; i++)
                outp[i] = BitConverter.ToSingle(b, offset + i * 4);
            return outp;
        }

        static string Tag(byte[] b, int o) =>
            new string(new[] { (char)b[o], (char)b[o + 1], (char)b[o + 2], (char)b[o + 3] });

        static int ReadI32(byte[] b, int o) =>
            b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

        static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
    }

    public sealed class WavFormatException : Exception
    {
        public WavFormatException(string message) : base(message) { }
    }
}
