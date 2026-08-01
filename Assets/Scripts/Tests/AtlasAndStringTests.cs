using System.Collections.Generic;
using System.IO;
using Craftwar.App;
using Craftwar.Import;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class AtlasAndStringTests
    {
        const string DataRoot = @"C:\Program Files (x86)\Warcraft II Remastered\x86\Data";

        static IAssetSource Source()
        {
            var found = Wc2InstallLocator.Find();
            return found.Count > 0 && found[0].IsUsable
                ? new LooseFileAssetSource(found[0].DataRoot)
                : null;
        }

        // ---------- atlas parsing ----------

        [Test]
        public void ParsesFrameRects()
        {
            const string json = @"{""frames"": {
                ""forest_0"": { ""frame"": {""x"":0,""y"":0,""w"":46,""h"":38} },
                ""forest_1"": { ""frame"": {""x"":46,""y"":0,""w"":46,""h"":38} } } }";
            var frames = TexturePackerAtlas.Parse(json);

            Assert.AreEqual(2, frames.Count);
            Assert.AreEqual(46, frames["forest_1"].X);
            Assert.AreEqual(0, frames["forest_1"].Y);
            Assert.AreEqual(46, frames["forest_1"].Width);
            Assert.AreEqual(38, frames["forest_1"].Height);
        }

        [Test]
        public void RejectsJsonWithoutFrames()
        {
            Assert.Throws<JsonException>(() => TexturePackerAtlas.Parse(@"{""meta"": {}}"));
        }

        [Test]
        public void RealAtlas_HasEveryEraAtAConsistentSize()
        {
            string path = Path.Combine(DataRoot, "Art", "classic", "HUD", "Portrait-face.json");
            if (!File.Exists(path))
                Assert.Ignore("WC2 install not present on this machine");

            var frames = TexturePackerAtlas.Parse(File.ReadAllText(path));
            Assert.AreEqual(784, frames.Count, "196 icons x 4 eras");

            // Every era must define index 0..195, or the era-prefix lookup would
            // silently fall back to forest for a whole tileset.
            foreach (string prefix in new[] { "forest", "ice", "swamp", "xswamp" })
                for (int i = 0; i < 196; i++)
                    Assert.IsTrue(frames.ContainsKey($"{prefix}_{i}"), $"missing {prefix}_{i}");

            foreach (var kv in frames)
            {
                Assert.AreEqual(46, kv.Value.Width, kv.Key);
                Assert.AreEqual(38, kv.Value.Height, kv.Key);
            }
        }

        [Test]
        public void RealMaskAtlas_MirrorsTheFaceAtlas()
        {
            string path = Path.Combine(DataRoot, "Art", "classic", "HUD", "Portrait-mask.json");
            if (!File.Exists(path))
                Assert.Ignore("WC2 install not present on this machine");

            var frames = TexturePackerAtlas.Parse(File.ReadAllText(path));
            Assert.Greater(frames.Count, 0);
            Assert.IsTrue(frames.ContainsKey("forest_0_team"),
                "mask frames carry a _team suffix; the team-colour path depends on it");
        }

        // ---------- string table ----------
        // Wc2StringTable (the old runtime wrapper) is retired in favour of
        // BakedStringTable, which reads a pre-parsed LocalizedStringTable
        // asset rather than JSON — so these test the raw decode primitive the
        // bake step (Craftwar.EditorTools.StringBaker) actually relies on.

        static Dictionary<string, string> LoadStringMap(IAssetSource source, string locale = "enus")
        {
            if (!source.TryRead($"strings/{locale}.json", out var bytes))
                return null;
            try
            {
                return JsonValue.Parse(System.Text.Encoding.UTF8.GetString(bytes)).ToStringMap();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        static string NameFor(Dictionary<string, string> table, UnitTypeId type) =>
            table.TryGetValue("unit_" + (int)type, out var name) ? name : null;

        [Test]
        public void StringTable_GivesRealUnitNames()
        {
            var source = Source();
            if (source == null)
                Assert.Ignore("WC2 install not present on this machine");

            var table = LoadStringMap(source);
            Assert.IsNotNull(table);
            Assert.Greater(table.Count, 1000);

            Assert.AreEqual("Footman", NameFor(table, UnitTypeId.Footman));
            Assert.AreEqual("Peasant", NameFor(table, UnitTypeId.Peasant));
            Assert.AreEqual("Town Hall", NameFor(table, UnitTypeId.TownHall));
            // The case reflection gets wrong: "ElvenLumberMill" -> "Elven Lumber Mill"
            // happens to work, but the real table is the authority.
            Assert.AreEqual("Elven Lumber Mill", NameFor(table, UnitTypeId.ElvenLumberMill));
        }

        [Test]
        public void StringTable_CoversTheWholeRoster()
        {
            var source = Source();
            if (source == null)
                Assert.Ignore("WC2 install not present on this machine");

            var table = LoadStringMap(source);
            int named = 0, missing = 0;
            foreach (var v in System.Enum.GetValues(typeof(UnitTypeId)))
            {
                var type = (UnitTypeId)v;
                if (string.IsNullOrEmpty(NameFor(table, type)))
                    missing++;
                else
                    named++;
            }
            // Start markers and a few internal ids have no player-facing name.
            Assert.Greater(named, 90, $"only {named} named, {missing} missing");
        }

        [Test]
        public void StringTable_MissingLocaleIsNullNotAThrow()
        {
            var source = Source();
            if (source == null)
                Assert.Ignore("WC2 install not present on this machine");
            Assert.IsNull(LoadStringMap(source, "zzzz"));
        }

        // ---------- icon table ----------

        [Test]
        public void IconTable_StaysInsideTheAtlas()
        {
            foreach (var v in System.Enum.GetValues(typeof(UnitTypeId)))
            {
                int icon = Craftwar.View.UnitIconTable.IconFor((UnitTypeId)v);
                if (icon == Craftwar.View.UnitIconTable.None)
                    continue;
                Assert.GreaterOrEqual(icon, 0, $"{(UnitTypeId)v}");
                Assert.Less(icon, 196, $"{(UnitTypeId)v} is outside the 196-icon atlas");
            }
        }

        [Test]
        public void UpgradeIcons_StayInsideTheAtlas()
        {
            // These come from UGRD rather than a hand table, so this guards the
            // parse offset as much as the values.
            var rules = RuleSet.CreateDefault();
            foreach (var v in System.Enum.GetValues(typeof(UpgradeId)))
            {
                var id = (UpgradeId)v;
                if (id == UpgradeId.None)
                    continue;
                int icon = rules.Upgrades[(int)id].Icon;
                Assert.GreaterOrEqual(icon, 0, $"{id}");
                Assert.Less(icon, 196, $"{id} icon {icon} is outside the atlas");
            }
        }
    }
}
