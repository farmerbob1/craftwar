using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The payoff: a higher difficulty tier outplays a lower one, tier and
    /// handicaps composed exactly as the app wires them (skill drives the
    /// AiPlayer; handicaps are baked into the slot's hashed PlayerState).
    ///
    /// Near-equal tiers (e.g. Normal vs Dumb, which differ only in think cadence)
    /// stalemate for a very long time before a dry map resolves — the known
    /// symmetric-mirror slowness — so most cases assert a development LEAD at a
    /// checkpoint rather than a full win; one decisive case plays to a finish.
    /// </summary>
    public class AiDifficultyTests
    {
        static void ApplyTierHandicap(ref SlotSetup s, AiTier t)
        {
            var p = AiTierTable.For(t);
            s.StartGoldBonus = p.StartGoldBonus;
            s.StartLumberBonus = p.StartLumberBonus;
            s.HarvestBonusTenths = p.HarvestBonusTenths;
            s.SightBonus = p.SightBonus;
        }

        static (GameSim sim, List<AiPlayer> ais) TierMatch(AiTier t0, AiTier t1, ulong seed)
        {
            var pud = AiTestHarness.TwoBaseMap();
            var setup = MatchSetup.FromPud(pud);
            ApplyTierHandicap(ref setup.Slots[0], t0);
            ApplyTierHandicap(ref setup.Slots[1], t1);
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault(), setup);
            var ais = new List<AiPlayer>
            {
                new AiPlayer(0, AiBehavior.LandAttack, null, t0),
                new AiPlayer(1, AiBehavior.LandAttack, null, t1),
            };
            return (sim, ais);
        }

        /// <summary>A rough position strength: army weighted over economy.</summary>
        static int Strength(GameSim sim, int slot)
        {
            int combat = 0, buildings = 0, workers = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (!u.IsAlive || u.Player != slot) continue;
                if ((u.Flags & UnitFlags.Building) != 0) buildings++;
                else if (sim.State.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon)) workers++;
                else if (sim.State.Rules.Units[u.TypeId].Is(UnitTypeFlags.CanAttack)) combat++;
            }
            return combat * 3 + buildings * 2 + workers;
        }

        static void AssertOutdevelops(AiTier strong, AiTier weak, ulong seed, int atTick)
        {
            var (sim, ais) = TierMatch(strong, weak, seed);
            AiTestHarness.RunAiMatch(sim, ais, atTick);
            int s = Strength(sim, 0), w = Strength(sim, 1);
            Assert.Greater(s, w,
                $"{strong} should out-develop {weak} by tick {atTick} (seed {seed}): {s} vs {w}");
        }

        static void AssertBeats(AiTier strong, AiTier weak, ulong seed, int budget)
        {
            var (sim, ais) = TierMatch(strong, weak, seed);
            int ticks = AiTestHarness.RunAiMatch(sim, ais, budget, stop: s =>
                s.State.Players[0].Outcome != PlayerOutcome.Playing
                || s.State.Players[1].Outcome != PlayerOutcome.Playing);
            Assert.Less(ticks, budget, $"{strong} vs {weak} (seed {seed}) must resolve");
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[0].Outcome,
                $"{strong} should beat {weak} (seed {seed})");
            Assert.AreEqual(PlayerOutcome.Defeated, sim.State.Players[1].Outcome);
        }

        // Normal and Dumb are the same behaviour differing only in think cadence,
        // so a faster-thinking Normal shows a clean development lead at a checkpoint
        // (neither is more aggressive, so the unit-count proxy is fair here). The
        // competence-differentiated tiers instead play to an outright win — their
        // aggression trades units, so a snapshot count would understate them.
        [Test]
        public void Normal_OutdevelopsDumb() => AssertOutdevelops(AiTier.Normal, AiTier.Dumb, 41, 20000);

        [Test]
        public void Smart_BeatsDumb() => AssertBeats(AiTier.Smart, AiTier.Dumb, 42, 80000);

        [Test]
        public void God_BeatsNormal() => AssertBeats(AiTier.God, AiTier.Normal, 43, 80000);

        [Test]
        public void God_BeatsDumb() => AssertBeats(AiTier.God, AiTier.Dumb, 44, 80000);
    }
}
