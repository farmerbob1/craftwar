using NUnit.Framework;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim.Tests
{
    public class AiBuildTests
    {
        [Test]
        public void SiteSearch_FindsValidSites_Deterministically()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 3);
            ushort farm = (ushort)UnitTypeId.Farm;

            Assert.IsTrue(AiSiteSearch.FindSite(sim.State, farm, 8, 8,
                AiSiteSearch.MaxRadius, 0, out int x1, out int y1));
            Assert.IsTrue(BuildSite.IsValid(sim.State, farm, x1, y1),
                "the found site must pass the sim's own placement rule");

            Assert.IsTrue(AiSiteSearch.FindSite(sim.State, farm, 8, 8,
                AiSiteSearch.MaxRadius, 0, out int x2, out int y2));
            Assert.AreEqual((x1, y1), (x2, y2), "identical state, identical site");
        }

        [Test]
        public void SiteSearch_RespectsMineKeepOut()
        {
            var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 3);
            // Anchor right next to the slot-0 mine (18,6 size 3): every valid
            // site must stay clear of the mine footprint grown by 3.
            Assert.IsTrue(AiSiteSearch.FindSite(sim.State, (ushort)UnitTypeId.Farm,
                17, 7, AiSiteSearch.MaxRadius, 0, out int x, out int y));
            bool overlapsLane = x + 2 + 1 > 18 - 3 && 18 + 3 + 3 > x - 1
                && y + 2 + 1 > 6 - 3 && 6 + 3 + 3 > y - 1;
            Assert.IsFalse(overlapsLane, $"site ({x},{y}) is inside the mine lane");
        }

        [Test]
        public void SiteSearch_BoxedInAnchor_ReturnsFalse()
        {
            // A 2x2 land island in a sea of tree-blocked tiles: terrain admits
            // a farm footprint but the perimeter-open rule (>=3 passable
            // neighbours) must reject it, leaving no site at all.
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0070;
                pud.MoveMap[i] = 0x0081; // blocked by trees
            }
            for (int y = 15; y <= 16; y++)
                for (int x = 15; x <= 16; x++)
                {
                    pud.Tiles[y * 32 + x] = 0x0050;
                    pud.MoveMap[y * 32 + x] = 0x0001;
                }
            AiTestHarness.Seat(pud, 0, PudOwner.Computer, Race.Human);
            var sim = AiTestHarness.Boot(pud, seed: 3);

            Assert.IsFalse(AiSiteSearch.FindSite(sim.State, (ushort)UnitTypeId.Farm,
                15, 15, AiSiteSearch.MaxRadius, 0, out _, out _));
        }

        [Test]
        public void RaceMap_CoversEveryRoleBothRaces()
        {
            foreach (AiUnit role in System.Enum.GetValues(typeof(AiUnit)))
            {
                Assert.AreNotEqual(UnitTypeId.None, AiRaceMap.Unit(role, Race.Human), $"human {role}");
                Assert.AreNotEqual(UnitTypeId.None, AiRaceMap.Unit(role, Race.Orc), $"orc {role}");
            }
            foreach (AiUpgrade u in System.Enum.GetValues(typeof(AiUpgrade)))
            {
                Assert.AreNotEqual(UpgradeId.None, AiRaceMap.Upgrade(u, Race.Human), $"human {u}");
                Assert.AreNotEqual(UpgradeId.None, AiRaceMap.Upgrade(u, Race.Orc), $"orc {u}");
            }
        }
    }
}
