using System.IO;
using Craftwar.App;
using Craftwar.Import;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Coverage for the decode primitives the Editor-time importer
    /// (<c>Craftwar.EditorTools.Wc2AssetImporter</c>) bakes from — the runtime
    /// no longer touches any of this (see the Baked* classes in Craftwar.App),
    /// but a fresh install must still decode cleanly for the bake to work.
    /// Install-gated like the rest of this project's "real data" tests.
    /// </summary>
    public class ImportFlowTests
    {
        [Test]
        public void FreshMachine_FindsInstall_SavesPointer_AndReloadsIt()
        {
            var found = Wc2InstallLocator.Find();
            if (found.Count == 0 || !found[0].IsUsable)
                Assert.Ignore("no WC2 install on this machine");

            var best = found[0];

            // What the (still-present, now vestigial) locate-install wizard writes.
            var paths = new LocalAssetPaths { dataRoot = best.DataRoot };
            var mapFolders = Wc2InstallLocator.MapFolders(best.DataRoot);
            if (mapFolders.Count > 0)
                paths.mapsDir = mapFolders[0];

            string temp = Path.Combine(Path.GetTempPath(),
                "craftwar-import-" + Path.GetRandomFileName() + ".json");
            try
            {
                paths.Save(temp);
                Assert.IsTrue(File.Exists(temp));

                var reloaded = UnityEngine.JsonUtility.FromJson<LocalAssetPaths>(
                    File.ReadAllText(temp));
                Assert.AreEqual(best.DataRoot, reloaded.dataRoot);
                Assert.AreEqual(LocalAssetPaths.CurrentSchema, reloaded.schema);
                Assert.IsTrue(reloaded.HasData);

                // And the pointer actually yields a working source.
                var source = reloaded.CreateAssetSource();
                Assert.IsNotNull(source);
                Assert.IsTrue(source.Exists(Wc2InstallLocator.ProbeTileset));
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        [Test]
        public void DiscoveredInstall_ServesEveryAssetClass()
        {
            var found = Wc2InstallLocator.Find();
            if (found.Count == 0 || !found[0].IsUsable)
                Assert.Ignore("no WC2 install on this machine");

            var source = new LooseFileAssetSource(found[0].DataRoot);

            // Terrain.
            var tileset = Craftwar.Import.War2.War2Tileset.Load(source, PudEra.Forest);
            Assert.IsNotNull(tileset, "tileset");
            Assert.Greater(tileset.DecodeAll().Count, 300, "decoded tiles");

            // Units.
            string sprite = Craftwar.Import.War2.War2Sprites.FileForUnit(
                (ushort)UnitTypeId.Footman, PudEra.Forest);
            Assert.IsNotNull(sprite);
            Assert.IsTrue(source.TryRead("art/unit/" + sprite.ToLowerInvariant(), out var grp));
            Assert.Greater(Craftwar.Import.War2.War2Sprites.Decode(grp).FrameCount, 0);

            // Sound.
            Assert.IsTrue(source.TryRead(Wc2SoundCatalog.BldgMineCollapse, out var wav));
            Assert.Greater(RiffWav.Decode(wav).FrameCount, 0);

            // Music.
            Assert.IsTrue(source.TryRead("music/human1_r.wav", out var musicWav));
            Assert.Greater(RiffWav.Decode(musicWav).FrameCount, 0);

            // Names: the raw JSON decode BakedStringTable's bake step relies on
            // (Wc2StringTable, the old runtime wrapper, is retired).
            Assert.IsTrue(source.TryRead("strings/enus.json", out var stringsJson));
            var strings = JsonValue.Parse(System.Text.Encoding.UTF8.GetString(stringsJson)).ToStringMap();
            Assert.AreEqual("Footman", strings["unit_0"]);

            // Icons.
            Assert.IsTrue(source.TryRead("art/classic/hud/portrait-face.json", out var atlasJson));
            var frames = TexturePackerAtlas.Parse(
                System.Text.Encoding.UTF8.GetString(atlasJson));
            Assert.AreEqual(784, frames.Count);

            // Maps, so a discovered install can actually start a match.
            var mapFolders = Wc2InstallLocator.MapFolders(found[0].DataRoot);
            Assert.Greater(mapFolders.Count, 0, "no map folder found");
            Assert.Greater(Directory.GetFiles(mapFolders[0], "*.pud").Length, 0, "no .pud maps");
        }
    }
}
