using Craftwar.Sim.Ai;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>The difficulty-tier table and its threading into AiPlayer. Pure.</summary>
    public class AiTierTests
    {
        [Test]
        public void Normal_IsTheM9Baseline()
        {
            var n = AiTierTable.For(AiTier.Normal);
            Assert.AreEqual(25, n.ThinkPeriodTicks, "Normal keeps the M9 think cadence");
            Assert.IsFalse(n.PlannedLayout || n.FocusFire || n.Reinforce
                || n.RetargetWaves || n.ActiveDefense || n.Scouting || n.Expansion,
                "Normal has no extra competences");
            Assert.AreEqual(0, n.HarvestBonusTenths);
            Assert.AreEqual(0, n.SightBonus);
            Assert.AreEqual(0, n.StartGoldBonus);
            Assert.AreEqual(0, n.StartLumberBonus);
        }

        [Test]
        public void Cadence_SharpensWithTier()
        {
            int dumb = AiTierTable.For(AiTier.Dumb).ThinkPeriodTicks;
            int normal = AiTierTable.For(AiTier.Normal).ThinkPeriodTicks;
            int smart = AiTierTable.For(AiTier.Smart).ThinkPeriodTicks;
            int god = AiTierTable.For(AiTier.God).ThinkPeriodTicks;
            Assert.Greater(dumb, normal);
            Assert.Greater(normal, smart);
            Assert.Greater(smart, god);
            Assert.Greater(god, 0);
        }

        [Test]
        public void OnlyGod_CheatsByDefault()
        {
            Assert.AreEqual(0, AiTierTable.For(AiTier.Dumb).HarvestBonusTenths);
            Assert.AreEqual(0, AiTierTable.For(AiTier.Smart).HarvestBonusTenths);
            var god = AiTierTable.For(AiTier.God);
            Assert.Greater(god.HarvestBonusTenths, 0);
            Assert.Greater(god.SightBonus, 0);
            Assert.Greater(god.StartGoldBonus, 0);
        }

        [Test]
        public void Smart_HasSkillButNoHandicap()
        {
            var s = AiTierTable.For(AiTier.Smart);
            Assert.IsTrue(s.PlannedLayout && s.FocusFire && s.ActiveDefense,
                "Smart gets skill competences");
            Assert.AreEqual(0, s.HarvestBonusTenths, "Smart never cheats");
            Assert.AreEqual(0, s.SightBonus);
            Assert.AreEqual(0, s.StartGoldBonus);
        }

        [Test]
        public void AiPlayer_AdoptsTierCadence()
        {
            var dumb = new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.Dumb);
            var god = new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.God);
            var deflt = new AiPlayer(0, AiBehavior.LandAttack);
            Assert.AreEqual(50, dumb.ThinkPeriodTicks);
            Assert.AreEqual(12, god.ThinkPeriodTicks);
            Assert.AreEqual(25, deflt.ThinkPeriodTicks, "default tier is Normal");
            Assert.AreEqual(AiTier.Normal, deflt.Tier);
        }
    }
}
