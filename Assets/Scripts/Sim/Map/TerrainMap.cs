using Craftwar.Sim.Pud;

namespace Craftwar.Sim
{
    public enum MoveDomain : byte
    {
        Land = 0,
        Air = 1,
        Sea = 2,
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

        readonly byte[] _passable;           // bit per MoveDomain
        readonly byte[][] _clearance;        // [domain][tile]
        readonly byte[] _wood;               // remaining wood units/100 per tile (1 = 100 lumber)

        public TerrainMap(int width, int height)
        {
            Width = width;
            Height = height;
            _passable = new byte[width * height];
            _clearance = new byte[3][];
            for (int d = 0; d < 3; d++)
                _clearance[d] = new byte[width * height];
            _wood = new byte[width * height];
        }

        /// <summary>Forest per MTXM classification: solid 0x007x or boundary 0x07xx.</summary>
        public static bool IsForestTile(ushort tileId) =>
            (tileId >> 8) == 0x07 || ((tileId >> 8) == 0x00 && ((tileId >> 4) & 0xF) == 0x7);

        public bool HasWood(int x, int y) => InBounds(x, y) && _wood[y * Width + x] > 0;

        /// <summary>Fell the tree at (x,y): frees the tile for land movement.</summary>
        public void Chop(int x, int y)
        {
            _wood[y * Width + x] = 0;
            SetPassable(MoveDomain.Land, x, y, true);
            RebuildClearance();
        }

        public static TerrainMap FromPud(PudFile pud)
        {
            var map = new TerrainMap(pud.Width, pud.Height);
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                ushort sqm = pud.MoveMap[i];
                byte bits = 1 << (int)MoveDomain.Air; // air passes everything
                switch (sqm)
                {
                    case 0x0000: // bridge
                    case 0x0001: // land
                    case 0x0002: // coast corner
                    case 0x0011: // dirt
                    case 0x0082: // coast
                        bits |= 1 << (int)MoveDomain.Land;
                        break;
                    case 0x0040: // water
                        bits |= 1 << (int)MoveDomain.Sea;
                        break;
                    // 0x0081 forest/mountains, 0x008d wall, unknown: blocked
                }
                map._passable[i] = bits;
                if (sqm == 0x0081 && IsForestTile(pud.Tiles[i]))
                    map._wood[i] = 1; // one tree = 100 lumber
            }
            map.RebuildClearance();
            return map;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsPassable(MoveDomain domain, int x, int y) =>
            InBounds(x, y) && (_passable[y * Width + x] & (1 << (int)domain)) != 0;

        public int Clearance(MoveDomain domain, int x, int y) =>
            InBounds(x, y) ? _clearance[(int)domain][y * Width + x] : 0;

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
            for (int d = 0; d < 3; d++)
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
        }
    }
}
