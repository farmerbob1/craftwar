namespace Craftwar.Sim
{
    /// <summary>
    /// In-flight missile (sim entity, per the original's BULLET model).
    ///
    /// Two flavours, both a straight line to a point fixed at launch (BULLET.C
    /// <c>bullet_set_target</c>/<c>line_init_line</c> never re-aim mid-flight):
    ///  * Homing (<see cref="Splash"/> false): a single-target shot. It still
    ///    flies to <see cref="TargetUnit"/>'s launch-time position, but for
    ///    simplicity (and because it is not the reported defect) tracks that
    ///    unit's live position each tick rather than a frozen point — the
    ///    original itself commits to the unit's snapshot location and applies
    ///    <see cref="Damage"/> directly to it on arrival regardless of where it
    ///    has since moved. <see cref="Damage"/> is pre-rolled at launch.
    ///  * Ground/splash (<see cref="Splash"/> true) — catapults, ballistas,
    ///    ship cannons (any <c>UnitTypeFlags.CanGroundAttack</c> attacker):
    ///    flies to a fixed impact point (<see cref="DestPixX"/>/<see cref="DestPixY"/>,
    ///    the target's position plus drift at launch) and, on arrival, splashes
    ///    area damage there — it never retargets, so a target that dodges away
    ///    is missed. <see cref="Damage"/> holds the attacker's raw (pre-armor)
    ///    strength+pierce; each victim's own armor is applied at impact
    ///    (DAMAGE.C/BULLET.C <c>damage_area_unit</c>).
    /// </summary>
    public struct Projectile
    {
        public bool Active;
        public byte MissileType;   // UDTA missile id (view picks art by this)
        public int PixX;
        public int PixY;
        public bool Splash;        // true: ground-targeted splash (catapult/ballista/cannon)
        public uint TargetUnit;    // homing target, UnitId.Packed (Splash == false)
        public int DestPixX;       // fixed impact point (Splash == true)
        public int DestPixY;
        public uint SourceUnit;    // shooter, UnitId.Packed — excluded from its own splash
        public int Damage;         // homing: pre-rolled. splash: raw strength+pierce.
        public byte SourcePlayer;

        public void HashInto(ref StateHash h)
        {
            h.Add((byte)(Active ? 1 : 0));
            h.Add(MissileType);
            h.Add(PixX);
            h.Add(PixY);
            h.Add((byte)(Splash ? 1 : 0));
            h.Add(TargetUnit);
            h.Add(DestPixX);
            h.Add(DestPixY);
            h.Add(SourceUnit);
            h.Add(Damage);
            h.Add(SourcePlayer);
        }
    }
}
