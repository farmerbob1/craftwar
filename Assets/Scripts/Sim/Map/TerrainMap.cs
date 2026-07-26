using Craftwar.Sim.Pud;

namespace Craftwar.Sim
{
    public enum MoveDomain : byte
    {
        Land = 0,
        Air = 1,
        Sea = 2,
        /// <summary>
        /// Water plus coast: the original's CLASS_WATER_CANDOCK, which only
        /// tankers and transports have. Coast is "unpassable unless CANDOCK",
        /// so a destroyer uses Sea and a transport uses SeaDock.
        /// </summary>
        SeaDock = 3,
    }

    /// <summary>
    /// The PUD SQM word is the original's SQ_* bit field verbatim, not an
    /// enumeration — decode it bitwise (utype.h).
    /// </summary>
    static class Sqm
    {
        public const ushort Land = 0x0001;
        public const ushort Shore = 0x0002;
        public const ushort PlayerWall = 0x0004;
        public const ushort ComputerWall = 0x0008;
        public const ushort Unbuildable = 0x0010;
        public const ushort Water = 0x0040;
        public const ushort Unpassable = 0x0080;
        public const ushort Cave = 0x0200;   // 0x02xx: no flying units

        // SQ_UNPASS_MASK minus the unit bits (occupancy is tracked separately).
        public const ushort BlocksLand = Unpassable | Water | Shore | PlayerWall | ComputerWall;
        // SQ_UNPASS_SHIP_MASK: a ship may not enter land or coast.
        public const ushort BlocksSea = Land | Shore;
        // SQ_UNPASS_SHIP_CANDOCK_MASK: a docking ship may enter coast, not land.
        public const ushort BlocksSeaDock = Land;
    }

    /// <summary>
    /// Static walkability derived from the PUD SQM movement map (the
    /// authoritative movement layer; MTXM is graphics). Per-domain passable
    /// bits plus NxN clearance grids so multi-tile units path correctly —
    /// a size-N unit may stand with its top-left on tile T iff
    /// Clearance(domain, T) >= N.
    /// </summary>
    public sealed class TerrainMap
    {
        public readonly int Width;
        public readonly int Height;

        public const int DomainCount = 4;

        readonly byte[] _passable;           // bit per MoveDomain
        readonly byte[][] _clearance;        // [domain][tile]
        readonly int[][] _region;            // [domain][tile]: connected-component label, 0 = impassable
        readonly int[] _regionStack;         // preallocated flood-fill stack
        readonly byte[] _wood;               // remaining wood units/100 per tile (1 = 100 lumber)
        readonly byte[] _shore;              // 1 = SQ_SHORE: where transports unload and shore buildings sit

        public TerrainMap(int width, int height)
        {
            Width = width;
            Height = height;
            _passable = new byte[width * height];
            _clearance = new byte[DomainCount][];
            _region = new int[DomainCount][];
            for (int d = 0; d < DomainCount; d++)
            {
                _clearance[d] = new byte[width * height];
                _region[d] = new int[width * height];
            }
            _regionStack = new int[width * height];
            _wood = new byte[width * height];
            _shore = new byte[width * height];
        }

        /// <summary>Forest per MTXM classification: solid 0x007x or boundary 0x07xx.</summary>
        public static bool IsForestTile(ushort tileId) =>
            (tileId >> 8) == 0x07 || ((tileId >> 8) == 0x00 && ((tileId >> 4) & 0xF) == 0x7);

        public bool HasWood(int x, int y) => InBounds(x, y) && _wood[y * Width + x] > 0;

        /// <summary>SQ_SHORE: the coast strip transports unload onto and shore buildings sit on.</summary>
        public bool IsShore(int x, int y) => InBounds(x, y) && _shore[y * Width + x] != 0;

        /// <summary>
        /// Running checksum over the two planes that mutate at runtime: remaining
        /// wood and the passability bits. The terrain is not part of GameState, so
        /// felled forest used to reach the state hash only indirectly, via the
        /// MTXM retile — and only where the retile actually changed a tile id.
        /// Hashing this closes that gap directly. Clearance and region labels are
        /// pure functions of passability, so they need no coverage of their own.
        /// </summary>
        public uint TerrainChecksum { get; private set; }

        uint CellContribution(int i) =>
            GameState.CellMix(i, (uint)_wood[i] | ((uint)_passable[i] << 8));

        uint RecomputeTerrainChecksum()
        {
            uint sum = 0;
            unchecked
            {
                for (int i = 0; i < _wood.Length; i++)
                    sum += CellContribution(i);
            }
            return sum;
        }

        void SeedTerrainChecksum() => TerrainChecksum = RecomputeTerrainChecksum();

        /// <summary>Recompute from scratch; null when the running value agrees.
        /// Deliberately does not repair a mismatch — a silently corrected
        /// checksum would hide the write site that skipped the funnel.</summary>
        public string VerifyTerrainChecksum()
        {
            uint recomputed = RecomputeTerrainChecksum();
            if (recomputed != TerrainChecksum)
                return $"TerrainChecksum {TerrainChecksum:X8} != recomputed {recomputed:X8}";
            return null;
        }

        /// <summary>Fell the tree at (x,y): frees the tile for land movement.</summary>
        public void Chop(int x, int y)
        {
            int i = y * Width + x;
            uint before = CellContribution(i);
            _wood[i] = 0;
            SetPassable(MoveDomain.Land, x, y, true);
            unchecked { TerrainChecksum += CellContribution(i) - before; }
            RebuildClearance();
        }

        public static TerrainMap FromPud(PudFile pud)
        {
            var map = new TerrainMap(pud.Width, pud.Height);
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                ushort sqm = pud.MoveMap[i];
                byte bits = 0;
                if ((sqm & Sqm.BlocksLand) == 0) bits |= 1 << (int)MoveDomain.Land;
                if ((sqm & Sqm.BlocksSea) == 0) bits |= 1 << (int)MoveDomain.Sea;
                if ((sqm & Sqm.BlocksSeaDock) == 0) bits |= 1 << (int)MoveDomain.SeaDock;
                if ((sqm & Sqm.Cave) == 0) bits |= 1 << (int)MoveDomain.Air;
                map._passable[i] = bits;
                map._shore[i] = (byte)((sqm & Sqm.Shore) != 0 ? 1 : 0);
                // Forest is LAND|UNPASSABLE; the MTXM id distinguishes trees from mountains.
                if ((sqm & (Sqm.Land | Sqm.Unpassable)) == (Sqm.Land | Sqm.Unpassable)
                    && IsForestTile(pud.Tiles[i]))
                    map._wood[i] = 1; // one tree = 100 lumber
            }
            map.RebuildClearance();
            map.SeedTerrainChecksum();
            return map;
        }

        // --- Serialization access ------------------------------------------------
        //
        // Only the planes that are authoritative. _clearance and _region are pure
        // functions of _passable and are rebuilt on load; _regionStack is scratch.
        // _shore looks static but MUST be saved: it gates IsBeachable, is set
        // only from the PUD's SQM, and is not derivable from _passable, so a
        // snapshot loaded without it would refuse every transport unload.
        internal byte[] PassablePlane => _passable;
        internal byte[] WoodPlane => _wood;
        internal byte[] ShorePlane => _shore;

        /// <summary>Rebuild a map from saved planes. Clearance and regions are
        /// recomputed rather than stored.</summary>
        internal static TerrainMap FromPlanes(int width, int height,
            byte[] passable, byte[] wood, byte[] shore)
        {
            var map = new TerrainMap(width, height);
            System.Array.Copy(passable, map._passable, passable.Length);
            System.Array.Copy(wood, map._wood, wood.Length);
            System.Array.Copy(shore, map._shore, shore.Length);
            map.RebuildClearance();
            map.SeedTerrainChecksum();
            return map;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsPassable(MoveDomain domain, int x, int y) =>
            InBounds(x, y) && (_passable[y * Width + x] & (1 << (int)domain)) != 0;

        public int Clearance(MoveDomain domain, int x, int y) =>
            InBounds(x, y) ? _clearance[(int)domain][y * Width + x] : 0;

        /// <summary>Connected-component label for this tile in the domain's
        /// walkability graph; 0 for impassable or out-of-bounds. Two tiles with
        /// the same non-zero label are mutually reachable by a size-1 unit.</summary>
        public int RegionOf(MoveDomain domain, int x, int y) =>
            InBounds(x, y) ? _region[(int)domain][y * Width + x] : 0;

        /// <summary>Can a size-1 unit of this domain travel between the two tiles?
        /// Mirrors the original's region-colour compare (path_chk_target_region):
        /// an O(1) reachability test that avoids running A* toward somewhere the
        /// unit can never stand.</summary>
        public bool SameRegion(MoveDomain domain, int ax, int ay, int bx, int by)
        {
            int ra = RegionOf(domain, ax, ay);
            return ra != 0 && ra == RegionOf(domain, bx, by);
        }

        public void SetPassable(MoveDomain domain, int x, int y, bool passable)
        {
            int i = y * Width + x;
            byte bit = (byte)(1 << (int)domain);
            _passable[i] = passable ? (byte)(_passable[i] | bit) : (byte)(_passable[i] & ~bit);
        }

        /// <summary>
        /// clearance[t] = 1 + min(right, below, diag) when passable, else 0 —
        /// computed bottom-right to top-left. O(tiles); rerun after terrain
        /// changes (tree chop / building) until incremental updates land.
        /// </summary>
        public void RebuildClearance()
        {
            for (int d = 0; d < DomainCount; d++)
            {
                byte[] c = _clearance[d];
                byte bit = (byte)(1 << d);
                for (int y = Height - 1; y >= 0; y--)
                {
                    for (int x = Width - 1; x >= 0; x--)
                    {
                        int i = y * Width + x;
                        if ((_passable[i] & bit) == 0)
                        {
                            c[i] = 0;
                            continue;
                        }
                        if (x == Width - 1 || y == Height - 1)
                        {
                            c[i] = 1;
                            continue;
                        }
                        int m = c[i + 1];
                        if (c[i + Width] < m) m = c[i + Width];
                        if (c[i + Width + 1] < m) m = c[i + Width + 1];
                        c[i] = (byte)(m >= 255 ? 255 : m + 1);
                    }
                }
            }
            RebuildRegions();
        }

        /// <summary>
        /// Label each domain's connected components of passable tiles. 4-connected
        /// on purpose: diagonal movement forbids corner-cutting, so two tiles that
        /// are only diagonally adjacent are reachable iff a shared orthogonal
        /// neighbour is open — which makes them 4-connected too. Runs alongside
        /// RebuildClearance (setup and tree-chop), O(tiles) per domain, zero
        /// allocation (preallocated stack). Terrain-only, exactly like the
        /// original's region map: buildings stay dynamic obstacles handled by the
        /// occupancy layer during the search.
        /// </summary>
        public void RebuildRegions()
        {
            for (int d = 0; d < DomainCount; d++)
            {
                int[] r = _region[d];
                System.Array.Clear(r, 0, r.Length);
                byte bit = (byte)(1 << d);
                int label = 0;
                for (int seed = 0; seed < r.Length; seed++)
                {
                    if ((_passable[seed] & bit) == 0 || r[seed] != 0)
                        continue;
                    label++;
                    int sp = 0;
                    _regionStack[sp++] = seed;
                    r[seed] = label;
                    while (sp > 0)
                    {
                        int cur = _regionStack[--sp];
                        int cx = cur % Width, cy = cur / Width;
                        if (cx > 0) Flood(r, bit, label, cur - 1, ref sp);
                        if (cx < Width - 1) Flood(r, bit, label, cur + 1, ref sp);
                        if (cy > 0) Flood(r, bit, label, cur - Width, ref sp);
                        if (cy < Height - 1) Flood(r, bit, label, cur + Width, ref sp);
                    }
                }
            }
        }

        void Flood(int[] r, byte bit, int label, int n, ref int sp)
        {
            if ((_passable[n] & bit) != 0 && r[n] == 0)
            {
                r[n] = label;
                _regionStack[sp++] = n;
            }
        }
    }
}
