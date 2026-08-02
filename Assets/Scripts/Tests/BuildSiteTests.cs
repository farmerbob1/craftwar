using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// collide.c mtx_check_near / PLACE_TOWNHALL_NEAR_GOLD_ERR: a town hall may
    /// not be sited within MIN_GOLD_DIST (3) tiles of a gold mine, checked as a
    /// footprint gap (like GameSim.FootprintDistance), not a naive centre
    /// distance or plain overlap test.
    /// </summary>
    public class BuildSiteTests
    {
        static PudFile FlatMap()
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0001; // land
            }
            pud.Owner[0] = (byte)PudOwner.Human;
            return pud;
        }

        [Test]
        public void TownHall_MayNotBeBuiltWithinThreeTilesOfAGoldMine()
        {
            var pud = FlatMap();
            const int mineX = 15, mineY = 15;
            pud.Units.Add(new PudUnitEntry
            { X = mineX, Y = mineY, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 4 });
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            ushort hall = (ushort)UnitTypeId.TownHall;
            int mineSize = sim.State.Footprint((ushort)UnitTypeId.GoldMine);
            int mineRightEdge = mineX + mineSize; // one past the mine's last tile

            // Immediately adjacent, no gap at all — blocked.
            Assert.AreEqual(SiteBlock.TooCloseToGoldMine,
                BuildSite.Check(sim.State, hall, mineRightEdge, mineY, 0, out _),
                "touching the mine's footprint must be blocked");

            // Two empty tiles of gap — still inside the 3-tile minimum
            // (MIN_GOLD_DIST wants at least 3 empty tiles between them).
            Assert.AreEqual(SiteBlock.TooCloseToGoldMine,
                BuildSite.Check(sim.State, hall, mineRightEdge + 2, mineY, 0, out _));

            // Exactly 3 empty tiles of gap — the minimum is met.
            Assert.AreEqual(SiteBlock.None,
                BuildSite.Check(sim.State, hall, mineRightEdge + BuildSite.MinGoldMineGap, mineY, 0, out _),
                "a full 3-tile gap must be allowed");

            // Diagonal: both axes just touching at the corner — the
            // Chebyshev-combined gap must still catch this, not just a
            // same-row/same-column check.
            Assert.AreEqual(SiteBlock.TooCloseToGoldMine,
                BuildSite.Check(sim.State, hall, mineRightEdge, mineY + mineSize, 0, out _),
                "diagonal corner-touching must also be blocked");

            // Diagonal, but far enough on both axes — safe.
            int diagFar = mineRightEdge + BuildSite.MinGoldMineGap;
            Assert.AreEqual(SiteBlock.None,
                BuildSite.Check(sim.State, hall, diagFar, diagFar, 0, out _));
        }

        [Test]
        public void OtherBuildings_IgnoreTheGoldMineGap()
        {
            var pud = FlatMap();
            pud.Units.Add(new PudUnitEntry
            { X = 15, Y = 15, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 4 });
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());

            // A farm may sit right next to the mine — only town halls care.
            Assert.AreEqual(SiteBlock.None,
                BuildSite.Check(sim.State, (ushort)UnitTypeId.Farm, 18, 15, 0, out _));
        }

        [Test]
        public void DepletedGoldMine_StillKeepsTheHallAway()
        {
            var pud = FlatMap();
            pud.Units.Add(new PudUnitEntry
            { X = 15, Y = 15, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 0 });
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Units[0].ResourceAmount = 0;

            Assert.AreEqual(SiteBlock.TooCloseToGoldMine,
                BuildSite.Check(sim.State, (ushort)UnitTypeId.TownHall, 18, 15, 0, out _),
                "the original doesn't check remaining gold, only proximity");
        }
    }
}
