using NUnit.Framework;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Tests
{
    /// <summary>The moddable .ai profile format: parse, defaults, binary round-trip,
    /// stable hash.</summary>
    public class AiProfileTests
    {
        [Test]
        public void Builtin_Parses()
        {
            var p = BuiltinAiProfiles.Default;
            Assert.AreEqual("land-attack", p.Name);
            Assert.AreEqual(AiTier.Normal, p.DefaultTier);
            Assert.AreEqual(8, p.WaveSize);
            Assert.AreEqual(16, p.WorkerTarget);
            Assert.IsNotEmpty(p.BuildOrder);
            Assert.AreEqual(AiUnit.Hall, p.BuildOrder[0]);
            Assert.AreEqual(9, p.Army.Length);
            Assert.AreEqual(15, p.Research.Length);
        }

        [Test]
        public void Weights_AreConvertedFromPercents()
        {
            var p = BuiltinAiProfiles.Default;
            // farm=300 -> 3.0 in Q16.16
            Assert.AreEqual(AiMath.FromInt(3), p.WeightFarm);
            // defend=400 -> 4.0
            Assert.AreEqual(AiMath.FromInt(4), p.WeightDefend);
            // build=100 -> 1.0
            Assert.AreEqual(AiMath.One, p.WeightBuild);
        }

        [Test]
        public void Curves_AreConvertedFromPercents()
        {
            var p = BuiltinAiProfiles.Default;
            Assert.AreEqual(CurveKind.Logistic, p.Affordability.Kind);
            Assert.AreEqual(AiMath.FromInt(3), p.Affordability.A); // 300%
            // threatSafety: linear -100 100 -> slope -1, intercept 1 (downslope)
            Assert.AreEqual(CurveKind.Linear, p.ThreatSafety.Kind);
            Assert.AreEqual(-AiMath.One, p.ThreatSafety.A);
            Assert.AreEqual(AiMath.One, p.ThreatSafety.B);
        }

        [Test]
        public void BinaryRoundTrip_PreservesProfile()
        {
            var p = BuiltinAiProfiles.Default;
            byte[] bytes = p.ToBytes();
            var q = AiProfile.FromBytes(bytes);
            Assert.AreEqual(p.Hash(), q.Hash());
            Assert.AreEqual(p.Name, q.Name);
            Assert.AreEqual(p.BuildOrder.Length, q.BuildOrder.Length);
            Assert.AreEqual(p.WeightDefend, q.WeightDefend);
            Assert.AreEqual(p.Affordability.A, q.Affordability.A);
        }

        [Test]
        public void Hash_IsStableAcrossReparse()
        {
            var a = AiProfileParser.Parse(BuiltinAiProfiles.LandAttackText);
            var b = AiProfileParser.Parse(BuiltinAiProfiles.LandAttackText);
            Assert.AreEqual(a.Hash(), b.Hash());
        }

        [Test]
        public void CustomProfile_OverridesAndKeepsDefaults()
        {
            var p = AiProfileParser.Parse(
                "profile rusher\n" +
                "personality aggression=90\n" +
                "military waveSize=4\n" +
                "weights wave=180\n");
            Assert.AreEqual("rusher", p.Name);
            Assert.AreEqual(90, p.Aggression);
            Assert.AreEqual(4, p.WaveSize);
            Assert.AreEqual(AiMath.FromInt(180) / 100, p.WeightWave);
            // Untouched knobs keep their defaults.
            Assert.AreEqual(50, p.Greed);
            Assert.AreEqual(3, p.SuicideBuildingCount);
        }

        [Test]
        public void MissingName_Throws()
        {
            Assert.Throws<AiProfileParseException>(() =>
                AiProfileParser.Parse("personality aggression=10\n"));
        }
    }
}
