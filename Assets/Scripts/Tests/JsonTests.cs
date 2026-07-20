using System.IO;
using Craftwar.Import;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class JsonTests
    {
        // ---------- shape ----------

        [Test]
        public void FlatStringMap_RoundTrips()
        {
            var v = JsonValue.Parse("{\"a\": \"one\", \"b\": \"two\"}");
            var map = v.ToStringMap();
            Assert.AreEqual(2, map.Count);
            Assert.AreEqual("one", map["a"]);
            Assert.AreEqual("two", map["b"]);
        }

        [Test]
        public void NestedObjects_AreAddressableByChaining()
        {
            // The TexturePacker shape: frames -> name -> frame -> x/y/w/h.
            const string src = @"{""frames"": { ""forest_0"": {
                ""frame"": {""x"":46,""y"":0,""w"":46,""h"":38},
                ""rotated"": false, ""trimmed"": false } } }";
            var v = JsonValue.Parse(src);
            var rect = v["frames"]["forest_0"]["frame"];
            Assert.AreEqual(46, rect["x"].AsInt());
            Assert.AreEqual(0, rect["y"].AsInt());
            Assert.AreEqual(46, rect["w"].AsInt());
            Assert.AreEqual(38, rect["h"].AsInt());
            Assert.IsFalse(v["frames"]["forest_0"]["rotated"].AsBool());
        }

        [Test]
        public void MissingMembers_ReturnNull_NotThrow()
        {
            var v = JsonValue.Parse("{\"a\": {\"b\": 1}}");
            Assert.IsNull(v["nope"]);
            Assert.IsNull(v["a"]["nope"]);
            Assert.IsNull(v["nope"]?["deeper"], "chaining off a miss must not throw");
        }

        [Test]
        public void Arrays_AndMixedTypes()
        {
            var v = JsonValue.Parse("[1, \"two\", true, null, {\"k\":2.5}, [3]]");
            Assert.AreEqual(6, v.Count);
            Assert.AreEqual(1, v[0].AsInt());
            Assert.AreEqual("two", v[1].AsString());
            Assert.IsTrue(v[2].AsBool());
            Assert.AreEqual(JsonValue.Kind.Null, v[3].Type);
            Assert.AreEqual(2.5, v[4]["k"].AsDouble(), 1e-9);
            Assert.AreEqual(3, v[5][0].AsInt());
            Assert.IsNull(v[99], "out-of-range index is null, not an exception");
        }

        [Test]
        public void Escapes_AreDecoded()
        {
            var v = JsonValue.Parse(@"{""k"": ""a\""b\\c\nd\teé""}");
            Assert.AreEqual("a\"b\\c\nd\teé", v["k"].AsString());
        }

        [Test]
        public void NegativeAndExponentNumbers()
        {
            var v = JsonValue.Parse("{\"a\":-5,\"b\":1e3,\"c\":-2.5e-2}");
            Assert.AreEqual(-5, v["a"].AsInt());
            Assert.AreEqual(1000, v["b"].AsInt());
            Assert.AreEqual(-0.025, v["c"].AsDouble(), 1e-9);
        }

        [Test]
        public void EmptyContainers()
        {
            Assert.AreEqual(0, JsonValue.Parse("{}").Count);
            Assert.AreEqual(0, JsonValue.Parse("[]").Count);
            Assert.AreEqual(0, JsonValue.Parse("{}").ToStringMap().Count);
        }

        // ---------- failure modes ----------

        [TestCase("{")]
        [TestCase("[1,")]
        [TestCase("{\"a\" 1}")]
        [TestCase("{\"a\": }")]
        [TestCase("{'a': 1}")]
        [TestCase("\"unterminated")]
        [TestCase("{} trailing")]
        [TestCase("tru")]
        public void Malformed_Throws(string src)
        {
            Assert.Throws<JsonException>(() => JsonValue.Parse(src));
        }

        [Test]
        public void NullInput_Throws()
        {
            Assert.Throws<JsonException>(() => JsonValue.Parse(null));
        }

        // ---------- against the real files ----------

        const string DataRoot = @"C:\Program Files (x86)\Warcraft II Remastered\x86\Data";

        [Test]
        public void RealStringTable_ParsesAndMapsUnitIds()
        {
            string path = Path.Combine(DataRoot, "Strings", "enUS.json");
            if (!File.Exists(path))
                Assert.Ignore("WC2 install not present on this machine");

            var map = JsonValue.Parse(File.ReadAllText(path)).ToStringMap();
            Assert.Greater(map.Count, 1000, "enUS.json has ~1613 keys");

            // unit_<typeId> indexes the same id space the sim uses.
            Assert.AreEqual("Footman", map["unit_" + (int)UnitTypeId.Footman]);
            Assert.AreEqual("Peasant", map["unit_" + (int)UnitTypeId.Peasant]);
            Assert.AreEqual("Town Hall", map["unit_" + (int)UnitTypeId.TownHall]);
        }

        [Test]
        public void RealIconAtlas_ParsesEveryFrame()
        {
            string path = Path.Combine(DataRoot, "Art", "classic", "HUD", "Portrait-face.json");
            if (!File.Exists(path))
                Assert.Ignore("WC2 install not present on this machine");

            var frames = JsonValue.Parse(File.ReadAllText(path))["frames"];
            Assert.IsNotNull(frames);
            Assert.AreEqual(784, frames.Count, "196 icons x 4 eras");

            // Every icon is exactly 46x38 — the invariant the UI layout relies on.
            foreach (var kv in frames.Object)
            {
                var r = kv.Value["frame"];
                Assert.AreEqual(46, r["w"].AsInt(), kv.Key);
                Assert.AreEqual(38, r["h"].AsInt(), kv.Key);
            }
        }
    }
}
