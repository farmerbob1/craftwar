using NUnit.Framework;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Ai.Spatial;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The self-boxing invariant: ReachabilityProbe must reject a placement that
    /// severs the base's open interior from its mine / a map exit, using the
    /// OCCUPANCY layer (buildings-as-walls), which the terrain-only region map
    /// cannot see. These scenarios reproduce the "Garden of War" failure in
    /// miniature: a wall of the AI's own buildings with a single gap.
    /// </summary>
    public class AiReachabilityTests
    {
        const int W = 26, H = 18;
        const byte Player = 0;

        // Open grass map: base (hall+worker) on the left, a neutral gold mine on
        // the right. Nothing between them but open ground.
        static PudFile OpenMap()
        {
            var pud = new PudFile { Width = W, Height = H };
            pud.Tiles = new ushort[W * H];
            pud.MoveMap = new ushort[W * H];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;   // grass
                pud.MoveMap[i] = 0x0001; // land-passable
            }
            AiTestHarness.Seat(pud, 0, PudOwner.Computer, Race.Human);
            AiTestHarness.Seat(pud, 1, PudOwner.Computer, Race.Orc);
            pud.StartGold[0] = pud.StartGold[1] = 2000;
            pud.StartLumber[0] = pud.StartLumber[1] = 1000;

            AiTestHarness.Place(pud, 0, UnitTypeId.TownHall, 2, 2);
            AiTestHarness.Place(pud, 0, UnitTypeId.Peasant, 2, 8);
            pud.Units.Add(new PudUnitEntry
            {
                X = 20, Y = 8, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 25,
            });
            return pud;
        }

        // Stack farms in columns 12-13 to build a wall, leaving rows [gapY, gapY+1]
        // open when gap==true, or fully sealed when gap==false.
        static void BuildWall(GameSim sim, int gapY, bool gap)
        {
            for (int y = 0; y < H; y += 2)
            {
                if (gap && (y == gapY))
                    continue; // leave the 2-tall gap open
                sim.State.SpawnUnit((ushort)UnitTypeId.Farm, Player, 12, (ushort)y);
            }
        }

        static ReachabilityProbe Begin(GameSim sim)
        {
            var probe = new ReachabilityProbe();
            AiQueriesAnchor(sim, out int ax, out int ay);
            probe.BeginDecision(sim.State, Player, ax, ay);
            return probe;
        }

        static void AiQueriesAnchor(GameSim sim, out int ax, out int ay) =>
            Craftwar.Sim.Ai.AiQueries.FindBaseAnchor(sim.State, Player, out ax, out ay);

        [Test]
        public void Rejects_PlacementThatSealsTheGap()
        {
            var sim = AiTestHarness.Boot(OpenMap(), seed: 5);
            Assume.That(sim.State.Footprint((ushort)UnitTypeId.Farm), Is.EqualTo(2),
                "test geometry assumes a 2x2 farm");
            BuildWall(sim, gapY: 8, gap: true);

            var probe = Begin(sim);

            // A farm dropped on the gap (top-left 12,8 covers cols 12-13 rows 8-9)
            // seals the base off from the mine and the right exit.
            Assert.IsFalse(
                probe.CandidateKeepsConnectivity((ushort)UnitTypeId.Farm, 12, 8),
                "sealing the only gap must be rejected");
        }

        [Test]
        public void Allows_PlacementThatKeepsTheGapOpen()
        {
            var sim = AiTestHarness.Boot(OpenMap(), seed: 5);
            BuildWall(sim, gapY: 8, gap: true);
            var probe = Begin(sim);

            // A farm in the left interior does not touch the gap.
            Assert.IsTrue(
                probe.CandidateKeepsConnectivity((ushort)UnitTypeId.Farm, 4, 12),
                "a placement that leaves the corridor open must be allowed");
        }

        [Test]
        public void DoesNotBlame_TargetThatWasAlreadyDisconnected()
        {
            var sim = AiTestHarness.Boot(OpenMap(), seed: 5);
            // Fully sealed wall — the mine is already unreachable at baseline.
            BuildWall(sim, gapY: 8, gap: false);
            var probe = Begin(sim);

            // Any left-interior placement is fine: it did not cause the pre-existing
            // disconnection, so it must not be blocked (else the AI deadlocks).
            Assert.IsTrue(
                probe.CandidateKeepsConnectivity((ushort)UnitTypeId.Farm, 4, 12),
                "a pre-existing disconnect must not freeze all building");
        }

        [Test]
        public void OpenMap_AnyReasonableSiteIsConnected()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 5);
            var probe = new ReachabilityProbe();
            AiQueriesAnchor(sim, out int ax, out int ay);
            probe.BeginDecision(sim.State, Player, ax, ay);

            // On a wide-open map, a plot a few tiles from the hall keeps everything
            // reachable.
            Assert.IsTrue(
                probe.CandidateKeepsConnectivity((ushort)UnitTypeId.Farm, ax + 3, ay + 3));
        }

        // ---- AiSitePlanner integration: never returns a sealing plot ----

        [Test]
        public void SitePlanner_NeverReturnsASealingPlot()
        {
            var sim = AiTestHarness.Boot(OpenMap(), seed: 5);
            BuildWall(sim, gapY: 8, gap: true);
            AiQueriesAnchor(sim, out int ax, out int ay);

            var planner = new AiSitePlanner();
            bool found = planner.FindSite(sim.State, Player, (ushort)UnitTypeId.Farm,
                ax, ay, AiSiteSearch.MaxRadius, builderPacked: 0,
                blacklist: null, threat: null, out int x, out int y);

            Assert.IsTrue(found, "a connectivity-safe plot exists and must be found");
            Assert.IsFalse(x == 12 && y == 8, "must not choose the gap-sealing plot");

            // Independently confirm the chosen plot keeps the base connected.
            var probe = new ReachabilityProbe();
            probe.BeginDecision(sim.State, Player, ax, ay);
            Assert.IsTrue(probe.CandidateKeepsConnectivity((ushort)UnitTypeId.Farm, x, y),
                "the planner's choice must pass the connectivity gate");
        }

        [Test]
        public void SitePlanner_FindsAPlotOnOpenMap()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 5);
            AiQueriesAnchor(sim, out int ax, out int ay);
            var planner = new AiSitePlanner();
            Assert.IsTrue(planner.FindSite(sim.State, Player, (ushort)UnitTypeId.Farm,
                ax, ay, AiSiteSearch.MaxRadius, 0, null, null, out _, out _));
        }
    }
}
