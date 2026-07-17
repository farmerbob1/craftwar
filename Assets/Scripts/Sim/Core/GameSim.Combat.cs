namespace Craftwar.Sim
{
    public sealed partial class GameSim
    {
        void TickCombat()
        {
            if (State.Terrain == null)
                return;

            TickProjectiles();

            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive || (u.Flags & UnitFlags.Hidden) != 0)
                    continue;
                ref UnitTypeData row = ref State.Rules.Units[u.TypeId];
                if (!row.Is(UnitTypeFlags.CanAttack))
                    continue;

                if (u.Cooldown > 0)
                    u.Cooldown--;

                // Drop dead/stale targets.
                if (u.AttackTarget != 0 &&
                    !State.TryGetUnitIndex(UnitId.FromPacked(u.AttackTarget), out _))
                {
                    u.AttackTarget = 0;
                    if (u.Order == OrderType.Attack)
                    {
                        u.Order = OrderType.None;
                        u.PathLength = 0;
                    }
                    else if (u.Order == OrderType.AttackMove)
                    {
                        // Kill done: resume the original attack-move journey.
                        u.OrderX = u.GoalX;
                        u.OrderY = u.GoalY;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                    }
                }

                // Periodic auto-acquisition (idle or attack-moving), staggered
                // by slot so the scan cost spreads across ticks.
                if (u.AttackTarget == 0
                    && (u.Order == OrderType.None || u.Order == OrderType.AttackMove)
                    && (State.Tick + i) % SimConstants.AcquisitionPeriod == 0)
                {
                    u.AttackTarget = FindTargetInRange(ref u, row.ReactRangeHuman);
                }

                if (u.AttackTarget == 0)
                    continue;

                State.TryGetUnitIndex(UnitId.FromPacked(u.AttackTarget), out int ti);
                ref Unit target = ref State.Units[ti];

                if (FootprintDistance(ref u, ref target) <= row.AttackRange)
                {
                    // In range: hold position, face the enemy, swing on cooldown.
                    u.PathLength = 0;
                    u.PathCursor = 0;
                    if (u.StepRemaining == 0)
                    {
                        u.Facing = FacingFrom(
                            Sign(target.TileX - u.TileX), Sign(target.TileY - u.TileY));
                        if (u.Cooldown == 0)
                        {
                            Strike(ref u, ti, ref row);
                            u.Cooldown = (byte)SimConstants.AttackCooldownTicks;
                        }
                    }
                }
                else if (UnitSpeeds.Get(u.TypeId) > 0)
                {
                    // Chase: keep the movement system pointed at the target's
                    // current tile; repath only when it actually moved.
                    if (u.Order == OrderType.None)
                        u.Order = OrderType.Attack;
                    // Attack AND attack-move both chase the engaged target;
                    // attack-move resumes its Goal once the target dies.
                    if (u.ChaseX != target.TileX || u.ChaseY != target.TileY)
                    {
                        u.ChaseX = target.TileX;
                        u.ChaseY = target.TileY;
                        u.OrderX = target.TileX;
                        u.OrderY = target.TileY;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                    }
                }
            }
        }

        static int Sign(int v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        /// <summary>Chebyshev distance between unit footprints, in tiles.</summary>
        int FootprintDistance(ref Unit a, ref Unit b)
        {
            int sa = State.Footprint(a.TypeId);
            int sb = State.Footprint(b.TypeId);
            int dx = Max0(Max(b.TileX - (a.TileX + sa - 1), a.TileX - (b.TileX + sb - 1)));
            int dy = Max0(Max(b.TileY - (a.TileY + sa - 1), a.TileY - (b.TileY + sb - 1)));
            return dx > dy ? dx : dy;
        }

        static int Max(int a, int b) => a > b ? a : b;
        static int Max0(int v) => v < 0 ? 0 : v;

        bool CanTargetUnit(ref UnitTypeData attacker, ushort targetTypeId)
        {
            byte domain = State.Rules.Units[targetTypeId].MoveDomain;
            int bit = domain == 1 ? 4 : domain == 2 ? 2 : 1;
            return (attacker.CanTarget & bit) != 0;
        }

        /// <summary>
        /// Scan occupancy tiles in a square of `range` around the footprint.
        /// Nearest enemy wins; ties break on lowest unit slot (deterministic).
        /// </summary>
        uint FindTargetInRange(ref Unit u, int range)
        {
            ref UnitTypeData row = ref State.Rules.Units[u.TypeId];
            int size = State.Footprint(u.TypeId);
            int w = State.Terrain.Width, h = State.Terrain.Height;
            uint best = 0;
            int bestDist = int.MaxValue;
            int bestSlot = int.MaxValue;

            for (int layer = 0; layer < 2; layer++)
            {
                uint[] occ = layer == 0 ? State.OccupancySurface : State.OccupancyAir;
                for (int ty = u.TileY - range; ty < u.TileY + size + range; ty++)
                {
                    if (ty < 0 || ty >= h) continue;
                    for (int tx = u.TileX - range; tx < u.TileX + size + range; tx++)
                    {
                        if (tx < 0 || tx >= w) continue;
                        uint packed = occ[ty * w + tx];
                        if (packed == 0)
                            continue;
                        var id = UnitId.FromPacked(packed);
                        if (!State.TryGetUnitIndex(id, out int idx))
                            continue;
                        ref Unit other = ref State.Units[idx];
                        if (other.Player == u.Player || other.Player >= SimConstants.MaxPlayers)
                            continue; // own or neutral
                        if (!CanTargetUnit(ref row, other.TypeId))
                            continue;
                        int dist = FootprintDistance(ref u, ref other);
                        if (dist > range)
                            continue;
                        if (dist < bestDist || (dist == bestDist && idx < bestSlot))
                        {
                            best = packed;
                            bestDist = dist;
                            bestSlot = idx;
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The WC2 damage roll: armor subtracts from basic only, pierce is
        /// added after, and the final hit lands at 50-100%:
        /// half + rng(half + 1).
        /// </summary>
        int RollDamage(ref UnitTypeData attacker, ref UnitTypeData defender)
        {
            int dmg = attacker.BasicDamage - defender.Armor;
            if (dmg < 0) dmg = 0;
            dmg += attacker.PiercingDamage;
            int half = (dmg + 1) / 2;
            return half + State.Rng.Next(half + 1);
        }

        void Strike(ref Unit attacker, int targetIndex, ref UnitTypeData row)
        {
            ref Unit target = ref State.Units[targetIndex];
            ref UnitTypeData defRow = ref State.Rules.Units[target.TypeId];
            int damage = RollDamage(ref row, ref defRow);

            if (row.MissileWeapon == SimConstants.MissileNone)
            {
                ApplyDamage(targetIndex, damage);
                return;
            }

            // Ranged: launch a homing projectile carrying the rolled damage.
            for (int p = 0; p < State.Projectiles.Length; p++)
            {
                if (State.Projectiles[p].Active)
                    continue;
                int size = State.Footprint(attacker.TypeId);
                State.Projectiles[p] = new Projectile
                {
                    Active = true,
                    MissileType = row.MissileWeapon,
                    PixX = attacker.PixX + size * SimConstants.TilePixels / 2,
                    PixY = attacker.PixY + size * SimConstants.TilePixels / 2,
                    TargetUnit = new UnitId((ushort)targetIndex, target.Gen).Packed,
                    Damage = damage,
                    SourcePlayer = attacker.Player,
                };
                return;
            }
            // Pool exhausted: land the hit instantly rather than lose it.
            ApplyDamage(targetIndex, damage);
        }

        void ApplyDamage(int targetIndex, int damage)
        {
            ref Unit target = ref State.Units[targetIndex];
            target.Hp -= damage;
            if (target.Hp <= 0)
                State.DestroyUnit(new UnitId((ushort)targetIndex, target.Gen));
        }

        void TickProjectiles()
        {
            int speed = SimConstants.ProjectileSpeedPxPerTick;
            for (int p = 0; p < State.Projectiles.Length; p++)
            {
                ref Projectile proj = ref State.Projectiles[p];
                if (!proj.Active)
                    continue;

                if (!State.TryGetUnitIndex(UnitId.FromPacked(proj.TargetUnit), out int ti))
                {
                    proj.Active = false; // target died mid-flight
                    continue;
                }

                ref Unit target = ref State.Units[ti];
                int size = State.Footprint(target.TypeId);
                int tx = target.PixX + size * SimConstants.TilePixels / 2;
                int ty = target.PixY + size * SimConstants.TilePixels / 2;
                int dx = tx - proj.PixX;
                int dy = ty - proj.PixY;

                if (dx >= -speed && dx <= speed && dy >= -speed && dy <= speed)
                {
                    proj.Active = false;
                    ApplyDamage(ti, proj.Damage);
                    continue;
                }
                proj.PixX += dx > speed ? speed : dx < -speed ? -speed : dx;
                proj.PixY += dy > speed ? speed : dy < -speed ? -speed : dy;
            }
        }
    }
}
