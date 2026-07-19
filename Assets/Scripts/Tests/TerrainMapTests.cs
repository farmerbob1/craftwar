using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The PUD SQM word is the original's SQ_* bit field, not an enumeration
    /// (utype.h). These lock the bitwise decode — in particular that coast is
    /// dockable-but-not-land, which is what shipyards, oil platforms and
    /// transport unloading are all defined in terms of.
    /// </summary>
    public class TerrainMapTests
    {
        /// <summary>A 1x1 map whose single tile carries the given SQM word.</summary>
        static TerrainMap FromSqm(ushort sqm, ushort tile = 0x0050)
        {
            var pud = new PudFile { Width = 1, Height = 1 };
            pud.Tiles = new ushort[] { tile };
            pud.MoveMap = new ushort[] { sqm };
            return TerrainMap.FromPud(pud);
        }

        static bool P(TerrainMap m, MoveDomain d) => m.IsPassable(d, 0, 0);

        [TestCase((ushort)0x0000, true, true, true, true, false, TestName = "Sqm_Bridge_IsLandSeaAndAir")]
        [TestCase((ushort)0x0001, true, false, false, true, false, TestName = "Sqm_Land")]
        [TestCase((ushort)0x0002, false, false, true, true, true, TestName = "Sqm_CoastCorner_DocksOnly")]
        [TestCase((ushort)0x0011, true, false, false, true, false, TestName = "Sqm_Dirt_IsLand")]
        [TestCase((ushort)0x0040, false, true, true, true, false, TestName = "Sqm_Water")]
        [TestCase((ushort)0x0081, false, false, false, true, false, TestName = "Sqm_ForestMountain_BlocksGround")]
        [TestCase((ushort)0x0082, false, false, true, true, true, TestName = "Sqm_Coast_DocksOnly")]
        [TestCase((ushort)0x0089, false, false, false, true, false, TestName = "Sqm_ComputerWall_BlocksGround")]
        [TestCase((ushort)0x008d, false, false, false, true, false, TestName = "Sqm_Wall_BlocksGround")]
        public void Sqm_DecodesToExpectedDomains(
            ushort sqm, bool land, bool sea, bool dock, bool air, bool shore)
        {
            var m = FromSqm(sqm);
            Assert.AreEqual(land, P(m, MoveDomain.Land), "land");
            Assert.AreEqual(sea, P(m, MoveDomain.Sea), "sea");
            Assert.AreEqual(dock, P(m, MoveDomain.SeaDock), "seadock");
            Assert.AreEqual(air, P(m, MoveDomain.Air), "air");
            Assert.AreEqual(shore, m.IsShore(0, 0), "shore");
        }

        [Test]
        public void Coast_IsDockableButNotSea_SoWarshipsStayOffIt()
        {
            var m = FromSqm(0x0082);
            Assert.IsTrue(P(m, MoveDomain.SeaDock), "a transport may dock on coast");
            Assert.IsFalse(P(m, MoveDomain.Sea), "a destroyer may not enter coast");
            Assert.IsFalse(P(m, MoveDomain.Land), "coast is not walkable (SQ_SHORE)");
        }

        [Test]
        public void Cave_BlocksFlyersButNotGround()
        {
            // 0x02xx per pud_format.txt: cave, no flying units allowed.
            var m = FromSqm(0x0201);
            Assert.IsTrue(P(m, MoveDomain.Land), "ground still walks a cave tile");
            Assert.IsFalse(P(m, MoveDomain.Air), "flyers are excluded from caves");
        }

        [Test]
        public void ForestTile_CarriesWood_AndChoppingOpensLandOnly()
        {
            // Forest is LAND|UNPASSABLE; the MTXM id distinguishes trees from mountains.
            var m = FromSqm(0x0081, tile: 0x0070);
            Assert.IsTrue(m.HasWood(0, 0), "0x007x is a forest tile");
            Assert.IsFalse(P(m, MoveDomain.Land));

            m.Chop(0, 0);
            Assert.IsFalse(m.HasWood(0, 0));
            Assert.IsTrue(P(m, MoveDomain.Land), "a felled tree opens the tile to land");
            Assert.IsFalse(P(m, MoveDomain.Sea), "and never to ships");
        }

        [Test]
        public void MountainTile_HasNoWood()
        {
            var m = FromSqm(0x0081, tile: 0x0030);
            Assert.IsFalse(m.HasWood(0, 0), "0x0081 alone is not enough — MTXM must say forest");
        }

        [Test]
        public void Clearance_IsTrackedForEveryDomain()
        {
            var pud = new PudFile { Width = 4, Height = 4 };
            pud.Tiles = new ushort[16];
            pud.MoveMap = new ushort[16];
            for (int i = 0; i < 16; i++) { pud.Tiles[i] = 0x0050; pud.MoveMap[i] = 0x0040; }
            var m = TerrainMap.FromPud(pud);

            // All water: a 4x4 open field for sea, dock and air; nothing for land.
            Assert.AreEqual(4, m.Clearance(MoveDomain.Sea, 0, 0));
            Assert.AreEqual(4, m.Clearance(MoveDomain.SeaDock, 0, 0));
            Assert.AreEqual(4, m.Clearance(MoveDomain.Air, 0, 0));
            Assert.AreEqual(0, m.Clearance(MoveDomain.Land, 0, 0));
        }
    }
}
