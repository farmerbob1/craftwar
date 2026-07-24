using System.Collections.Generic;

namespace Craftwar.Sim.Ai.Spatial
{
    /// <summary>
    /// Connectivity-gated building placement — the replacement for the old
    /// AiBasePlan/AiSiteSearch scoring that let bases wall themselves in. Every
    /// candidate must pass, in order:
    ///   1. <see cref="AiSiteSearch.FindSiteAt"/> — the authoritative fit test
    ///      (BuildSite.Check terrain+occupancy, mine-lane keep-out).
    ///   2. <see cref="ReachabilityProbe"/> — the HARD invariant: the base's open
    ///      interior still reaches its mine, wood and a map exit once this footprint
    ///      is a wall. This is what makes self-boxing structurally impossible.
    /// Survivors are ranked by an occupancy-aware score (compact, breathable, off
    /// choke corridors, out of threat). Deterministic: fixed ring order, integer
    /// scores, first-of-equal-score wins. Instanced per AiPlayer (reuses the probe).
    /// </summary>
    public sealed class AiSitePlanner
    {
        /// <summary>Plots scored before we settle, to bound cost on large maps.</summary>
        public const int MaxCandidates = 96;

        /// <summary>Radius of the open-space window used to detect (and avoid)
        /// choke corridors when scoring.</summary>
        public const int OpennessRadius = 2;

        readonly ReachabilityProbe _probe = new ReachabilityProbe();
        readonly List<Rect> _friends = new List<Rect>();
        readonly List<Cand> _cands = new List<Cand>();

        struct Rect { public int X, Y, Size; }
        struct Cand { public int X, Y, Score, Seq; }

        public ReachabilityProbe Probe => _probe;

        /// <summary>
        /// Best connectivity-safe plot for <paramref name="buildType"/> near the
        /// anchor, or false if none. <paramref name="threat"/> may be null (no
        /// threat term). Call once per build order.
        /// </summary>
        public bool FindSite(GameState s, byte player, ushort buildType,
            int anchorX, int anchorY, int maxRadius, uint builderPacked,
            List<int> blacklist, InfluenceField threat, out int tileX, out int tileY)
        {
            int size = s.Footprint(buildType);
            tileX = 0;
            tileY = 0;

            // Gather cheap-valid candidates and score them WITHOUT the connectivity
            // flood, which is by far the most expensive step.
            CollectFriends(s, player);
            _cands.Clear();
            int seq = 0;
            for (int r = AiSiteSearch.MinRadius; r <= maxRadius && _cands.Count < MaxCandidates; r++)
            {
                int len = AiSiteSearch.RingLength(r);
                for (int i = 0; i < len && _cands.Count < MaxCandidates; i++)
                {
                    AiSiteSearch.RingTile(anchorX, anchorY, r, i, out int x, out int y);
                    if (blacklist != null && blacklist.Contains(y * s.Terrain.Width + x))
                        continue;
                    if (!AiSiteSearch.FindSiteAt(s, buildType, size, x, y, builderPacked))
                        continue;
                    _cands.Add(new Cand
                    {
                        X = x, Y = y, Score = Score(s, x, y, size, r, threat), Seq = seq++,
                    });
                }
            }
            if (_cands.Count == 0)
                return false;

            // Probe the gate best-first and take the first connectivity-safe plot —
            // usually the top candidate passes, so this runs one flood, not N.
            _cands.Sort(CandCmp);
            _probe.BeginDecision(s, player, anchorX, anchorY);
            for (int c = 0; c < _cands.Count; c++)
            {
                var cand = _cands[c];
                if (_probe.CandidateKeepsConnectivity(buildType, cand.X, cand.Y))
                {
                    tileX = cand.X;
                    tileY = cand.Y;
                    return true;
                }
            }
            return false;
        }

        // Highest score first; ties broken by insertion (ring) order for determinism.
        static readonly System.Comparison<Cand> CandCmp = (a, b) =>
            a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Seq.CompareTo(b.Seq);

        void CollectFriends(GameState s, byte player)
        {
            _friends.Clear();
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (u.IsAlive && u.Player == player && (u.Flags & UnitFlags.Building) != 0)
                    _friends.Add(new Rect { X = u.TileX, Y = u.TileY, Size = s.Footprint(u.TypeId) });
            }
        }

        /// <summary>
        /// Compact (hug friends — now safe, the gate forbids sealing), breathable
        /// (occupancy-aware open perimeter), off choke corridors (open-space
        /// window), out of threat, and close in (prefer small rings).
        /// </summary>
        int Score(GameState s, int x, int y, int size, int ring, InfluenceField threat)
        {
            int adj = AdjacentFriendlyTiles(x, y, size);
            int openP = OpenPerimeter(s, x, y, size);
            int openness = OpenSpaceWindow(s, x, y, size);
            int threatPenalty = threat == null
                ? 0
                : threat.SampleFootprintMax(x, y, size);
            return 4 * adj + 2 * openP + openness - ring - threatPenalty;
        }

        int AdjacentFriendlyTiles(int x0, int y0, int size)
        {
            int n = 0;
            for (int x = x0 - 1; x <= x0 + size; x++)
            {
                if (InFriend(x, y0 - 1)) n++;
                if (InFriend(x, y0 + size)) n++;
            }
            for (int y = y0; y < y0 + size; y++)
            {
                if (InFriend(x0 - 1, y)) n++;
                if (InFriend(x0 + size, y)) n++;
            }
            return n;
        }

        bool InFriend(int x, int y)
        {
            for (int f = 0; f < _friends.Count; f++)
            {
                var r = _friends[f];
                if (x >= r.X && x < r.X + r.Size && y >= r.Y && y < r.Y + r.Size)
                    return true;
            }
            return false;
        }

        /// <summary>Perimeter tiles that are BOTH land-passable AND unoccupied — the
        /// occupancy-aware fix. The old heuristic tested terrain only, so a plot
        /// ringed by buildings still read as "open"; this does not.</summary>
        static int OpenPerimeter(GameState s, int x0, int y0, int size)
        {
            int n = 0;
            for (int x = x0 - 1; x <= x0 + size; x++)
            {
                if (Open(s, x, y0 - 1)) n++;
                if (Open(s, x, y0 + size)) n++;
            }
            for (int y = y0; y < y0 + size; y++)
            {
                if (Open(s, x0 - 1, y)) n++;
                if (Open(s, x0 + size, y)) n++;
            }
            return n;
        }

        /// <summary>Count of open tiles in a window around the footprint — low means
        /// a narrow corridor, which we avoid plugging even when the gate allows it.</summary>
        static int OpenSpaceWindow(GameState s, int x0, int y0, int size)
        {
            int n = 0;
            int a = OpennessRadius;
            for (int y = y0 - a; y < y0 + size + a; y++)
                for (int x = x0 - a; x < x0 + size + a; x++)
                    if (Open(s, x, y)) n++;
            return n;
        }

        static bool Open(GameState s, int x, int y)
        {
            var t = s.Terrain;
            return t.InBounds(x, y)
                && t.IsPassable(MoveDomain.Land, x, y)
                && s.OccupancySurface[y * t.Width + x] == 0;
        }
    }
}
