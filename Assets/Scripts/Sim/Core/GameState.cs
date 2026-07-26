namespace Craftwar.Sim
{
    /// <summary>
    /// The complete simulation world. Everything that affects gameplay lives
    /// here (or is reachable from here), is integer-typed, and is covered by
    /// ComputeHash(). If it isn't hashed, it must not influence the sim.
    /// </summary>
    public sealed class GameState
    {
        public int Tick;
        public Pcg32 Rng;

        public readonly PlayerState[] Players = new PlayerState[SimConstants.MaxPlayers];

        public readonly Unit[] Units = new Unit[SimConstants.MaxUnits];
        public int HighestUnitIndex; // exclusive upper bound of ever-used slots

        // Free slots for recycling. Stack discipline: deterministic order.
        readonly ushort[] _freeList = new ushort[SimConstants.MaxUnits];
        int _freeCount;

        // Match-static references, set by GameSim.Setup.
        public RuleSet Rules;
        public TerrainMap Terrain;

        /// <summary>Mutable copy of the MTXM tile layer (trees fall, walls break).
        /// Private because every write has to go through <see cref="SetTile"/> to
        /// keep <see cref="TilesChecksum"/> exact — the hash reads the checksum,
        /// not the array, so an unfunnelled write would be invisible to desync
        /// detection. SimPurityTests bans direct indexed assignment elsewhere.</summary>
        ushort[] _tiles;

        public bool HasTiles => _tiles != null;
        public int TileCount => _tiles == null ? 0 : _tiles.Length;
        public ushort Tile(int index) => _tiles[index];
        public ushort TileAt(int x, int y) => _tiles[y * Terrain.Width + x];

        /// <summary>Install the match's tile layer and seed its checksum.</summary>
        public void InstallTiles(ushort[] tiles)
        {
            _tiles = tiles;
            TilesChecksum = 0;
            if (_tiles == null)
                return;
            unchecked
            {
                for (int i = 0; i < _tiles.Length; i++)
                    TilesChecksum += CellMix(i, _tiles[i]);
            }
        }

        /// <summary>The only way to mutate a tile. Keeps the running checksum
        /// exact by removing the old cell's contribution and adding the new one.</summary>
        public void SetTile(int index, ushort value)
        {
            ushort old = _tiles[index];
            if (old == value)
                return;
            _tiles[index] = value;
            unchecked { TilesChecksum += CellMix(index, value) - CellMix(index, old); }
        }

        /// <summary>Internal bulk read for the serializer. Never hand this out.</summary>
        internal ushort[] TileArray => _tiles;

        /// <summary>Tile mutations this tick, for the view to patch. Not hashed (derived from Tiles).</summary>
        public readonly System.Collections.Generic.List<(ushort x, ushort y, ushort tile)> TileChanges
            = new System.Collections.Generic.List<(ushort, ushort, ushort)>();

        /// <summary>Presentation events this tick. Not hashed (derived from state
        /// transitions); the sim writes but never reads them.</summary>
        public readonly System.Collections.Generic.List<SimEvent> Events
            = new System.Collections.Generic.List<SimEvent>();

        /// <summary>Last tick an UnderAttack event fired, per player, so the feed
        /// isn't spammed once per damage application. Not hashed: throttling only
        /// gates event emission, never sim logic.</summary>
        public readonly int[] LastUnderAttackTick = new int[SimConstants.MaxPlayers];

        /// <summary>Per-slot current path (packed tile indices); null when none.</summary>
        public readonly ushort[][] UnitPaths = new ushort[SimConstants.MaxUnits][];

        public readonly Projectile[] Projectiles = new Projectile[SimConstants.MaxProjectiles];

        // One unit per tile per layer, exactly like the original.
        // Values are UnitId.Packed (0 = free). Surface = land+sea, Air separate.
        public uint[] OccupancySurface;
        public uint[] OccupancyAir;

        /// <summary>Per-player sight this tick: [player][y*Width+x], 1 = in sight.
        /// Recomputed from scratch every tick by TickFog, so it is a pure
        /// function of unit positions — hashed anyway so a fog divergence shows
        /// up as a desync instead of a silent rendering difference.</summary>
        public byte[][] Visible;

        /// <summary>Per-player "has ever been seen": [player][y*Width+x].
        /// Accumulated state (never cleared), so it genuinely must be hashed.</summary>
        public byte[][] Explored;

        /// <summary>Per-player submarine detection this tick: [player][y*Width+x].
        /// Written only by units carrying UnitTypeFlags.SeesSubmarine. Submerged
        /// units are invisible and untargetable outside these discs — unlike the
        /// rest of fog, this one does gate gameplay.</summary>
        public byte[][] Detected;

        // --- Running checksums --------------------------------------------------
        //
        // ComputeHash used to walk every grid byte by byte: ~555 KB per call on a
        // 128x128 8-player map. That was free while only tests called it, but
        // lockstep desync detection runs it every command turn. These maintained
        // checksums replace the walks, and because each is updated at the single
        // place its grid is written, they cost essentially nothing per tick.
        //
        // Each combiner is either commutative-and-exact (a set that only grows) or
        // invertible (subtract the old cell's contribution, add the new one), so
        // the value is a pure function of grid contents, not of update order.
        // VerifyChecksums() proves that against a from-scratch recompute.

        public uint TilesChecksum { get; private set; }
        public readonly uint[] ExploredChecksum = new uint[SimConstants.MaxPlayers];
        public uint OccupancySurfaceChecksum { get; private set; }
        public uint OccupancyAirChecksum { get; private set; }

        /// <summary>
        /// Avalanche mix of (cell index, value). Must be injective enough that
        /// distinct grids do not sum to the same total: a naive multiply would
        /// let structured collisions cancel out, and a desync detector that
        /// cannot see a change is worse than none. The index is mixed in, so
        /// moving a value between cells still changes the sum.
        ///
        /// Zero contributes zero, which makes a freshly allocated grid's
        /// checksum 0 — so a running total that starts at 0 is correct without
        /// having to seed it with the empty grid's baseline, and the sparse
        /// grids (occupancy is almost all zeros) cost nothing to recompute.
        /// </summary>
        internal static uint CellMix(int index, uint value)
        {
            if (value == 0)
                return 0;
            unchecked
            {
                uint x = (uint)index * 0x9E3779B1u ^ Fmix(value + 0x165667B1u);
                return Fmix(x);
            }
        }

        static uint Fmix(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x85EBCA6Bu;
                x ^= x >> 13;
                x *= 0xC2B2AE35u;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>Record that a tile flipped unexplored -> explored. Called only
        /// on the transition; Reveal restamps the same tiles every tick, so an
        /// unguarded add would accumulate a term per tick and never match a
        /// recompute.</summary>
        public void StampExplored(int player, int tileIndex)
        {
            unchecked { ExploredChecksum[player] += CellMix(tileIndex, 1); }
        }

        void StampOccupancy(bool air, int cell, uint oldValue, uint newValue)
        {
            if (oldValue == newValue)
                return;
            unchecked
            {
                uint delta = CellMix(cell, newValue) - CellMix(cell, oldValue);
                if (air)
                    OccupancyAirChecksum += delta;
                else
                    OccupancySurfaceChecksum += delta;
            }
        }

        public GameState(ulong seed)
        {
            // Fixed stream selector: one RNG stream, seeded per match.
            Rng = new Pcg32(seed, 54);
        }

        public int Footprint(ushort typeId)
        {
            if (Rules == null)
                return 1;
            int s = Rules.Units[typeId].SizeW;
            return s < 1 ? 1 : s;
        }

        /// <summary>
        /// The movement domain a unit type paths on. UDTA MoveDomain is 0 land /
        /// 1 air / 2 naval; naval splits again because coast is "unpassable
        /// unless CLASS_WATER_CANDOCK" — tankers and transports dock, warships
        /// do not. This is the single source of truth; do not re-encode the
        /// 0/1/2 mapping at call sites.
        /// </summary>
        public MoveDomain DomainOf(ushort typeId)
        {
            if (Rules == null) return MoveDomain.Land;
            ref var row = ref Rules.Units[typeId];
            switch (row.MoveDomain)
            {
                case 1: return MoveDomain.Air;
                case 2:
                    return row.Is(UnitTypeFlags.Tanker | UnitTypeFlags.Transport)
                        ? MoveDomain.SeaDock
                        : MoveDomain.Sea;
                default: return MoveDomain.Land;
            }
        }

        uint[] OccupancyFor(ushort typeId) =>
            DomainOf(typeId) == MoveDomain.Air ? OccupancyAir : OccupancySurface;

        public void Occupy(UnitId id, ushort typeId, int tileX, int tileY)
        {
            if (Terrain == null) return;
            bool air = DomainOf(typeId) == MoveDomain.Air;
            var layer = air ? OccupancyAir : OccupancySurface;
            int size = Footprint(typeId);
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    if (tileX + dx < Terrain.Width && tileY + dy < Terrain.Height)
                    {
                        int i = (tileY + dy) * Terrain.Width + tileX + dx;
                        StampOccupancy(air, i, layer[i], id.Packed);
                        layer[i] = id.Packed;
                    }
        }

        public void Vacate(UnitId id, ushort typeId, int tileX, int tileY)
        {
            if (Terrain == null) return;
            bool air = DomainOf(typeId) == MoveDomain.Air;
            var layer = air ? OccupancyAir : OccupancySurface;
            int size = Footprint(typeId);
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    if (tileX + dx < Terrain.Width && tileY + dy < Terrain.Height)
                    {
                        int i = (tileY + dy) * Terrain.Width + tileX + dx;
                        if (layer[i] == id.Packed)
                        {
                            StampOccupancy(air, i, layer[i], 0);
                            layer[i] = 0;
                        }
                    }
        }

        /// <summary>
        /// Does this type obstruct movement? An unclaimed oil patch is flagged
        /// Building so it renders and can be targeted, but it is scenery
        /// floating on open water — ships sail straight over it, and only the
        /// platform later raised on top of it blocks. Keeping the patch out of
        /// the occupancy layer entirely (rather than special-casing every
        /// reader) also stops a passing ship's Occupy from overwriting the
        /// patch's entry, which Vacate would never restore.
        /// </summary>
        public bool BlocksMovement(ushort typeId) =>
            Rules == null || !Rules.Units[typeId].Is(UnitTypeFlags.OilPatch);

        /// <summary>Is the footprint at (tileX,tileY) free for this unit (ignoring itself)?</summary>
        public bool FootprintFree(UnitId self, ushort typeId, int tileX, int tileY)
        {
            if (Terrain == null) return true;
            var layer = OccupancyFor(typeId);
            int size = Footprint(typeId);
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                {
                    if (tileX + dx >= Terrain.Width || tileY + dy >= Terrain.Height)
                        return false;
                    uint occ = layer[(tileY + dy) * Terrain.Width + tileX + dx];
                    if (occ != 0 && occ != self.Packed)
                        return false;
                }
            return true;
        }

        public UnitId SpawnUnit(ushort typeId, byte player, ushort tileX, ushort tileY)
        {
            ushort index;
            if (_freeCount > 0)
            {
                index = _freeList[--_freeCount];
            }
            else
            {
                if (HighestUnitIndex >= Units.Length)
                    return UnitId.None; // out of slots
                index = (ushort)HighestUnitIndex++;
            }

            ref Unit u = ref Units[index];
            // Gen 0 means "never used"; first use starts at 1 so UnitId.None
            // (gen 0) can never alias a live unit.
            ushort gen = (ushort)(u.Gen + 1);
            if (gen == 0)
                gen = 1;
            u = new Unit
            {
                Gen = gen,
                Flags = UnitFlags.Alive,
                TypeId = typeId,
                Player = player,
                TileX = tileX,
                TileY = tileY,
                PixX = tileX * SimConstants.TilePixels,
                PixY = tileY * SimConstants.TilePixels,
            };
            var id = new UnitId(index, gen);
            if (!BlocksMovement(typeId))
                return id;
            Occupy(id, typeId, tileX, tileY);
            return id;
        }

        public void DestroyUnit(UnitId id)
        {
            if (!TryGetUnitIndex(id, out int index))
                return;
            ref Unit u = ref Units[index];
            // Mid-step units also hold their reserved destination tile.
            if (u.StepRemaining > 0)
                Vacate(id, u.TypeId, u.TileX + u.StepDX, u.TileY + u.StepDY);
            Vacate(id, u.TypeId, u.TileX, u.TileY);
            u.Flags &= ~UnitFlags.Alive;
            UnitPaths[index] = null;
            _freeList[_freeCount++] = (ushort)index;
        }

        public bool TryGetUnitIndex(UnitId id, out int index)
        {
            index = id.Index;
            return !id.IsNone
                && id.Index < HighestUnitIndex
                && Units[id.Index].Gen == id.Gen
                && Units[id.Index].IsAlive;
        }

        /// <summary>
        /// The desync fingerprint. Cheap enough to take every command turn: the
        /// per-entity walks stay (units are the state that actually diverges) but
        /// every full-grid walk is replaced by its running checksum.
        ///
        /// Visible and Detected are deliberately NOT hashed. TickFog clears both
        /// and rebuilds them from scratch each tick, so each is exactly
        /// F(hashed state at the end of this tick) — memoryless, carrying nothing
        /// across ticks. Combat does read Detected one tick stale, but a
        /// divergence there implies a divergence in its already-hashed inputs,
        /// which this hash catches on the earlier tick, before it can reach
        /// AttackTarget. Explored is different: it accumulates, so it is hashed
        /// (via its checksum).
        ///
        /// Occupancy IS hashed, which it never used to be. It gates pathing,
        /// target acquisition and build placement, and it is a function of write
        /// history rather than of current unit positions — so it can diverge on
        /// its own, and was the one real blind spot here.
        /// </summary>
        public uint ComputeHash()
        {
            var h = StateHash.Begin();
            h.Add(Tick);
            h.Add(Rng.State);
            h.Add(Rng.Inc);
            for (int i = 0; i < Players.Length; i++)
                Players[i].HashInto(ref h);
            h.Add(TilesChecksum);
            h.Add(OccupancySurfaceChecksum);
            h.Add(OccupancyAirChecksum);
            h.Add(Terrain == null ? 0u : Terrain.TerrainChecksum);
            h.Add(HighestUnitIndex);
            for (int i = 0; i < HighestUnitIndex; i++)
                Units[i].HashInto(ref h);
            for (int i = 0; i < Projectiles.Length; i++)
                if (Projectiles[i].Active)
                {
                    h.Add(i);
                    Projectiles[i].HashInto(ref h);
                }
            // Only in-game slots explore anything; the rest stay zero.
            for (int p = 0; p < Players.Length; p++)
                h.Add(ExploredChecksum[p]);
            return h.Value;
        }

        /// <summary>
        /// Recompute every maintained checksum from scratch and report the first
        /// mismatch, or null when they all agree. This is the guard that makes the
        /// incremental scheme trustworthy: if a future write site skips a funnel,
        /// the hash would silently stop noticing that grid, so tests assert this
        /// after long runs and loads rather than trusting the funnels by
        /// inspection. Test/diagnostic use — not called on the hot path.
        /// </summary>
        public string VerifyChecksums()
        {
            unchecked
            {
                uint tiles = 0;
                if (_tiles != null)
                    for (int i = 0; i < _tiles.Length; i++)
                        tiles += CellMix(i, _tiles[i]);
                if (tiles != TilesChecksum)
                    return $"TilesChecksum {TilesChecksum:X8} != recomputed {tiles:X8}";

                uint surf = 0, air = 0;
                if (OccupancySurface != null)
                    for (int i = 0; i < OccupancySurface.Length; i++)
                        surf += CellMix(i, OccupancySurface[i]);
                if (surf != OccupancySurfaceChecksum)
                    return $"OccupancySurfaceChecksum {OccupancySurfaceChecksum:X8} != recomputed {surf:X8}";
                if (OccupancyAir != null)
                    for (int i = 0; i < OccupancyAir.Length; i++)
                        air += CellMix(i, OccupancyAir[i]);
                if (air != OccupancyAirChecksum)
                    return $"OccupancyAirChecksum {OccupancyAirChecksum:X8} != recomputed {air:X8}";

                for (int p = 0; p < Players.Length; p++)
                {
                    uint exp = 0;
                    byte[] g = Explored?[p];
                    if (g != null)
                        for (int i = 0; i < g.Length; i++)
                            if (g[i] != 0)
                                exp += CellMix(i, 1);
                    if (exp != ExploredChecksum[p])
                        return $"ExploredChecksum[{p}] {ExploredChecksum[p]:X8} != recomputed {exp:X8}";
                }

                if (Terrain != null)
                {
                    string terrainError = Terrain.VerifyTerrainChecksum();
                    if (terrainError != null)
                        return terrainError;
                }
            }
            return null;
        }
    }
}
