namespace Craftwar.Sim.Ai.Spatial
{
    /// <summary>
    /// A tile-resolution integer influence map. Sources are stamped as discs with
    /// linear falloff; the accumulated field is sampled for spatial reasoning
    /// (threat, friendly presence, resource pull, site quality). Plain integers and
    /// a fixed scan order — bit-reproducible, so it is lockstep-safe and passes
    /// SimPurityTests. Reused (Ensure + Clear) rather than reallocated each think.
    /// </summary>
    public sealed class InfluenceField
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        int[] _v;

        public void Ensure(TerrainMap t)
        {
            int len = t.Width * t.Height;
            if (_v == null || _v.Length != len)
            {
                Width = t.Width;
                Height = t.Height;
                _v = new int[len];
            }
        }

        public void Clear() => System.Array.Clear(_v, 0, _v.Length);

        bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>
        /// Add a disc of influence: <paramref name="peak"/> at the centre, falling
        /// linearly to 0 at <paramref name="radius"/> tiles (Euclidean, integer).
        /// Values accumulate, so overlapping sources reinforce.
        /// </summary>
        public void AddDisc(int cx, int cy, int peak, int radius)
        {
            if (radius <= 0 || peak == 0)
            {
                if (InBounds(cx, cy)) _v[cy * Width + cx] += peak;
                return;
            }
            int r2 = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= Height) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= Width) continue;
                    int d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;
                    int d = AiMath.Isqrt(d2);
                    // Linear falloff, integer: peak * (radius - d) / radius.
                    _v[y * Width + x] += peak * (radius - d) / radius;
                }
            }
        }

        public int Sample(int x, int y) => InBounds(x, y) ? _v[y * Width + x] : 0;

        /// <summary>Highest field value over a size×size footprint anchored at the
        /// top-left tile — the worst-case (e.g. peak threat) a building/army would
        /// sit in.</summary>
        public int SampleFootprintMax(int tileX, int tileY, int size)
        {
            int best = 0;
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                {
                    int s = Sample(tileX + dx, tileY + dy);
                    if (s > best) best = s;
                }
            return best;
        }
    }
}
