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
    }

    /// <summary>
    /// The single source of truth for "may this building go here". The sim
    /// enforces it on the builder's arrival and the placement ghost previews it;
    /// keeping one implementation means the ghost can never promise a site the
    /// sim then rejects.
    /// </summary>
    public static class BuildSite
    {
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

                    // An oil platform is raised *on* its patch, so the patch is the
                    // one occupant that does not block — every other unit does.
                    if (kind == SiteKind.OilPlatform && IsOilPatch(state, occ))
                    {
                        patchPacked = occ;
                        continue;
                    }
                    return SiteBlock.Occupied;
                }

            if (kind == SiteKind.OilPlatform && patchPacked == 0)
                result = SiteBlock.NoOilPatch;
            return result;
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

        static bool IsOilPatch(GameState state, uint packed)
        {
            var id = UnitId.FromPacked(packed);
            if (!state.TryGetUnitIndex(id, out int i)) return false;
            ref Unit u = ref state.Units[i];
            return u.IsAlive && state.Rules.Units[u.TypeId].Is(UnitTypeFlags.OilPatch);
        }
    }
}
