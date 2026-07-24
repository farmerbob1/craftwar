namespace Craftwar.Sim
{
    /// <summary>
    /// Grid A*: 8-connectivity, costs 10 orthogonal / 14 diagonal, octile
    /// heuristic. Clearance-aware (multi-tile units) with no corner cutting.
    /// If the goal is unreachable (or the node budget runs out) the result is
    /// a partial path to the explored node closest to the goal — the fix for
    /// the classic "unit refuses to move" failure.
    /// All buffers are preallocated and generation-stamped; zero allocation
    /// per search except the caller-visible path copy.
    /// </summary>
    public sealed class Pathfinder
    {
        // Cap on nodes expanded per search. The original never ran a whole-map
        // A* (traverse.c bounded every search to MAX_STEPS=50); on a 128x128 map
        // 8192 was half the grid, so one obstructed/unreachable search cost ~2 ms
        // and late-game repath storms saturated the 20 ms tick budget. 2048 is
        // ~9% of a big map — ample for reachable goals (weighted A* expands far
        // fewer than this for open terrain) — and the closest-node partial-path
        // fallback plus the caller's escalating recovery re-plan from the new
        // spot, so a shorter partial in a pathological pocket is harmless.
        const int MaxExpandedNodes = 2048;

        // Greedy weight on the heuristic (f = g + h*3/2). Trades path optimality
        // for a much smaller explored frontier — the standard RTS choice, and
        // invisible in play. Integer-only for determinism.
        const int HeuristicWeightNum = 3;
        const int HeuristicWeightDen = 2;

        readonly TerrainMap _map;
        readonly GameState _state; // optional: occupancy-aware planning
        readonly int _w, _h;
        uint _self;
        bool _strict;

        readonly int[] _gScore;
        readonly int[] _parent;
        readonly int[] _stamp;       // generation stamp per tile
        readonly byte[] _closed;
        readonly int[] _heap;        // binary min-heap of tile indices
        readonly int[] _fScore;
        int _heapCount;
        int _generation;

        readonly ushort[] _pathBuffer;

        static readonly int[] Dx = { 0, 1, 1, 1, 0, -1, -1, -1 };
        static readonly int[] Dy = { -1, -1, 0, 1, 1, 1, 0, -1 };

        public Pathfinder(TerrainMap map, GameState state = null)
        {
            _map = map;
            _state = state;
            _w = map.Width;
            _h = map.Height;
            int n = _w * _h;
            _gScore = new int[n];
            _parent = new int[n];
            _stamp = new int[n];
            _closed = new byte[n];
            _heap = new int[n];
            _fScore = new int[n];
            _pathBuffer = new ushort[n];
        }

        int Heuristic(int x, int y, int tx, int ty)
        {
            int dx = x > tx ? x - tx : tx - x;
            int dy = y > ty ? y - ty : ty - y;
            // Octile: 14 per diagonal step, 10 per straight remainder.
            return dx > dy ? 14 * dy + 10 * (dx - dy) : 14 * dx + 10 * (dy - dx);
        }

        // Weighted heuristic for the priority key. bestNode tracking still uses
        // the raw Heuristic so the closest-node fallback is unaffected.
        int WeightedH(int x, int y, int tx, int ty) =>
            Heuristic(x, y, tx, ty) * HeuristicWeightNum / HeuristicWeightDen;

        bool Enterable(MoveDomain domain, int size, int x, int y)
        {
            if (_map.Clearance(domain, x, y) < size)
                return false;
            // Idle units and buildings are obstacles; moving units are
            // transparent (they will clear the tile, or we repath on contact).
            if (_state?.OccupancySurface != null)
            {
                var layer = domain == MoveDomain.Air ? _state.OccupancyAir : _state.OccupancySurface;
                uint occ = layer[y * _w + x];
                if (occ != 0 && occ != _self && IsStaticBlocker(occ))
                    return false;
            }
            return true;
        }

        bool IsStaticBlocker(uint packed)
        {
            if (!_state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                return false;
            Unit u = _state.Units[idx];
            if ((u.Flags & UnitFlags.Building) != 0)
                return true;
            if (u.StepRemaining != 0)
                return false; // mid-step units always clear their tiles
            // Strict mode (livelock escape): anything standing still is a wall.
            return _strict || u.Order == OrderType.None;
        }

        /// <summary>
        /// Find a path from (sx,sy) to (tx,ty). Writes packed tile indices
        /// (y*width+x) into path, start-exclusive. Returns step count; 0 means
        /// already there or nowhere to go. selfPacked excludes the moving unit
        /// itself from occupancy blocking.
        /// </summary>
        public int FindPath(MoveDomain domain, int size, int sx, int sy, int tx, int ty, ushort[] path,
            uint selfPacked = 0, bool strictBlockers = false)
        {
            if (sx == tx && sy == ty)
                return 0;
            _self = selfPacked;
            _strict = strictBlockers;

            _generation++;
            _heapCount = 0;

            int start = sy * _w + sx;
            Touch(start);
            _gScore[start] = 0;
            _fScore[start] = WeightedH(sx, sy, tx, ty);
            HeapPush(start);

            int bestNode = start;
            int bestH = _fScore[start];
            int expanded = 0;
            int goal = ty * _w + tx;
            bool found = false;

            while (_heapCount > 0 && expanded < MaxExpandedNodes)
            {
                int current = HeapPop();
                if (current == goal)
                {
                    bestNode = current;
                    found = true;
                    break;
                }
                if (_closed[current] != 0 && _stamp[current] == _generation)
                    continue;
                _closed[current] = 1;
                expanded++;

                int cx = current % _w;
                int cy = current / _w;

                int h = Heuristic(cx, cy, tx, ty);
                if (h < bestH)
                {
                    bestH = h;
                    bestNode = current;
                }

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d];
                    int ny = cy + Dy[d];
                    if (!Enterable(domain, size, nx, ny))
                        continue;
                    bool diagonal = (d & 1) != 0;
                    if (diagonal)
                    {
                        // No corner cutting: both orthogonal steps must be open.
                        if (!Enterable(domain, size, cx + Dx[d], cy) ||
                            !Enterable(domain, size, cx, cy + Dy[d]))
                            continue;
                    }

                    int neighbor = ny * _w + nx;
                    if (_stamp[neighbor] == _generation && _closed[neighbor] != 0)
                        continue;

                    int g = _gScore[current] + (diagonal ? 14 : 10);
                    if (_stamp[neighbor] == _generation && g >= _gScore[neighbor])
                        continue;

                    Touch(neighbor);
                    _gScore[neighbor] = g;
                    _fScore[neighbor] = g + WeightedH(nx, ny, tx, ty);
                    _parent[neighbor] = current;
                    HeapPush(neighbor);
                }
            }

            if (!found && bestNode == start)
                return 0;

            // Reconstruct start-exclusive path into the shared buffer, then
            // reverse-copy into the caller's array.
            int len = 0;
            for (int node = bestNode; node != start; node = _parent[node])
                _pathBuffer[len++] = (ushort)node;
            int steps = len > path.Length ? path.Length : len;
            for (int i = 0; i < steps; i++)
                path[i] = _pathBuffer[len - 1 - i];
            return steps;
        }

        void Touch(int node)
        {
            if (_stamp[node] != _generation)
            {
                _stamp[node] = _generation;
                _closed[node] = 0;
                _gScore[node] = int.MaxValue;
                _parent[node] = node;
            }
        }

        void HeapPush(int node)
        {
            int i = _heapCount++;
            _heap[i] = node;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_fScore[_heap[p]] <= _fScore[_heap[i]])
                    break;
                (_heap[p], _heap[i]) = (_heap[i], _heap[p]);
                i = p;
            }
        }

        int HeapPop()
        {
            int top = _heap[0];
            _heap[0] = _heap[--_heapCount];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1;
                int r = l + 1;
                int smallest = i;
                if (l < _heapCount && _fScore[_heap[l]] < _fScore[_heap[smallest]]) smallest = l;
                if (r < _heapCount && _fScore[_heap[r]] < _fScore[_heap[smallest]]) smallest = r;
                if (smallest == i)
                    break;
                (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                i = smallest;
            }
            return top;
        }
    }
}
