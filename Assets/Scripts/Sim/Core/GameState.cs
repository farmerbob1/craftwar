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

        /// <summary>Mutable copy of the MTXM tile layer (trees fall, walls break).</summary>
        public ushort[] Tiles;

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
            var layer = OccupancyFor(typeId);
            int size = Footprint(typeId);
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    if (tileX + dx < Terrain.Width && tileY + dy < Terrain.Height)
                        layer[(tileY + dy) * Terrain.Width + tileX + dx] = id.Packed;
        }

        public void Vacate(UnitId id, ushort typeId, int tileX, int tileY)
        {
            if (Terrain == null) return;
            var layer = OccupancyFor(typeId);
            int size = Footprint(typeId);
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    if (tileX + dx < Terrain.Width && tileY + dy < Terrain.Height)
                    {
                        int i = (tileY + dy) * Terrain.Width + tileX + dx;
                        if (layer[i] == id.Packed)
                            layer[i] = 0;
                    }
        }

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

        static void HashGrid(ref StateHash h, byte[][] grids, int player)
        {
            if (grids == null)
                return;
            byte[] g = grids[player];
            if (g == null)
                return;
            for (int i = 0; i < g.Length; i++)
                h.Add(g[i]);
        }

        public uint ComputeHash()
        {
            var h = StateHash.Begin();
            h.Add(Tick);
            h.Add(Rng.State);
            h.Add(Rng.Inc);
            for (int i = 0; i < Players.Length; i++)
                Players[i].HashInto(ref h);
            if (Tiles != null)
                for (int i = 0; i < Tiles.Length; i++)
                    h.Add(Tiles[i]);
            h.Add(HighestUnitIndex);
            for (int i = 0; i < HighestUnitIndex; i++)
                Units[i].HashInto(ref h);
            for (int i = 0; i < Projectiles.Length; i++)
                if (Projectiles[i].Active)
                {
                    h.Add(i);
                    Projectiles[i].HashInto(ref h);
                }
            // Fog. Null before Setup (tests build a sim with no map at all), and
            // only in-game slots carry grids, so both guards are load-bearing.
            for (int p = 0; p < Players.Length; p++)
            {
                if (!Players[p].InGame)
                    continue;
                HashGrid(ref h, Visible, p);
                HashGrid(ref h, Explored, p);
                HashGrid(ref h, Detected, p);
            }
            return h.Value;
        }
    }
}
