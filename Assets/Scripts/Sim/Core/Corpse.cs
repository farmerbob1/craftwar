namespace Craftwar.Sim
{
    /// <summary>
    /// A raisable corpse (SPELL.C action_raisedead scans DEAD_GUY units within
    /// its radius): registered when an Organic unit dies, consumed by Raise
    /// Dead, and otherwise just expires. Separate from the view's own Corpse
    /// (a purely cosmetic decay-and-fade animation) — this one is sim state,
    /// hashed and replay-deterministic, because Raise Dead's outcome depends
    /// on it.
    /// </summary>
    public struct Corpse
    {
        public bool Active;
        public ushort TileX;
        public ushort TileY;
        public int TicksRemaining;

        public void HashInto(ref StateHash h)
        {
            h.Add((byte)(Active ? 1 : 0));
            h.Add(TileX);
            h.Add(TileY);
            h.Add(TicksRemaining);
        }
    }
}
