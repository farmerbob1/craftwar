namespace Craftwar.Sim
{
    /// <summary>
    /// In-flight missile (sim entity, per the original's BULLET model).
    /// Homing: it chases its target and always connects unless the target
    /// died first. The damage roll is made at launch (same RNG stream either
    /// way; launch keeps impact processing branch-free).
    /// </summary>
    public struct Projectile
    {
        public bool Active;
        public byte MissileType;   // UDTA missile id (view picks art by this)
        public int PixX;
        public int PixY;
        public uint TargetUnit;    // UnitId.Packed
        public int Damage;         // pre-rolled, applied on impact
        public byte SourcePlayer;

        public void HashInto(ref StateHash h)
        {
            h.Add((byte)(Active ? 1 : 0));
            h.Add(MissileType);
            h.Add(PixX);
            h.Add(PixY);
            h.Add(TargetUnit);
            h.Add(Damage);
            h.Add(SourcePlayer);
        }
    }
}
