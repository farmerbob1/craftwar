namespace Craftwar.Sim
{
    /// <summary>
    /// One armed Runes trap (SPELL.C place_a_rune/update_runes): sits
    /// invisible-mostly at a fixed tile for up to RuneTrapLifeTicks, and
    /// detonates the instant any ground unit — friend, foe, or critter, the
    /// original checks no ownership at all — steps onto its exact tile.
    /// Global to the match, not owned by a player, exactly like the
    /// original's shared 50-slot rune table.
    /// </summary>
    public struct RuneTrap
    {
        public bool Active;
        public ushort TileX;
        public ushort TileY;
        public int TicksRemaining;
        /// <summary>Whoever cast it — attribution for the detonation's
        /// damage credit and effect colour only, not a trigger filter.</summary>
        public byte OwnerPlayer;

        public void HashInto(ref StateHash h)
        {
            h.Add((byte)(Active ? 1 : 0));
            h.Add(TileX);
            h.Add(TileY);
            h.Add(TicksRemaining);
            h.Add(OwnerPlayer);
        }
    }
}
