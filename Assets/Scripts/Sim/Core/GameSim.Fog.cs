namespace Craftwar.Sim
{
    /// <summary>
    /// Fog of war: per-player sight and explored grids.
    ///
    /// The whole grid is recomputed from scratch every tick rather than being
    /// maintained incrementally with reference counts. A recompute is
    /// O(units x r^2) — a few thousand integer ops — and it cannot desync,
    /// whereas incremental counters would have to be adjusted correctly at
    /// every spawn, death, hide/unhide and mid-step tile swap, where a single
    /// missed adjustment is a desync rather than a graphical glitch. Revisit
    /// only if profiling says so.
    ///
    /// Fog is hashed state but nothing in the sim reads it at M6: it drives
    /// rendering only. Combat acquisition is deliberately untouched, so this
    /// system's position after TickCombat in the fixed order is harmless.
    /// Gating gameplay on sight would require moving it earlier and is a
    /// separate, balance-affecting decision.
    /// </summary>
    public sealed partial class GameSim
    {
        void TickFog()
        {
            if (State.Terrain == null || State.Visible == null)
                return;

            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                byte[] vis = State.Visible[p];
                if (vis != null)
                    System.Array.Clear(vis, 0, vis.Length);
            }

            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive)
                    continue;
                // Inside a mine, depot or construction site: grants no vision.
                if ((u.Flags & UnitFlags.Hidden) != 0)
                    continue;
                // Neutral (15) owns mines and oil patches; it has no fog.
                if (u.Player >= SimConstants.MaxPlayers)
                    continue;

                byte[] vis = State.Visible[u.Player];
                if (vis == null)
                    continue;

                Reveal(vis, State.Explored[u.Player], ref u, EffectiveSight(ref u));
            }
        }

        /// <summary>
        /// Stamp a sight disc around every tile of the unit's footprint. The
        /// tile coord is the top-left of an NxN square (see GameState.Occupy),
        /// so a 4x4 Town Hall must light from its whole area, not one corner.
        /// Squared distance only — SimPurityTests bans Math.Sqrt, and integer
        /// comparison is exactly what we want anyway.
        /// </summary>
        void Reveal(byte[] visible, byte[] explored, ref Unit u, int sight)
        {
            if (sight < 0)
                sight = 0;
            int w = State.Terrain.Width;
            int h = State.Terrain.Height;
            int size = State.Footprint(u.TypeId);
            int r2 = sight * sight;

            int minX = u.TileX - sight;
            int maxX = u.TileX + size - 1 + sight;
            int minY = u.TileY - sight;
            int maxY = u.TileY + size - 1 + sight;
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX >= w) maxX = w - 1;
            if (maxY >= h) maxY = h - 1;

            for (int y = minY; y <= maxY; y++)
            {
                // Distance to the nearest footprint tile on each axis; zero
                // while inside the footprint's span.
                int dy = y < u.TileY ? u.TileY - y
                    : y > u.TileY + size - 1 ? y - (u.TileY + size - 1) : 0;
                int row = y * w;
                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x < u.TileX ? u.TileX - x
                        : x > u.TileX + size - 1 ? x - (u.TileX + size - 1) : 0;
                    if (dx * dx + dy * dy > r2)
                        continue;
                    int t = row + x;
                    visible[t] = 1;
                    if (explored != null)
                        explored[t] = 1;
                }
            }
        }

        /// <summary>Is this tile currently in sight for the player? View-facing
        /// helper; out-of-range/unset players see nothing.</summary>
        public bool IsVisible(int player, int x, int y)
        {
            if (State.Visible == null || player < 0 || player >= SimConstants.MaxPlayers)
                return false;
            byte[] g = State.Visible[player];
            if (g == null || !State.Terrain.InBounds(x, y))
                return false;
            return g[y * State.Terrain.Width + x] != 0;
        }

        /// <summary>Has this tile ever been seen by the player?</summary>
        public bool IsExplored(int player, int x, int y)
        {
            if (State.Explored == null || player < 0 || player >= SimConstants.MaxPlayers)
                return false;
            byte[] g = State.Explored[player];
            if (g == null || !State.Terrain.InBounds(x, y))
                return false;
            return g[y * State.Terrain.Width + x] != 0;
        }

        /// <summary>Is any tile of this unit's footprint visible to the player?
        /// Buildings are large, so a corner peek reveals them, as in the
        /// original.</summary>
        public bool IsUnitVisible(int player, ref Unit u)
        {
            int size = State.Footprint(u.TypeId);
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    if (IsVisible(player, u.TileX + dx, u.TileY + dy))
                        return true;
            return false;
        }
    }
}
