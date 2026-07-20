using System;
using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class RiffWavTests
    {
        /// <summary>Build a WAV in memory, optionally with extra chunks between fmt and data.</summary>
        static byte[] MakeWav(int sampleRate, int channels, int bits, byte[] data,
                              bool extraChunk = false, ushort format = 1)
        {
            var fmt = new List<byte>();
            fmt.AddRange(BitConverter.GetBytes(format));
            fmt.AddRange(BitConverter.GetBytes((ushort)channels));
            fmt.AddRange(BitConverter.GetBytes(sampleRate));
            fmt.AddRange(BitConverter.GetBytes(sampleRate * channels * bits / 8)); // byte rate
            fmt.AddRange(BitConverter.GetBytes((ushort)(channels * bits / 8)));    // block align
            fmt.AddRange(BitConverter.GetBytes((ushort)bits));

            var body = new List<byte>();
            body.AddRange(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            body.AddRange(System.Text.Encoding.ASCII.GetBytes("fmt "));
            body.AddRange(BitConverter.GetBytes(fmt.Count));
            body.AddRange(fmt);

            if (extraChunk)
            {
                // LIST/INFO legitimately sits between fmt and data.
                var info = System.Text.Encoding.ASCII.GetBytes("INFOhello!!!");
                body.AddRange(System.Text.Encoding.ASCII.GetBytes("LIST"));
                body.AddRange(BitConverter.GetBytes(info.Length));
                body.AddRange(info);
            }

            body.AddRange(System.Text.Encoding.ASCII.GetBytes("data"));
            body.AddRange(BitConverter.GetBytes(data.Length));
            body.AddRange(data);

            var all = new List<byte>();
            all.AddRange(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            all.AddRange(BitConverter.GetBytes(body.Count));
            all.AddRange(body);
            return all.ToArray();
        }

        [Test]
        public void Decodes16BitMono()
        {
            // -32768, 0, 32767
            var data = new byte[] { 0x00, 0x80, 0x00, 0x00, 0xFF, 0x7F };
            var wav = RiffWav.Decode(MakeWav(22050, 1, 16, data));

            Assert.AreEqual(22050, wav.SampleRate);
            Assert.AreEqual(1, wav.Channels);
            Assert.AreEqual(3, wav.FrameCount);
            Assert.AreEqual(-1f, wav.Samples[0], 1e-6);
            Assert.AreEqual(0f, wav.Samples[1], 1e-6);
            Assert.AreEqual(1f, wav.Samples[2], 1e-4);
        }

        [Test]
        public void Decodes8BitPcm_AsUnsignedCentredOn128()
        {
            // The one real trap in PCM: 8-bit is unsigned, every wider size signed.
            var wav = RiffWav.Decode(MakeWav(11025, 1, 8, new byte[] { 0, 128, 255 }));
            Assert.AreEqual(-1f, wav.Samples[0], 1e-6);
            Assert.AreEqual(0f, wav.Samples[1], 1e-6);
            Assert.AreEqual(0.9921875f, wav.Samples[2], 1e-6);
        }

        [Test]
        public void DecodesStereo_Interleaved()
        {
            var data = new byte[] { 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80, 0x00, 0x00 };
            var wav = RiffWav.Decode(MakeWav(44100, 2, 16, data));
            Assert.AreEqual(2, wav.Channels);
            Assert.AreEqual(2, wav.FrameCount, "four samples over two channels is two frames");
        }

        [Test]
        public void SkipsInterveningChunks()
        {
            var wav = RiffWav.Decode(
                MakeWav(22050, 1, 16, new byte[] { 0x00, 0x40 }, extraChunk: true));
            Assert.AreEqual(1, wav.FrameCount, "a LIST chunk before data must not be mistaken for it");
            Assert.AreEqual(0.5f, wav.Samples[0], 1e-4);
        }

        [Test]
        public void DropsTrailingPartialFrame()
        {
            // Three 16-bit samples in a stereo file: the odd one would otherwise
            // swap the channels for everything after it.
            var data = new byte[] { 0, 0, 0, 0, 0, 0 };
            var wav = RiffWav.Decode(MakeWav(22050, 2, 16, data));
            Assert.AreEqual(2, wav.Samples.Length % 2 == 0 ? wav.Samples.Length : -1);
        }

        [TestCase(new byte[] { 1, 2, 3 })]
        [TestCase(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, 0, 0, 0, 0 })]
        public void RejectsNonWave(byte[] bytes)
        {
            Assert.Throws<WavFormatException>(() => RiffWav.Decode(bytes));
        }

        [Test]
        public void RejectsNull()
        {
            Assert.Throws<WavFormatException>(() => RiffWav.Decode(null));
        }

        [Test]
        public void RejectsUnsupportedCompression()
        {
            // format 2 = MS ADPCM; nothing in the install uses it, but a wrong
            // file should say so rather than emit noise.
            Assert.Throws<WavFormatException>(() =>
                RiffWav.Decode(MakeWav(22050, 1, 4, new byte[] { 1, 2 }, format: 2)));
        }

        // ---------- the real installation ----------

        const string DataRoot = @"C:\Program Files (x86)\Warcraft II Remastered\x86\Data";

        [Test]
        public void DecodesEveryGameSoundEffect()
        {
            string root = Path.Combine(DataRoot, "Gamesfx");
            if (!Directory.Exists(root))
                Assert.Ignore("WC2 install not present on this machine");

            int count = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*.wav", SearchOption.AllDirectories))
            {
                WavData wav;
                try { wav = RiffWav.Decode(File.ReadAllBytes(path)); }
                catch (WavFormatException e) { Assert.Fail($"{Path.GetFileName(path)}: {e.Message}"); continue; }

                Assert.Greater(wav.SampleRate, 0, path);
                Assert.Greater(wav.Channels, 0, path);
                Assert.Greater(wav.FrameCount, 0, $"{path} decoded to no audio");

                // Everything in Gamesfx is 22 kHz mono 16-bit; a surprise here
                // would mean the format assumption is wrong somewhere.
                Assert.AreEqual(1, wav.Channels, $"{path} is not mono");

                foreach (var s in wav.Samples)
                    if (s < -1.001f || s > 1.001f)
                        Assert.Fail($"{path}: sample {s} outside [-1,1]");
                count++;
            }

            Assert.Greater(count, 350, "expected ~399 game sound effects");
        }
    }
}
