namespace Craftwar.Sim
{
    public enum SiteKind : byte
    {
        /// <summary>Ordinary land building: footprint walkable and clear.</summary>
        Land = 0,
        /// <summary>
        /// Shipyard / foundry / refinery. These sit *in* the water against the
        /// coast, so no footprint tile may carry SQ_LAND (utype.h
        /// SQ_SHORE_UNBUILD_MASK). A land worker builds them from the bank.
        /// </summary>
        Shore = 1,
        /// <summary>
        /// Oil platform. Open water only (SQ_OILPATCH_UNPLACE_MASK bars shore
        /// and land) and it must be raised on an existing oil patch.
        /// </summary>
        OilPlatform = 2,
    }

    public enum SiteBlock : byte
    {
        None = 0,
        OutOfBounds,
        BadTerrain,
        Occupied,
        /// <summary>An oil platform was sited on open water with no patch under it.</summary>
        NoOilPatch,
        /// <summary>A town hall was sited within <see cref="BuildSite.MinGoldMineGap"/>
        /// tiles of a gold mine (collide.c PLACE_TOWNHALL_NEAR_GOLD_ERR).</summary>
        TooCloseToGoldMine,
    }

    /// <summary>
    /// The single source of truth for "may this building go here". The sim
    /// enforces it on the builder's arrival and the placement ghost previews it;
    /// keeping one implementation means the ghost can never promise a site the
    /// sim then rejects.
    /// </summary>
    public static class BuildSite
    {
        /// <summary>collide.c MIN_GOLD_DIST: the gap between a town hall's
        /// footprint and a gold mine's must be at least this many tiles.</summary>
        public const int MinGoldMineGap = 3;

        public static SiteKind KindOf(GameState state, ushort buildType)
        {
            ref var row = ref state.Rules.Units[buildType];
            if (row.Is(UnitTypeFlags.OilSource)) return SiteKind.OilPlatform;
            if (row.Is(UnitTypeFlags.ShoreBuilding)) return SiteKind.Shore;
            return SiteKind.Land;
        }

        public static bool IsValid(GameState state, ushort buildType, int tileX, int tileY,
            uint builderPacked = 0) =>
            Check(state, buildType, tileX, tileY, builderPacked, out _) == SiteBlock.None;

        /// <summary>
        /// Why the site fails, or None. <paramref name="patchPacked"/> returns the
        /// oil patch an OilPlatform site consumes (0 for other kinds).
        /// </summary>
        public static SiteBlock Check(GameState state, ushort buildType, int tileX, int tileY,
            uint builderPacked, out uint patchPacked)
        {
            patchPacked = 0;
            var terrain = state.Terrain;
            if (terrain == null) return SiteBlock.None;

            var kind = KindOf(state, buildType);
            int size = state.Footprint(buildType);
            var result = SiteBlock.None;

            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                {
                    int x = tileX + dx, y = tileY + dy;
                    if (!terrain.InBounds(x, y)) return SiteBlock.OutOfBounds;

                    if (!TerrainOk(terrain, kind, x, y)) return SiteBlock.BadTerrain;

                    uint occ = state.OccupancySurface[y * terrain.Width + x];
                    if (occ == 0 || occ == builderPacked) continue;
                    return SiteBlock.Occupied;
                }

            if (IsTownHall(buildType) && TooCloseToGoldMine(state, tileX, tileY, size))
                return SiteBlock.TooCloseToGoldMine;

            // Patches are deliberately absent from the occupancy layer (they do
            // not block ships), so find the one under this site by unit scan.
            if (kind == SiteKind.OilPlatform)
            {
                patchPacked = FindPatchAt(state, tileX, tileY, size);
                if (patchPacked == 0)
                    result = SiteBlock.NoOilPatch;
            }
            return result;
        }

        static bool IsTownHall(ushort buildType) =>
            buildType == (ushort)UnitTypeId.TownHall || buildType == (ushort)UnitTypeId.GreatHall;

        /// <summary>
        /// collide.c mtx_check_near: blocked when the gap between the two
        /// footprints — per axis, then Chebyshev-combined, in the same
        /// touching-is-1 convention as <c>GameSim.FootprintDistance</c> (so a
        /// footprint immediately next to the mine, with zero empty tiles
        /// between them, measures 1) — is <see cref="MinGoldMineGap"/> tiles
        /// or less: 3 in that convention is two actual empty tiles, matching
        /// MIN_GOLD_DIST's "at least 3 tiles between them". Gold remaining
        /// doesn't matter; even a played-out mine still keeps the site clear.
        /// </summary>
        static bool TooCloseToGoldMine(GameState state, int tileX, int tileY, int size)
        {
            for (int i = 0; i < state.HighestUnitIndex; i++)
            {
                ref Unit u = ref state.Units[i];
                if (!u.IsAlive || !state.Rules.Units[u.TypeId].Is(UnitTypeFlags.GoldMine))
                    continue;
                int mineSize = state.Footprint(u.TypeId);
                int dx = Gap(tileX, size, u.TileX, mineSize);
                int dy = Gap(tileY, size, u.TileY, mineSize);
                if ((dx > dy ? dx : dy) <= MinGoldMineGap)
                    return true;
            }
            return false;
        }

        /// <summary>Tile gap between two same-axis spans, touching-is-1 (matches
        /// GameSim.FootprintDistance); 0 only when they actually overlap.</summary>
        static int Gap(int a, int aSize, int b, int bSize)
        {
            int d1 = b - (a + aSize - 1);
            int d2 = a - (b + bSize - 1);
            int d = d1 > d2 ? d1 : d2;
            return d < 0 ? 0 : d;
        }

        static bool TerrainOk(TerrainMap terrain, SiteKind kind, int x, int y) => kind switch
        {
            // Coast is not land-passable, so "no SQ_LAND bit" is exactly
            // "dockable water" — shore or open sea, either is a valid berth.
            SiteKind.Shore => terrain.IsPassable(MoveDomain.SeaDock, x, y),
            // Open water only: a platform may not straddle the coast strip.
            SiteKind.OilPlatform => terrain.IsPassable(MoveDomain.Sea, x, y),
            _ => terrain.IsPassable(MoveDomain.Land, x, y),
        };

        /// <summary>
        /// The oil patch a platform at this site would consume, or 0. Requires
        /// exact alignment: the platform replaces the patch tile-for-tile, as in
        /// the original, so a half-overlapping site is not a valid rig.
        /// </summary>
        public static uint FindPatchAt(GameState state, int tileX, int tileY, int size)
        {
            for (int i = 0; i < state.HighestUnitIndex; i++)
            {
                ref Unit u = ref state.Units[i];
                if (!u.IsAlive || !state.Rules.Units[u.TypeId].Is(UnitTypeFlags.OilPatch))
                    continue;
                if (u.TileX != tileX || u.TileY != tileY)
                    continue;
                if (state.Footprint(u.TypeId) != size)
                    continue;
                return new UnitId((ushort)i, u.Gen).Packed;
            }
            return 0;
        }

        /// <summary>
        /// Snap a click to the patch it lands on, so the player does not have to
        /// pixel-align a 3x3 rig by hand. Returns false if no patch is under it.
        /// </summary>
        public static bool TrySnapToPatch(GameState state, int tileX, int tileY,
            int size, out int snappedX, out int snappedY)
        {
            snappedX = tileX;
            snappedY = tileY;
            for (int i = 0; i < state.HighestUnitIndex; i++)
            {
                ref Unit u = ref state.Units[i];
                if (!u.IsAlive || !state.Rules.Units[u.TypeId].Is(UnitTypeFlags.OilPatch))
                    continue;
                int ps = state.Footprint(u.TypeId);
                // Does the proposed footprint touch this patch at all?
                if (tileX + size <= u.TileX || u.TileX + ps <= tileX) continue;
                if (tileY + size <= u.TileY || u.TileY + ps <= tileY) continue;
                snappedX = u.TileX;
                snappedY = u.TileY;
                return true;
            }
            return false;
        }
    }
}
