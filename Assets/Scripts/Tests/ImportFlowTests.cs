using System.IO;
using Craftwar.App;
using Craftwar.Import;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The M8 success criterion that the old pipeline provably failed: a machine
    /// with no configuration at all can find the game, record where it is, and
    /// load every asset class from it.
    ///
    /// Exercises the same steps as ImportWizardScreen without the UI, so the
    /// flow is covered by the gate rather than only by clicking through it.
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

            // What the wizard writes.
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

            // Names.
            var strings = Wc2StringTable.Load(source);
            Assert.IsNotNull(strings);
            Assert.AreEqual("Footman", strings.UnitName(UnitTypeId.Footman));

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

        [Test]
        public void MusicResolves_FromEitherSource()
        {
            var found = Wc2InstallLocator.Find();
            if (found.Count == 0 || !found[0].IsUsable)
                Assert.Ignore("no WC2 install on this machine");

            var library = MusicLibrary.Create(null, found[0].DataRoot);
            Assert.IsNotNull(library, "no music source at all");

            // Six in-game tracks per race, whether they come from the converted
            // Ogg cache or straight from the installation's WAVs.
            Assert.AreEqual(6, library.TracksFor(Craftwar.View.MusicCue.InGame, Race.Human).Count);
            Assert.AreEqual(6, library.TracksFor(Craftwar.View.MusicCue.InGame, Race.Orc).Count);
            Assert.AreEqual(1, library.TracksFor(Craftwar.View.MusicCue.Menu, Race.Human).Count);
            Assert.AreEqual(1, library.TracksFor(Craftwar.View.MusicCue.Victory, Race.Orc).Count);
        }
    }
}
