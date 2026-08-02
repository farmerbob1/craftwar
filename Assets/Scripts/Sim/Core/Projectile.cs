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
    ///  * Chained fireball (<see cref="Splash"/> true, <see cref="ChainPulsesRemaining"/>
    ///    nonzero) — Gryphon Rider / Dragon: BULLET.C hard-codes these two unit
    ///    types (<c>O_DRAGON</c>/<c>H_GRIFFON</c>, <c>bullet_create_fireball</c>)
    ///    to keep drifting past the impact point after arrival, re-splashing
    ///    every few ticks instead of stopping at one hit — one attack lands as
    ///    a short trail of explosions rather than a single impact. <see
    ///    cref="ChainStepX"/>/<see cref="ChainStepY"/> hold the drift
    ///    direction.
    ///  * Chained area spell (<see cref="Splash"/> true, <see cref="ChainPulsesRemaining"/>
    ///    nonzero, <see cref="MissileType"/> one of the synthetic
    ///    <c>SimConstants.Effect*</c> ids) — Blizzard/Whirlwind/Death and
    ///    Decay (GameSim.Spells.cs's SpawnAreaBlast): repeat hits at a fixed
    ///    landing point (<see cref="DestPixX"/>/<see cref="DestPixY"/>, never
    ///    moves) rather than drifting like the fireball case, so <see
    ///    cref="ChainStepX"/>/<see cref="ChainStepY"/> stay 0. Blizzard is the
    ///    one exception within this case: each pulse re-launches <see
    ///    cref="PixX"/>/<see cref="PixY"/> from a fresh point northwest of the
    ///    landing point (see TickProjectiles) so the shard is visibly back in
    ///    flight for every hit, matching BULLET.C's blizzard_shards — Whirlwind
    ///    and Death and Decay instead sit at their landing point the whole time.
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
        public ushort ChainPulsesRemaining; // pulses left after this one (fireball/blizzard/whirlwind/rot)
        public sbyte ChainStepX;   // -1/0/1: direction the impact point drifts per pulse
        public sbyte ChainStepY;

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
            h.Add(ChainPulsesRemaining);
            h.Add((byte)ChainStepX);
            h.Add((byte)ChainStepY);
        }
    }
}
