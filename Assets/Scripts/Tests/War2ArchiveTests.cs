using System.IO;
using Craftwar.Import.War2;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class War2ArchiveTests
    {
        const string MaindatPath =
            @"C:\Users\mattc\Desktop\Warcraft shit\war2tools-master\data\maindat.war";

        static War2Archive OpenMaindat()
        {
            if (!File.Exists(MaindatPath))
                Assert.Ignore($"maindat.war not present: {MaindatPath}");
            return new War2Archive(File.ReadAllBytes(MaindatPath));
        }

        [Test]
        public void Opens_Maindat_AndReadsEntryTable()
        {
            var war = OpenMaindat();
            Assert.Greater(war.EntryCount, 400, "expansion maindat has 400+ entries");
        }

        [Test]
        public void ForestPalette_DecodesWithTransparentIndex0()
        {
            var war = OpenMaindat();
            var entry = war.ExtractEntry(War2Palette.EntryForEra(PudEra.Forest));
            Assert.AreEqual(768, entry.Length, "palette entry must be 256*3 bytes");

            var palette = War2Palette.Decode(entry);
            Assert.AreEqual(0, palette[0].A, "index 0 transparent");
            Assert.AreEqual(255, palette[1].A);

            // 6-bit source shifted left 2: every component is a multiple of 4.
            for (int i = 0; i < 256; i++)
            {
                Assert.AreEqual(0, palette[i].R % 4);
                Assert.AreEqual(0, palette[i].G % 4);
                Assert.AreEqual(0, palette[i].B % 4);
            }
        }

        [TestCase(PudEra.Forest)]
        [TestCase(PudEra.Winter)]
        [TestCase(PudEra.Wasteland)]
        [TestCase(PudEra.Swamp)]
        public void Tileset_DecodesTilesForEveryEra(PudEra era)
        {
            var war = OpenMaindat();
            var tileset = War2Tileset.Load(war, era);
            var tiles = tileset.DecodeAll();

            Assert.Greater(tiles.Count, 300, $"{era} should decode hundreds of tiles");
            foreach (var t in tiles)
                Assert.AreEqual(32 * 32 * 4, t.Pixels.Length);

            // Spec worked example: MTXM id 0x0052 (solid ground) must exist.
            bool found0052 = false;
            foreach (var t in tiles)
                if (t.TileId == 0x0052)
                    found0052 = true;
            Assert.IsTrue(found0052, "tile 0x0052 (doc worked example) missing");
        }

        [Test]
        public void DecodedGroundTile_IsFullyOpaque()
        {
            var war = OpenMaindat();
            var tiles = War2Tileset.Load(war, PudEra.Forest).DecodeAll();
            foreach (var t in tiles)
            {
                if (t.TileId != 0x0052)
                    continue;
                for (int px = 0; px < t.Pixels.Length; px += 4)
                    Assert.AreEqual(255, t.Pixels[px + 3], "terrain must not have holes");
                return;
            }
            Assert.Fail("tile 0x0052 not found");
        }
    }
}
