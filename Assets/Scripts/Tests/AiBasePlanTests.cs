using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Base-layout planning for the higher tiers: a clustered, non-self-boxing
    /// placement that replaces the naive spiral. Pure/deterministic; the
    /// integration case proves a planning AI still functions (builds a base and
    /// gets its army out) rather than walling itself in.
    /// </summary>
    public class AiBasePlanTests
    {
        static (GameSim sim, int anchorX, int anchorY, int hallX, int hallY, int hallSize)
            BootWithHall()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 3);
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == 0 && u.TypeId == (ushort)UnitTypeId.TownHall)
                    return (sim, u.TileX, u.TileY, u.TileX, u.TileY, sim.State.Footprint(u.TypeId));
            }
            Assert.Fail("no town hall");
            return default;
        }

        [Test]
        public void FindSite_IsValidAndDeterministic()
        {
            var (sim, ax, ay, _, _, _) = BootWithHall();
            ushort barracks = (ushort)UnitTypeId.HumanBarracks;

            bool ok1 = AiBasePlan.FindSite(sim.State, 0, barracks, ax, ay, 20, 0, null,
                out int x1, out int y1);
            bool ok2 = AiBasePlan.FindSite(sim.State, 0, barracks, ax, ay, 20, 0, null,
                out int x2, out int y2);

            Assert.IsTrue(ok1, "a barracks plot exists near the hall");
            Assert.AreEqual(x1, x2, "deterministic");
            Assert.AreEqual(y1, y2);
            Assert.AreEqual(SiteBlock.None,
                BuildSite.Check(sim.State, barracks, x1, y1, 0, out _),
                "the planned plot must actually be buildable");
        }

        [Test]
        public void FindSite_ClustersAgainstAnExistingBuilding()
        {
            var (sim, ax, ay, hx, hy, hs) = BootWithHall();
            ushort barracks = (ushort)UnitTypeId.HumanBarracks;
            Assert.IsTrue(AiBasePlan.FindSite(sim.State, 0, barracks, ax, ay, 20, 0, null,
                out int x, out int y));

            int size = sim.State.Footprint(barracks);
            int gap = RectGap(x, y, size, hx, hy, hs);
            Assert.AreEqual(0, gap,
                "the clustered plan tucks the plot right against the hall, not off in a spiral");
        }

        /// <summary>Chebyshev gap between two footprints; 0 = touching.</summary>
        static int RectGap(int ax, int ay, int asz, int bx, int by, int bsz)
        {
            int gx = System.Math.Max(0, System.Math.Max(ax - (bx + bsz), bx - (ax + asz)));
            int gy = System.Math.Max(0, System.Math.Max(ay - (by + bsz), by - (ay + asz)));
            return System.Math.Max(gx, gy);
        }

        [Test]
        public void SmartAi_BuildsABaseAndGetsItsArmyOut()
        {
            // A planning (Smart) AI vs an inert opponent: if placement walled it in,
            // it could neither finish its build order nor march an army out.
            // First muster on this map's slow economy lands ~t14k (the same for
            // the naive baseline), so give it headroom rather than assert a pace.
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 5);
            var ais = new List<AiPlayer>
            {
                new AiPlayer(0, AiBehavior.LandAttack, null, AiTier.Smart),
            };
            int waves = 0;
            AiTestHarness.RunAiMatch(sim, ais, 20000,
                onCommand: (t, c) =>
                {
                    if (c.Op == CommandOp.AttackMove && c.Player == 0)
                        waves++;
                });

            int buildings = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == 0 && (u.Flags & UnitFlags.Building) != 0)
                    buildings++;
            }
            Assert.GreaterOrEqual(buildings, 4, "the planning AI builds out a base");
            Assert.GreaterOrEqual(waves, 1, "and its army can leave the base");
        }
    }
}
