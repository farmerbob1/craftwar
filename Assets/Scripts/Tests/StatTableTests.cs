using System.IO;
using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class StatTableTests
    {
        const string BneRezDir = @"C:\Program Files (x86)\Warcraft II Remastered\x86\Data\Rez";

        [Test]
        public void GeneratedDefaults_MatchKnownGroundTruth()
        {
            var rules = RuleSet.CreateDefault();

            ref var footman = ref rules.UnitType(UnitTypeId.Footman);
            Assert.AreEqual(60, footman.Hp);
            Assert.AreEqual(600, footman.GoldCost);
            Assert.AreEqual(2, footman.Armor);
            Assert.AreEqual(6, footman.BasicDamage);
            Assert.AreEqual(3, footman.PiercingDamage);
            Assert.IsTrue(footman.Is(UnitTypeFlags.LandUnit | UnitTypeFlags.CanAttack));
            Assert.IsFalse(footman.Is(UnitTypeFlags.Building));

            ref var peasant = ref rules.UnitType(UnitTypeId.Peasant);
            Assert.AreEqual(30, peasant.Hp);
            Assert.AreEqual(400, peasant.GoldCost);
            Assert.IsTrue(peasant.Is(UnitTypeFlags.Peon));

            ref var townHall = ref rules.UnitType(UnitTypeId.TownHall);
            Assert.AreEqual(1200, townHall.Hp);
            Assert.AreEqual(1200, townHall.GoldCost);
            Assert.AreEqual(800, townHall.LumberCost);
            Assert.IsTrue(townHall.Is(UnitTypeFlags.Building | UnitTypeFlags.GoldDepot));

            ref var mine = ref rules.UnitType(UnitTypeId.GoldMine);
            Assert.IsTrue(mine.Is(UnitTypeFlags.GoldMine));

            // Upgrades: sword 1/2 gold costs (UGRD slots 0 and 1).
            Assert.AreEqual(800, rules.Upgrades[0].Gold);
            Assert.AreEqual(2400, rules.Upgrades[1].Gold);

            // Orc/human mirror symmetry for core units.
            Assert.AreEqual(rules.UnitType(UnitTypeId.Footman).Hp, rules.UnitType(UnitTypeId.Grunt).Hp);
            Assert.AreEqual(rules.UnitType(UnitTypeId.Knight).Hp, rules.UnitType(UnitTypeId.Ogre).Hp);
            Assert.AreEqual(rules.UnitType(UnitTypeId.TownHall).Hp, rules.UnitType(UnitTypeId.GreatHall).Hp);
        }

        [Test]
        public void GeneratedDefaults_RoundTripAgainstSourceDatFiles()
        {
            string unitPath = Path.Combine(BneRezDir, "unitdata.dat");
            string upgPath = Path.Combine(BneRezDir, "upgrades.dat");
            if (!File.Exists(unitPath) || !File.Exists(upgPath))
                Assert.Ignore("BNE Rez data not present");

            var fromDisk = UdtaParser.Parse(File.ReadAllBytes(unitPath), hasLeadingWord: false);
            var generated = DefaultData.BuildUnits();
            Assert.AreEqual(fromDisk.Length, generated.Length);
            for (int i = 0; i < fromDisk.Length; i++)
                Assert.AreEqual(fromDisk[i], generated[i], $"unit 0x{i:x2} mismatch — rerun DataCodegen");

            var upgDisk = UgrdParser.Parse(File.ReadAllBytes(upgPath), hasLeadingWord: false);
            var upgGen = DefaultData.BuildUpgrades();
            for (int i = 0; i < upgDisk.Length; i++)
                Assert.AreEqual(upgDisk[i], upgGen[i], $"upgrade 0x{i:x2} mismatch — rerun DataCodegen");
        }

        [Test]
        public void RuleSet_MapOverride_UseDefaultWordSkipsOverride()
        {
            var rules = RuleSet.CreateDefault();
            int originalHp = rules.UnitType(UnitTypeId.Footman).Hp;

            // Payload with leading word = 1 ("use default"): must be ignored.
            var payload = new byte[UdtaParser.PayloadSizeTrimmed + 2];
            payload[0] = 1;
            var pud = new Craftwar.Sim.Pud.PudFile { UnitDataOverride = payload };
            rules.ApplyMapOverrides(pud);
            Assert.AreEqual(originalHp, rules.UnitType(UnitTypeId.Footman).Hp);
        }
    }
}
