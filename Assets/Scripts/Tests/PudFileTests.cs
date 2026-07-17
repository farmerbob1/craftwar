using System.IO;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class PudFileTests
    {
        // Local BNE install; tests that need it are skipped when absent so the
        // suite still runs on machines without Warcraft 2 data.
        const string BneMapsDir = @"C:\Program Files (x86)\Warcraft II Remastered\x86\Maps";

        static byte[] LoadBneMap(string name)
        {
            string path = Path.Combine(BneMapsDir, name);
            if (!File.Exists(path))
                Assert.Ignore($"BNE map not present: {path}");
            return File.ReadAllBytes(path);
        }

        [Test]
        public void Parses_RealBneMap_CoreSections()
        {
            var pud = PudFile.Parse(LoadBneMap("Gold Rush BNE.pud"));

            Assert.IsTrue(pud.Width is 32 or 64 or 96 or 128, $"invalid width {pud.Width}");
            Assert.AreEqual(pud.Width, pud.Height, "BNE melee maps are square");
            Assert.AreEqual(pud.Width * pud.Height, pud.Tiles.Length);
            Assert.AreEqual(pud.Width * pud.Height, pud.MoveMap.Length);
            Assert.IsNotEmpty(pud.Units);

            // Every unit must be inside the map and owned by a valid slot.
            foreach (var u in pud.Units)
            {
                Assert.Less(u.X, pud.Width);
                Assert.Less(u.Y, pud.Height);
                Assert.IsTrue(u.Owner < 8 || u.Owner == 15, $"bad owner {u.Owner}");
            }

            // A melee map has player start locations (0x5e human / 0x5f orc).
            int startLocations = 0;
            foreach (var u in pud.Units)
                if (u.Type == 0x5e || u.Type == 0x5f)
                    startLocations++;
            Assert.GreaterOrEqual(startLocations, 2);

            // Gold mines carry their resource amount in Alter (x2500 units).
            int mines = 0;
            foreach (var u in pud.Units)
                if (u.Type == 0x5c)
                {
                    mines++;
                    Assert.Greater(u.Alter, 0, "gold mine without gold");
                }
            Assert.Greater(mines, 0, "melee map should have gold mines");
        }

        [Test]
        public void Parses_AllBneMaps_WithoutError()
        {
            if (!Directory.Exists(BneMapsDir))
                Assert.Ignore("BNE maps folder not present");
            string[] puds = Directory.GetFiles(BneMapsDir, "*.pud");
            Assert.IsNotEmpty(puds);
            foreach (string path in puds)
            {
                var pud = PudFile.Parse(File.ReadAllBytes(path));
                Assert.Greater(pud.Tiles.Length, 0, Path.GetFileName(path));
            }
        }

        [Test]
        public void Rejects_NonPudData()
        {
            Assert.Throws<PudFormatException>(() => PudFile.Parse(new byte[64]));
        }
    }
}
