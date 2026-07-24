using System.Collections.Generic;
using NUnit.Framework;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Ai.Spatial;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The headline regression: reproduce the "Garden of War" failure — many AIs on
    /// a wide-open map — and prove no AI ever walls itself in. On flat grass the only
    /// thing that can disconnect a base from its mine or a map exit is the AI's own
    /// buildings, so an occupancy-aware reachability check at checkpoints is a direct
    /// test of the old bug. Also confirms the economy stays live (workers harvesting)
    /// and the run is deterministic.
    /// </summary>
    public class AiAntiBoxingTests
    {
        const int Seats = 7;

        [Test]
        public void SevenAis_NeverSelfBox_OverTwentyThousandTicks()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.MeleeMap(Seats), seed: 1234);
            var ais = AiTestHarness.CreateAis(sim);
            Assert.AreEqual(Seats, ais.Count, "every seat should be a computer");

            var probe = new ReachabilityProbe();
            // Checkpoints span the window where the old AI boxed itself in ("within
            // minutes" = a few thousand ticks) through the mid-game build-up.
            var checkpoints = new HashSet<int> { 2000, 4000, 7000, 11000, 16000, 19900 };
            int worstBoxed = -1;

            AiTestHarness.RunAiMatch(sim, ais, 20000, stop: s =>
            {
                if (!checkpoints.Contains(s.State.Tick))
                    return false;
                for (byte p = 0; p < SimConstants.MaxPlayers; p++)
                {
                    ref PlayerState ps = ref s.State.Players[p];
                    if (ps.Controller != Controller.Computer || !ps.InGame
                        || ps.Outcome != PlayerOutcome.Playing)
                        continue;
                    if (!AiQueries.FindBaseAnchor(s.State, p, out int ax, out int ay))
                        continue; // no base left (eliminated) — nothing to box in
                    probe.BeginDecision(s.State, p, ax, ay);
                    // The base must still reach a map exit, and — if a mine remains —
                    // reach it. A self-boxed AI fails exactly here.
                    if (!probe.BaseEdgeReachable || (probe.HasMine && !probe.BaseMineReachable))
                        worstBoxed = p;
                }
                return false;
            });

            Assert.AreEqual(-1, worstBoxed,
                $"AI slot {worstBoxed} walled itself off from its mine or a map exit");
        }

        [Test]
        public void EconomyStaysLive_WorkersKeepHarvesting()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.MeleeMap(Seats), seed: 55);
            var ais = AiTestHarness.CreateAis(sim);
            // Give the AIs time to build up, then confirm each surviving base still
            // has workers on the harvest cycle — impossible if boxed off the mine.
            AiTestHarness.RunAiMatch(sim, ais, 6000);

            int checkedAis = 0;
            for (byte p = 0; p < SimConstants.MaxPlayers; p++)
            {
                ref PlayerState ps = ref sim.State.Players[p];
                if (ps.Controller != Controller.Computer || !ps.InGame
                    || ps.Outcome != PlayerOutcome.Playing)
                    continue;
                if (!AiQueries.FindBaseAnchor(sim.State, p, out _, out _))
                    continue;
                checkedAis++;
                Assert.Greater(HarvestingWorkers(sim, p), 0,
                    $"AI slot {p} has no harvesting workers — economy stalled");
            }
            Assert.Greater(checkedAis, 0, "expected surviving AIs to check");
        }

        [Test]
        public void SevenAiMelee_IsDeterministic()
        {
            uint RunOnce()
            {
                var sim = AiTestHarness.Boot(AiTestHarness.MeleeMap(Seats), seed: 909);
                AiTestHarness.RunAiMatch(sim, AiTestHarness.CreateAis(sim), 8000);
                return sim.State.ComputeHash();
            }
            Assert.AreEqual(RunOnce(), RunOnce());
        }

        static int HarvestingWorkers(GameSim sim, int slot)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == slot && u.Order == OrderType.Harvest)
                    n++;
            }
            return n;
        }
    }
}
