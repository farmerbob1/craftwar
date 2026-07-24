using System.Collections.Generic;

namespace Craftwar.Sim.Ai.Spatial
{
    /// <summary>
    /// The hard spatial invariant: an occupancy-aware connectivity check that makes
    /// self-boxing structurally impossible. Before the AI commits any building, this
    /// verifies the base still connects to the resources and a map exit it can reach
    /// *today*, once the candidate footprint (and every existing building) is treated
    /// as a wall.
    ///
    /// Why it cannot reuse <see cref="TerrainMap.SameRegion"/>: the region map is
    /// terrain-only by design (buildings are dynamic obstacles handled by the
    /// occupancy layer), so it reports gold "reachable" even after a wall of
    /// buildings seals it off. This flood fills over tiles that are BOTH
    /// land-passable AND unoccupied, which is the graph movement actually uses.
    ///
    /// Deterministic and allocation-free on the hot path: a stamp-marked visited
    /// grid (no per-probe clear), a preallocated stack, fixed 4-neighbour order.
    /// Instanced and reused by one AiPlayer, so no cross-player state bleed.
    /// Pattern mirrors <see cref="TerrainMap.RebuildRegions"/>.
    /// </summary>
    public sealed class ReachabilityProbe
    {
        /// <summary>How far out to look for a wood tile when treating wood as a
        /// connectivity target. Beyond this we simply don't require wood.</summary>
        public const int WoodSearchRadius = 40;

        /// <summary>How far out from the anchor to look for the base's open
        /// interior when seeding the flood.</summary>
        public const int MaxSeedRing = 12;

        GameState _s;
        TerrainMap _t;
        int _w, _h;
        int[] _mark;      // last epoch each tile was visited
        int[] _stack;
        int _epoch;

        // Decision context (set by BeginDecision, reused across candidates).
        bool _hasMineTarget;    // at least one live gold mine exists on the map
        int _woodX, _woodY;     // nearest wood tile, valid iff _hasWood
        bool _hasWood;
        bool _baseMineReach, _baseWoodReach, _baseEdgeReach;
        bool _degenerate;   // base already has no open interior — never block
        readonly List<int> _seeds = new List<int>();

        /// <summary>Whether the map has a gold mine / wood the base could target,
        /// and whether the base currently reaches each (set by BeginDecision). The
        /// anti-boxing regression asserts these stay true.</summary>
        public bool HasMine => _hasMineTarget;
        public bool HasWood => _hasWood;
        public bool BaseMineReachable => _baseMineReach;
        public bool BaseWoodReachable => _baseWoodReach;
        public bool BaseEdgeReachable => _baseEdgeReach;

        void Ensure(GameState s)
        {
            _s = s;
            _t = s.Terrain;
            int len = _t.Width * _t.Height;
            if (_mark == null || _mark.Length != len)
            {
                _w = _t.Width;
                _h = _t.Height;
                _mark = new int[len];
                _stack = new int[len];
                _epoch = 0;
            }
        }

        /// <summary>
        /// Snapshot the base, its resource targets, and which of them are reachable
        /// right now. Call once per build decision, then probe each candidate with
        /// <see cref="CandidateKeepsConnectivity"/>.
        /// </summary>
        public void BeginDecision(GameState s, byte player, int anchorX, int anchorY)
        {
            Ensure(s);
            CollectSeeds(anchorX, anchorY);
            _degenerate = _seeds.Count == 0;

            _hasMineTarget = AiQueries.NearestGoldMine(s, anchorX, anchorY) >= 0;
            _hasWood = AiQueries.NearestWoodTile(s, anchorX, anchorY, WoodSearchRadius,
                out _woodX, out _woodY);

            // Baseline: what the base can reach with no new building. A target that
            // is already cut off is not this placement's fault, so we never require
            // it — only that we don't newly sever something.
            Flood(0, 0, 0, false, out bool edge);
            _baseEdgeReach = edge;
            _baseMineReach = _hasMineTarget && AnyMineReached();
            _baseWoodReach = _hasWood && TileApproachReached(_woodX, _woodY);
        }

        /// <summary>
        /// Would placing <paramref name="buildType"/> at (siteX,siteY) keep every
        /// currently-reachable target (nearest mine, nearest wood, a map exit)
        /// reachable? True = safe to build. Assumes BeginDecision was called for
        /// this decision.
        /// </summary>
        public bool CandidateKeepsConnectivity(ushort buildType, int siteX, int siteY)
        {
            if (_degenerate)
                return true; // no open base interior to protect — don't deadlock
            int size = _s.Footprint(buildType);
            Flood(siteX, siteY, size, true, out bool edge);

            if (_baseEdgeReach && !edge)
                return false;
            if (_baseMineReach && !AnyMineReached())
                return false;
            if (_baseWoodReach && !TileApproachReached(_woodX, _woodY))
                return false;
            return true;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Seed the flood from the base's open interior nearest the hall: the
        /// innermost ring around the anchor that has any open tile. Tying seeds to
        /// the anchor (not to every friendly building) is deliberate — it means
        /// "can the base, where the workers live, still reach its targets," so a
        /// wall of the AI's own buildings across the base genuinely reads as a
        /// disconnect even if the AI owns a building on the far side.
        /// </summary>
        void CollectSeeds(int anchorX, int anchorY)
        {
            _seeds.Clear();
            if (Open(anchorX, anchorY))
                _seeds.Add(anchorY * _w + anchorX);
            for (int r = 1; r <= MaxSeedRing && _seeds.Count == 0; r++)
            {
                int len = AiSiteSearch.RingLength(r);
                for (int i = 0; i < len; i++)
                {
                    AiSiteSearch.RingTile(anchorX, anchorY, r, i, out int x, out int y);
                    if (Open(x, y))
                        _seeds.Add(y * _w + x);
                }
            }
        }

        bool Open(int x, int y) =>
            _t.InBounds(x, y)
            && _t.IsPassable(MoveDomain.Land, x, y)
            && _s.OccupancySurface[y * _w + x] == 0;

        /// <summary>
        /// Flood the base interior over land-passable, unoccupied tiles, optionally
        /// treating a candidate footprint as a wall. Reports whether the flood
        /// touched a map-edge tile (an exit). Mine/wood reachability is post-checked
        /// against the marks from this flood.
        /// </summary>
        void Flood(int siteX, int siteY, int size, bool hasCandidate, out bool reachedEdge)
        {
            _epoch++;
            reachedEdge = false;
            int cx1 = siteX + size - 1, cy1 = siteY + size - 1;
            int sp = 0;

            for (int k = 0; k < _seeds.Count; k++)
            {
                int idx = _seeds[k];
                if (hasCandidate)
                {
                    int sx = idx % _w, sy = idx / _w;
                    if (sx >= siteX && sx <= cx1 && sy >= siteY && sy <= cy1)
                        continue; // seed sits under the candidate — not usable
                }
                if (_mark[idx] == _epoch)
                    continue;
                _mark[idx] = _epoch;
                _stack[sp++] = idx;
            }

            while (sp > 0)
            {
                int cur = _stack[--sp];
                int cx = cur % _w, cy = cur / _w;
                if (cx == 0 || cy == 0 || cx == _w - 1 || cy == _h - 1)
                    reachedEdge = true;
                // Fixed neighbour order: left, right, up, down.
                Visit(cx - 1, cy, siteX, siteY, cx1, cy1, hasCandidate, ref sp);
                Visit(cx + 1, cy, siteX, siteY, cx1, cy1, hasCandidate, ref sp);
                Visit(cx, cy - 1, siteX, siteY, cx1, cy1, hasCandidate, ref sp);
                Visit(cx, cy + 1, siteX, siteY, cx1, cy1, hasCandidate, ref sp);
            }
        }

        void Visit(int x, int y, int siteX, int siteY, int cx1, int cy1,
            bool hasCandidate, ref int sp)
        {
            if (!_t.InBounds(x, y))
                return;
            if (hasCandidate && x >= siteX && x <= cx1 && y >= siteY && y <= cy1)
                return; // candidate footprint is a wall
            int idx = y * _w + x;
            if (_mark[idx] == _epoch)
                return;
            if (!_t.IsPassable(MoveDomain.Land, x, y) || _s.OccupancySurface[idx] != 0)
                return;
            _mark[idx] = _epoch;
            _stack[sp++] = idx;
        }

        /// <summary>Did the last flood reach the harvest approach of ANY live gold
        /// mine? Checking every mine (not just the Euclidean-nearest) is what keeps
        /// the base connected to the gold economy it actually uses — on a real map
        /// the nearest mine by distance can be someone else's, across terrain, so a
        /// nearest-only test both under- and over-reports.</summary>
        bool AnyMineReached()
        {
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit m = ref _s.Units[i];
                if (!m.IsAlive || !_s.Rules.Units[m.TypeId].Is(UnitTypeFlags.GoldMine))
                    continue;
                int size = _s.Footprint(m.TypeId);
                for (int x = m.TileX; x < m.TileX + size; x++)
                {
                    if (Marked(x, m.TileY - 1)) return true;
                    if (Marked(x, m.TileY + size)) return true;
                }
                for (int y = m.TileY; y < m.TileY + size; y++)
                {
                    if (Marked(m.TileX - 1, y)) return true;
                    if (Marked(m.TileX + size, y)) return true;
                }
            }
            return false;
        }

        /// <summary>Did the last flood reach a tile 4-adjacent to (tx,ty) — e.g. a
        /// tree the chopper stands next to?</summary>
        bool TileApproachReached(int tx, int ty) =>
            Marked(tx - 1, ty) || Marked(tx + 1, ty)
            || Marked(tx, ty - 1) || Marked(tx, ty + 1);

        bool Marked(int x, int y) =>
            _t.InBounds(x, y) && _mark[y * _w + x] == _epoch;
    }
}
