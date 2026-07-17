namespace Craftwar.Sim
{
    public enum UnitFlags : ushort
    {
        None = 0,
        Alive = 1 << 0,
        Building = 1 << 1,
    }

    /// <summary>
    /// One entity slot (units AND buildings, per the original model).
    /// Array-of-structs in GameState.Units; refer to slots via UnitId handles.
    /// Coordinates follow the original CELL.H model: a tile ("matrix") coord
    /// plus an absolute integer pixel coord (32 px per tile) used for
    /// intra-tile movement. Integer-only, always.
    /// </summary>
    public struct Unit
    {
        public ushort Gen;          // generation of this slot; 0 = never used
        public UnitFlags Flags;
        public ushort TypeId;       // index into the unit-type table (PUD unit type ids)
        public byte Player;         // 0-7, 15 = neutral
        public byte Facing;         // 0-7, N/NE/E/SE/S/SW/W/NW like the original

        public ushort TileX;
        public ushort TileY;
        public int PixX;            // absolute pixel coords (tile * 32 + offset)
        public int PixY;

        public int Hp;

        public bool IsAlive => (Flags & UnitFlags.Alive) != 0;

        public void HashInto(ref StateHash h)
        {
            h.Add(Gen);
            h.Add((ushort)Flags);
            h.Add(TypeId);
            h.Add(Player);
            h.Add(Facing);
            h.Add(TileX);
            h.Add(TileY);
            h.Add(PixX);
            h.Add(PixY);
            h.Add(Hp);
        }
    }
}
