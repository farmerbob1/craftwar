using Craftwar.Sim;
using Craftwar.Sim.Ai;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Pure tests for the data-driven strategy layer — parse, canonical binary
    /// round-trip, hash stability, and the migration-fidelity facts transcribed
    /// from the original VLAND/COMMON scripts. Runs in the standalone harness.
    /// The whole existing M9 AI suite also now runs against this parsed strategy,
    /// which is the strongest guard that behaviour is unchanged from M9.
    /// </summary>
    public class AiStrategyTests
    {
        [Test]
        public void LandAttack_MatchesTranscribedFacts()
        {
            var s = BuiltinAiStrategies.Default;

            Assert.AreEqual("land-attack", s.Name);
            Assert.AreEqual(AiTier.Normal, s.DefaultTier);

            Assert.AreEqual(500, s.MinGold);
            Assert.AreEqual(1000, s.LowGold);
            Assert.AreEqual(500, s.LowTree);
            Assert.AreEqual(2000, s.PlentyTree);
            Assert.AreEqual(200, s.RebuildOnlyGold);
            Assert.AreEqual(100, s.RebuildOnlyLumber);
            Assert.AreEqual(3, s.SuicideBuildingCount);
            Assert.AreEqual(500, s.PostWaveSleepTicks);
            Assert.AreEqual(1500, s.DryWaveTicks);

            Assert.AreEqual(9, s.Phases.Length, "nine scripted phases before endgame");

            ref readonly AiPhase p0 = ref s.Phases[0];
            Assert.AreEqual(9, p0.WorkerTarget);
            Assert.AreEqual(3, p0.WaveSize);
            CollectionAssert.AreEqual(
                new[] { AiUnit.Hall, AiUnit.LumberMill, AiUnit.Barracks }, p0.Unlock);
            Assert.AreEqual(0, p0.ResearchGoals.Length);
            Assert.AreEqual(1, p0.Army.Length);
            Assert.AreEqual(AiUnit.Soldier, p0.Army[0].Unit);
            Assert.AreEqual(3, p0.Army[0].Count);

            ref readonly AiPhase p1 = ref s.Phases[1];
            CollectionAssert.AreEqual(new[] { AiUnit.Blacksmith }, p1.Unlock);
            CollectionAssert.AreEqual(new[] { AiUpgrade.Weapon1, AiUpgrade.Armor1 }, p1.ResearchGoals);

            ref readonly AiPhase p5 = ref s.Phases[5];
            CollectionAssert.AreEqual(new[] { AiUnit.CavalryHall }, p5.Unlock);
            CollectionAssert.AreEqual(new[] { AiUpgrade.RangedUnlock }, p5.ResearchGoals);
            Assert.AreEqual(9, p5.WaveSize);

            Assert.AreEqual(25, s.Endgame.WorkerTarget);
            Assert.AreEqual(11, s.Endgame.WaveSize);
            Assert.AreEqual(4, s.Endgame.Army.Length);
        }

        [Test]
        public void CanonicalBinary_RoundTrips()
        {
            var a = BuiltinAiStrategies.Default;
            byte[] first = a.ToBytes();
            var b = AiStrategy.FromBytes(first);
            byte[] second = b.ToBytes();
            CollectionAssert.AreEqual(first, second,
                "FromBytes(ToBytes(x)).ToBytes() must equal x's bytes");
        }

        [Test]
        public void Hash_IsStableAcrossParses()
        {
            uint h1 = AiStrategyParser.Parse(BuiltinAiStrategies.LandAttackText).Hash();
            uint h2 = AiStrategyParser.Parse(BuiltinAiStrategies.LandAttackText).Hash();
            Assert.AreEqual(h1, h2);
            Assert.AreNotEqual(0u, h1);
        }

        [Test]
        public void Hash_ChangesWithContent()
        {
            var a = BuiltinAiStrategies.Default;
            var b = AiStrategyParser.Parse(BuiltinAiStrategies.LandAttackText);
            b.PostWaveSleepTicks += 1;
            Assert.AreNotEqual(a.Hash(), b.Hash(),
                "a different value must change the provenance hash");
        }

        [Test]
        public void Parse_RejectsUnknownDirective()
        {
            Assert.Throws<AiStrategyParseException>(() =>
                AiStrategyParser.Parse("strategy x\nwibble 3\nphase workers=1 wave=1\nendgame workers=1 wave=1"));
        }

        [Test]
        public void Parse_RejectsMalformedArmyEntry()
        {
            Assert.Throws<AiStrategyParseException>(() =>
                AiStrategyParser.Parse("strategy x\nphase workers=1 wave=1 army=Soldier\nendgame workers=1 wave=1"));
        }

        [Test]
        public void Parse_RejectsUnknownRole()
        {
            Assert.Throws<AiStrategyParseException>(() =>
                AiStrategyParser.Parse("strategy x\nphase workers=1 wave=1 build=Wizard\nendgame workers=1 wave=1"));
        }

        [Test]
        public void Parse_RequiresEndgame()
        {
            Assert.Throws<AiStrategyParseException>(() =>
                AiStrategyParser.Parse("strategy x\nphase workers=1 wave=1"));
        }
    }
}
