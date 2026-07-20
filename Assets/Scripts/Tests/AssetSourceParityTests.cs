using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The regression oracle for the loose-file migration: decode the same
    /// logical asset through both sources and require identical bytes.
    ///
    /// Temporary by construction. It can only run where BOTH a loose install and
    /// a maindat.war are present, which is a development machine — and it
    /// self-disables once maindat.war leaves the supported path. That is fine:
    /// its job is to catch a wrong row in FileForUnit while the two tables still
    /// coexist, not to guard forever.
    /// </summary>
    public class AssetSourceParityTests
    {
        IAssetSource _loose;
        War2Archive _archive;

        [SetUp]
        public void SetUp()
        {
            var paths = LocalAssetPaths.Load();

            string dataRoot = paths?.dataRoot;
            if (string.IsNullOrEmpty(dataRoot))
            {
                var found = Wc2InstallLocator.Find();
                if (found.Count > 0 && found[0].IsUsable)
                    dataRoot = found[0].DataRoot;
            }
            if (!string.IsNullOrEmpty(dataRoot) && Directory.Exists(dataRoot))
                _loose = new LooseFileAssetSource(dataRoot);

            if (paths != null && !string.IsNullOrEmpty(paths.maindatWar) && File.Exists(paths.maindatWar))
                _archive = new War2Archive(File.ReadAllBytes(paths.maindatWar));
        }

        void RequireBoth()
        {
            if (_loose == null)
                Assert.Ignore("no loose WC2 install on this machine");
            if (_archive == null)
                Assert.Ignore("no maindat.war configured — parity check no longer applicable");
        }

        static readonly PudEra[] Eras =
            { PudEra.Forest, PudEra.Winter, PudEra.Wasteland, PudEra.Swamp };

        [Test]
        public void Tilesets_DecodeIdentically_InEveryEra()
        {
            RequireBoth();
            foreach (var era in Eras)
            {
                var fromLoose = War2Tileset.Load(_loose, era);
                Assert.IsNotNull(fromLoose, $"loose tileset missing for {era}");
                var fromArchive = War2Tileset.Load(_archive, era);

                var looseTiles = fromLoose.DecodeAll();
                var archiveTiles = fromArchive.DecodeAll();

                var byId = new Dictionary<ushort, byte[]>();
                foreach (var t in archiveTiles)
                    byId[t.TileId] = t.Pixels;

                int compared = 0;
                foreach (var t in looseTiles)
                {
                    // The loose set is a superset: this sample maindat is the
                    // older base-game archive and lacks a handful of ids.
                    if (!byId.TryGetValue(t.TileId, out var expected))
                        continue;
                    CollectionAssert.AreEqual(expected, t.Pixels,
                        $"{era} tile 0x{t.TileId:X4} differs between sources");
                    compared++;
                }

                Assert.Greater(compared, 300, $"{era}: too few tiles compared to be meaningful");
            }
        }

        [Test]
        public void EveryUnitSprite_MatchesItsArchiveEntry()
        {
            RequireBoth();

            int compared = 0, skippedNoFile = 0, skippedNoEntry = 0;
            foreach (var value in System.Enum.GetValues(typeof(UnitTypeId)))
            {
                ushort typeId = (ushort)(UnitTypeId)value;
                foreach (var era in Eras)
                {
                    int entry = War2Sprites.EntryForUnit(typeId, era);
                    string file = War2Sprites.FileForUnit(typeId, era);

                    if (entry == 0) { skippedNoEntry++; continue; }
                    if (file == null) { skippedNoFile++; continue; }

                    if (!_loose.TryRead("art/unit/" + file.ToLowerInvariant(), out var looseBytes))
                        Assert.Fail($"{(UnitTypeId)typeId}/{era}: FileForUnit names a missing file '{file}'");

                    byte[] archiveBytes;
                    try { archiveBytes = _archive.ExtractEntry(entry); }
                    catch { continue; } // known bad-flag entries in this archive

                    if (archiveBytes == null)
                        continue;

                    var a = War2Sprites.Decode(looseBytes);
                    var b = War2Sprites.Decode(archiveBytes);

                    Assert.AreEqual(b.FrameCount, a.FrameCount,
                        $"{(UnitTypeId)typeId}/{era}: '{file}' vs entry {entry} frame count");
                    Assert.AreEqual(b.MaxWidth, a.MaxWidth, $"{(UnitTypeId)typeId}/{era}: max width");
                    Assert.AreEqual(b.MaxHeight, a.MaxHeight, $"{(UnitTypeId)typeId}/{era}: max height");

                    for (int f = 0; f < a.FrameCount; f++)
                        CollectionAssert.AreEqual(b.Frames[f].Indices, a.Frames[f].Indices,
                            $"{(UnitTypeId)typeId}/{era}: '{file}' frame {f} differs from entry {entry}");

                    compared++;
                }
            }

            Assert.Greater(compared, 200,
                $"too few sprites compared ({compared}); " +
                $"noFile={skippedNoFile} noEntry={skippedNoEntry}");
        }

        [Test]
        public void FileForUnit_NamesOnlyFilesThatExist()
        {
            if (_loose == null)
                Assert.Ignore("no loose WC2 install on this machine");

            // Stands alone without maindat.war: once the archive path is retired
            // this is what still guards the table.
            foreach (var value in System.Enum.GetValues(typeof(UnitTypeId)))
            {
                ushort typeId = (ushort)(UnitTypeId)value;
                foreach (var era in Eras)
                {
                    string file = War2Sprites.FileForUnit(typeId, era);
                    if (file == null)
                        continue;
                    Assert.IsTrue(_loose.Exists("art/unit/" + file.ToLowerInvariant()),
                        $"{(UnitTypeId)typeId}/{era}: '{file}' does not exist in the install");
                }
            }
        }
    }
}
