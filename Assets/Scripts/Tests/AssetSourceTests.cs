using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class AssetSourceTests
    {
        string _temp;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "craftwar-assetsrc-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true); }
            catch { /* a locked temp dir must not fail the suite */ }
        }

        void Write(string relative, byte[] bytes)
        {
            string full = Path.Combine(_temp, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
        }

        // ---------- LooseFileAssetSource ----------

        [Test]
        public void ReadsByLogicalPath_IgnoringCaseAndSeparator()
        {
            // On disk the install uses mixed case; call sites want lowercase.
            Write("Gamesfx/Human/Hready.wav", new byte[] { 1, 2, 3 });
            var src = new LooseFileAssetSource(_temp);

            Assert.IsTrue(src.TryRead("gamesfx/human/hready.wav", out var a));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, a);

            Assert.IsTrue(src.TryRead("Gamesfx/Human/Hready.wav", out _), "case must not matter");
            Assert.IsTrue(src.TryRead(@"gamesfx\human\hready.wav", out _), "backslashes must not matter");
            Assert.IsTrue(src.Exists("gamesfx/human/hready.wav"));
        }

        [Test]
        public void MissingAsset_IsFalse_NotAnException()
        {
            var src = new LooseFileAssetSource(_temp);
            Assert.IsFalse(src.TryRead("art/nope.grp", out var data));
            Assert.IsNull(data);
            Assert.IsFalse(src.Exists("art/nope.grp"));
        }

        [Test]
        public void NonexistentRoot_YieldsEmptySource_NotAThrow()
        {
            var src = new LooseFileAssetSource(Path.Combine(_temp, "does-not-exist"));
            Assert.AreEqual(0, src.Count);
            Assert.IsFalse(src.TryRead("anything", out _));
        }

        [Test]
        public void List_FiltersByPrefix_AndIsDeterministic()
        {
            Write("Gamesfx/Human/Hwhat1.wav", new byte[] { 1 });
            Write("Gamesfx/Human/Hwhat2.wav", new byte[] { 1 });
            Write("Gamesfx/Orc/Owhat1.wav", new byte[] { 1 });
            Write("Music/HUMAN1_r.wav", new byte[] { 1 });

            var src = new LooseFileAssetSource(_temp);
            var human = new List<string>(src.List("gamesfx/human/"));

            CollectionAssert.AreEqual(
                new[] { "gamesfx/human/hwhat1.wav", "gamesfx/human/hwhat2.wav" }, human,
                "sorted, so variant discovery does not depend on filesystem order");

            Assert.AreEqual(4, new List<string>(src.List("")).Count);
        }

        [Test]
        public void List_DiscoversVariantCount()
        {
            // The reason List exists: how many "what" barks a unit has is data,
            // not something to hardcode.
            for (int i = 1; i <= 5; i++)
                Write($"Gamesfx/Human/Hwhat{i}.wav", new byte[] { (byte)i });
            var src = new LooseFileAssetSource(_temp);
            Assert.AreEqual(5, new List<string>(src.List("gamesfx/human/hwhat")).Count);
        }

        // ---------- Wc2InstallLocator ----------

        [Test]
        public void Inspect_ScoresPartialInstall_AndReportsWhichClassesAreMissing()
        {
            Write("Art/bgs/Forest/forest.ppl", new byte[768]);
            Write("Art/unit/Human/peon.grp", new byte[16]);

            var c = Wc2InstallLocator.Inspect(_temp);
            Assert.IsTrue(c.HasTilesets);
            Assert.IsTrue(c.HasSprites);
            Assert.IsFalse(c.HasSounds);
            Assert.IsFalse(c.HasStrings);
            Assert.IsTrue(c.IsUsable, "tilesets + sprites are enough to play");
            Assert.AreEqual(60, c.Confidence);
        }

        [Test]
        public void Inspect_EmptyFolder_IsNotUsable()
        {
            var c = Wc2InstallLocator.Inspect(_temp);
            Assert.IsFalse(c.IsUsable);
            Assert.AreEqual(0, c.Confidence);
        }

        [Test]
        public void Inspect_AcceptsInstallRootAboveTheDataFolder()
        {
            // A player using a folder picker may well choose the install root.
            Write("x86/Data/Art/bgs/Forest/forest.ppl", new byte[768]);
            Write("x86/Data/Art/unit/Human/peon.grp", new byte[16]);

            var c = Wc2InstallLocator.Inspect(_temp);
            Assert.IsTrue(c.IsUsable);
            StringAssert.EndsWith("Data", c.DataRoot);
        }

        [Test]
        public void MapFolders_FindsTheInstallLayout()
        {
            Write("x86/Data/Art/bgs/Forest/forest.ppl", new byte[768]);
            Directory.CreateDirectory(Path.Combine(_temp, "x86", "Maps"));
            Directory.CreateDirectory(Path.Combine(_temp, "x86", "Data", "OrigMaps"));

            var folders = Wc2InstallLocator.MapFolders(Path.Combine(_temp, "x86", "Data"));
            Assert.AreEqual(2, folders.Count);
            StringAssert.EndsWith("Maps", folders[0]);
        }

        // ---------- against the real install ----------

        [Test]
        public void RealInstall_IsFoundAndComplete()
        {
            var candidates = Wc2InstallLocator.Find();
            if (candidates.Count == 0)
                Assert.Ignore("no WC2 install on this machine");

            var best = candidates[0];
            Assert.IsTrue(best.IsUsable, $"{best.DataRoot} should be usable");
            Assert.IsTrue(best.HasSounds, "loose Gamesfx is the whole M8 audio plan");
            Assert.IsTrue(best.HasIcons);
            Assert.IsTrue(best.HasStrings);

            // And the source built from it can actually read a known asset.
            var src = new LooseFileAssetSource(best.DataRoot);
            Assert.IsTrue(src.TryRead(Wc2InstallLocator.ProbeTileset, out var ppl));
            Assert.AreEqual(768, ppl.Length, "palette is exactly 768 bytes");
        }
    }
}
