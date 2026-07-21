namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Deterministic building-site search: ring spiral around an anchor, each
    /// ring walked in a fixed order, first tile that passes BuildSite.Check
    /// plus two keep-the-base-usable heuristics wins. Pure function of state.
    /// </summary>
    public static class AiSiteSearch
    {
        public const int MinRadius = 2;
        public const int MaxRadius = 20;

        /// <summary>Tiles on the square ring at Chebyshev distance r.</summary>
        public static int RingLength(int r) => r == 0 ? 1 : 8 * r;

        /// <summary>
        /// The i-th tile of ring r in fixed order: top edge left→right, right
        /// edge top→bottom, bottom edge right→left, left edge bottom→top.
        /// </summary>
        public static void RingTile(int cx, int cy, int r, int i, out int x, out int y)
        {
            if (r == 0)
            {
                x = cx;
                y = cy;
                return;
            }
            int side = 2 * r; // tiles per edge, excluding the last corner
            if (i < side)
            {
                x = cx - r + i;
                y = cy - r;
            }
            else if (i < 2 * side)
            {
                x = cx + r;
                y = cy - r + (i - side);
            }
            else if (i < 3 * side)
            {
                x = cx + r - (i - 2 * side);
                y = cy + r;
            }
            else
            {
                x = cx - r;
                y = cy + r - (i - 3 * side);
            }
        }

        /// <summary>
        /// Find a top-left tile for `buildType` near the anchor. The builder's
        /// packed id lets its own tile count as free, exactly as the sim's
        /// arrival check will judge it.
        /// </summary>
        public static bool FindSite(GameState s, ushort buildType, int anchorX, int anchorY,
            int maxRadius, uint builderPacked, out int tileX, out int tileY)
        {
            int size = s.Footprint(buildType);
            for (int r = MinRadius; r <= maxRadius; r++)
            {
                int len = RingLength(r);
                for (int i = 0; i < len; i++)
                {
                    RingTile(anchorX, anchorY, r, i, out int x, out int y);
                    if (FindSiteAt(s, buildType, size, x, y, builderPacked))
                    {
                        tileX = x;
                        tileY = y;
                        return true;
                    }
                }
            }
            tileX = 0;
            tileY = 0;
            return false;
        }

        /// <summary>Would this exact top-left tile do? The single-candidate
        /// form of FindSite, for callers that walk the spiral themselves.</summary>
        public static bool FindSiteAt(GameState s, ushort buildType, int size,
            int tileX, int tileY, uint builderPacked)
        {
            if (BuildSite.Check(s, buildType, tileX, tileY, builderPacked, out _)
                != SiteBlock.None)
                return false;
            if (TouchesMineLane(s, tileX, tileY, size))
                return false;
            return PerimeterOpen(s, tileX, tileY, size);
        }

        /// <summary>
        /// Keep-out around gold mines: reject sites whose footprint (grown by
        /// one tile) overlaps a mine footprint grown by three, so the
        /// mine↔hall harvest lane never gets bricked over. Mirrors the
        /// original's mine clearance rule.
        /// </summary>
        static bool TouchesMineLane(GameState s, int tileX, int tileY, int size)
        {
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (!u.IsAlive || !s.Rules.Units[u.TypeId].Is(UnitTypeFlags.GoldMine))
                    continue;
                int ms = s.Footprint(u.TypeId);
                if (tileX - 1 < u.TileX + ms + 3 && u.TileX - 3 < tileX + size + 1
                    && tileY - 1 < u.TileY + ms + 3 && u.TileY - 3 < tileY + size + 1)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Require at least three land-passable tiles on the footprint's
        /// perimeter so the site cannot seal in its builder or fully plug a
        /// chokepoint.
        /// </summary>
        static bool PerimeterOpen(GameState s, int tileX, int tileY, int size)
        {
            var t = s.Terrain;
            int open = 0;
            for (int x = tileX - 1; x <= tileX + size; x++)
            {
                if (t.InBounds(x, tileY - 1) && t.IsPassable(MoveDomain.Land, x, tileY - 1))
                    open++;
                if (t.InBounds(x, tileY + size) && t.IsPassable(MoveDomain.Land, x, tileY + size))
                    open++;
            }
            for (int y = tileY; y < tileY + size; y++)
            {
                if (t.InBounds(tileX - 1, y) && t.IsPassable(MoveDomain.Land, tileX - 1, y))
                    open++;
                if (t.InBounds(tileX + size, y) && t.IsPassable(MoveDomain.Land, tileX + size, y))
                    open++;
            }
            return open >= 3;
        }
    }
}
