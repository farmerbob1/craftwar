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

        public GameState(ulong seed)
        {
            // Fixed stream selector: one RNG stream, seeded per match.
            Rng = new Pcg32(seed, 54);
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
            return new UnitId(index, gen);
        }

        public void DestroyUnit(UnitId id)
        {
            if (!TryGetUnitIndex(id, out int index))
                return;
            Units[index].Flags &= ~UnitFlags.Alive;
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

        public uint ComputeHash()
        {
            var h = StateHash.Begin();
            h.Add(Tick);
            h.Add(Rng.State);
            h.Add(Rng.Inc);
            for (int i = 0; i < Players.Length; i++)
                Players[i].HashInto(ref h);
            h.Add(HighestUnitIndex);
            for (int i = 0; i < HighestUnitIndex; i++)
                Units[i].HashInto(ref h);
            return h.Value;
        }
    }
}
