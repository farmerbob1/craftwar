using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class PathfinderTests
    {
        /// <summary>Build a terrain from ASCII art: '.' land, '~' water, '#' blocked.</summary>
        static TerrainMap Make(params string[] rows)
        {
            var map = new TerrainMap(rows[0].Length, rows.Length);
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[0].Length; x++)
                {
                    char c = rows[y][x];
                    map.SetPassable(MoveDomain.Air, x, y, true);
                    map.SetPassable(MoveDomain.Land, x, y, c == '.');
                    map.SetPassable(MoveDomain.Sea, x, y, c == '~');
                }
            map.RebuildClearance();
            return map;
        }

        static ushort[] Buffer(TerrainMap m) => new ushort[m.Width * m.Height];

        [Test]
        public void StraightLine_UsesDiagonalsAndReachesGoal()
        {
            var map = Make(
                "......",
                "......",
                "......");
            var pf = new Pathfinder(map);
            var path = Buffer(map);
            int steps = pf.FindPath(MoveDomain.Land, 1, 0, 0, 5, 2, path);
            Assert.AreEqual(5, steps, "octile distance: 5 steps (3 diagonal + 2 straight or similar)");
            Assert.AreEqual(2 * map.Width + 5, path[steps - 1], "ends at goal");
        }

        [Test]
        public void Wall_ForcesDetour()
        {
            var map = Make(
                ".#.",
                ".#.",
                "...");
            var pf = new Pathfinder(map);
            var path = Buffer(map);
            int steps = pf.FindPath(MoveDomain.Land, 1, 0, 0, 2, 0, path);
            Assert.Greater(steps, 2, "must route around the wall");
            Assert.AreEqual(2, path[steps - 1], "ends at goal tile (0-row x=2)");
        }

        [Test]
        public void UnreachableGoal_YieldsClosestReachablePath()
        {
            var map = Make(
                "..#..",
                "..#..",
                "..#..");
            var pf = new Pathfinder(map);
            var path = Buffer(map);
            int steps = pf.FindPath(MoveDomain.Land, 1, 0, 1, 4, 1, path);
            Assert.Greater(steps, 0, "should move toward the wall, not refuse");
            int endX = path[steps - 1] % map.Width;
            Assert.AreEqual(1, endX, "stops adjacent to the wall (closest column)");
        }

        [Test]
        public void TwoByTwoUnit_RejectsNarrowGap_TakesWideRoute()
        {
            // 1-wide gap at top, 2-wide corridor at bottom.
            var map = Make(
                "..#..",
                "..#..",
                ".....",
                ".....");
            var pf = new Pathfinder(map);
            var path = Buffer(map);

            int steps1 = pf.FindPath(MoveDomain.Land, 1, 0, 0, 4, 0, path);
            Assert.Greater(steps1, 0, "1x1 finds a route");

            int steps2 = pf.FindPath(MoveDomain.Land, 2, 0, 0, 3, 0, path);
            Assert.Greater(steps2, 0, "2x2 must path via the wide corridor");
            bool dippedDown = false;
            for (int i = 0; i < steps2; i++)
                if (path[i] / map.Width >= 2)
                    dippedDown = true;
            Assert.IsTrue(dippedDown, "2x2 route must use the bottom corridor");
        }

        [Test]
        public void SeaUnit_CannotPathOverLand()
        {
            var map = Make(
                "~~..~~",
                "~~..~~",
                "~~..~~");
            var pf = new Pathfinder(map);
            var path = Buffer(map);
            int steps = pf.FindPath(MoveDomain.Sea, 1, 0, 1, 5, 1, path);
            if (steps > 0)
            {
                int endX = path[steps - 1] % map.Width;
                Assert.LessOrEqual(endX, 1, "ship must stop at the coastline");
            }
        }

        [Test]
        public void NoCornerCutting()
        {
            var map = Make(
                ".#",
                "..");
            var pf = new Pathfinder(map);
            var path = Buffer(map);
            // 0,0 -> 1,1 diagonally would cut the corner of the blocked tile.
            int steps = pf.FindPath(MoveDomain.Land, 1, 0, 0, 1, 1, path);
            Assert.AreEqual(2, steps, "must go down then across, not diagonal through the corner");
        }
    }
}
