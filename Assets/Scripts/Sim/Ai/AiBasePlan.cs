using System.Collections.Generic;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Smarter building placement for the higher tiers (PlannedLayout). Where the
    /// naive spiral (<see cref="AiSiteSearch"/>) takes the first valid tile — which
    /// over a long game crowds the base and can wall it in — this scores the valid
    /// candidates and picks a CLUSTERED but non-boxing plot: hug existing friendly
    /// buildings (compact base, short worker paths), keep the plot's own perimeter
    /// open, prefer smaller rings, and never brick in a neighbour.
    ///
    /// Pure and deterministic: fixed ring/scan order, integer scores, first-wins
    /// ties. Reuses <see cref="AiSiteSearch"/>'s validity (BuildSite.Check +
    /// mine-lane keep-out + ≥3 open perimeter), so a plot it accepts is always a
    /// plot the sim's arrival check accepts.
    /// </summary>
    public static class AiBasePlan
    {
        /// <summary>Cap on plots scored, to bound cost on large maps.</summary>
        public const int MaxCandidates = 64;

        /// <summary>A friendly building footprint, for clustering / anti-seal.</summary>
        struct Rect
        {
            public int X, Y, Size;
        }

        public static bool FindSite(GameState s, byte player, ushort buildType,
            int anchorX, int anchorY, int maxRadius, uint builderPacked,
            List<int> blacklist, out int tileX, out int tileY)
        {
            int size = s.Footprint(buildType);
            var friends = CollectFriendlyBuildings(s, player);

            int bestX = 0, bestY = 0, bestScore = int.MinValue, seen = 0;
            for (int r = AiSiteSearch.MinRadius; r <= maxRadius && seen < MaxCandidates; r++)
            {
                int len = AiSiteSearch.RingLength(r);
                for (int i = 0; i < len && seen < MaxCandidates; i++)
                {
                    AiSiteSearch.RingTile(anchorX, anchorY, r, i, out int x, out int y);
                    if (blacklist != null && blacklist.Contains(y * s.Terrain.Width + x))
                        continue;
                    if (!AiSiteSearch.FindSiteAt(s, buildType, size, x, y, builderPacked))
                        continue;
                    seen++;
                    var cand = new Rect { X = x, Y = y, Size = size };
                    if (SealsNeighbour(s, cand, friends))
                        continue; // never wall in an existing building
                    int score = Score(s, cand, friends, r);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            if (bestScore == int.MinValue)
            {
                tileX = 0;
                tileY = 0;
                return false;
            }
            tileX = bestX;
            tileY = bestY;
            return true;
        }

        static List<Rect> CollectFriendlyBuildings(GameState s, byte player)
        {
            var list = new List<Rect>();
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (u.IsAlive && u.Player == player && (u.Flags & UnitFlags.Building) != 0)
                    list.Add(new Rect { X = u.TileX, Y = u.TileY, Size = s.Footprint(u.TypeId) });
            }
            return list;
        }

        /// <summary>
        /// Cluster toward friends (biggest pull), keep the plot breathable, prefer
        /// small rings. Weights: adjacency dominates so the base stays compact, but
        /// the open-perimeter term and the seal rejection stop it from packing so
        /// tight it seals itself or a neighbour.
        /// </summary>
        static int Score(GameState s, in Rect cand, List<Rect> friends, int ring)
        {
            int adj = AdjacentFriendlyTiles(cand, friends);
            int open = OpenPerimeter(s, cand, default, useExclude: false);
            return 3 * adj + open - ring;
        }

        /// <summary>Perimeter tiles of the candidate that fall inside a friendly
        /// building — a proxy for how tucked-in (clustered) the plot is.</summary>
        static int AdjacentFriendlyTiles(in Rect cand, List<Rect> friends)
        {
            int n = 0;
            ForEachPerimeter(cand, (x, y) =>
            {
                for (int f = 0; f < friends.Count; f++)
                    if (Contains(friends[f], x, y))
                        return true;
                return false;
            }, ref n);
            return n;
        }

        /// <summary>Count of land-passable perimeter tiles; when
        /// <paramref name="useExclude"/>, tiles inside <paramref name="exclude"/>
        /// do not count (used to see how much room a neighbour keeps after a
        /// candidate is placed).</summary>
        static int OpenPerimeter(GameState s, in Rect rect, in Rect exclude, bool useExclude)
        {
            var t = s.Terrain;
            int n = 0;
            int open = 0;
            var e = exclude;
            bool ex = useExclude;
            ForEachPerimeter(rect, (x, y) =>
            {
                if (ex && Contains(e, x, y))
                    return false;
                return t.InBounds(x, y) && t.IsPassable(MoveDomain.Land, x, y);
            }, ref open);
            n = open;
            return n;
        }

        /// <summary>Would placing the candidate leave any adjacent friendly
        /// building with fewer than two open perimeter tiles — i.e. brick it in?</summary>
        static bool SealsNeighbour(GameState s, in Rect cand, List<Rect> friends)
        {
            for (int f = 0; f < friends.Count; f++)
            {
                var nb = friends[f];
                if (!WithinOneTile(cand, nb))
                    continue;
                int openAfter = OpenPerimeter(s, nb, cand, useExclude: true);
                if (openAfter < 2)
                    return true;
            }
            return false;
        }

        // --- geometry helpers (integer, deterministic) ---

        delegate bool TilePredicate(int x, int y);

        static void ForEachPerimeter(in Rect rect, TilePredicate hit, ref int count)
        {
            int x0 = rect.X - 1, x1 = rect.X + rect.Size;
            int y0 = rect.Y - 1, y1 = rect.Y + rect.Size;
            for (int x = x0; x <= x1; x++)
            {
                if (hit(x, y0)) count++;
                if (hit(x, y1)) count++;
            }
            for (int y = rect.Y; y < rect.Y + rect.Size; y++)
            {
                if (hit(x0, y)) count++;
                if (hit(x1, y)) count++;
            }
        }

        static bool Contains(in Rect r, int x, int y) =>
            x >= r.X && x < r.X + r.Size && y >= r.Y && y < r.Y + r.Size;

        /// <summary>Do the two footprints (grown by one tile) touch?</summary>
        static bool WithinOneTile(in Rect a, in Rect b) =>
            a.X - 1 <= b.X + b.Size && b.X - 1 <= a.X + a.Size
            && a.Y - 1 <= b.Y + b.Size && b.Y - 1 <= a.Y + a.Size;
    }
}
