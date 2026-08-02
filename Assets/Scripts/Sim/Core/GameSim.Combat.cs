namespace Craftwar.Sim
{
    public sealed partial class GameSim
    {
        // Scratch dedupe list for ApplySplashDamage: a multi-tile unit can
        // appear at several scanned tiles and must only be hit once, exactly
        // as BULLET.C's damage_area guards with already_hit/sgpHitUnits.
        // Sized well past any real blast footprint (7x7 tiles, each holding
        // at most one surface occupant). Fully rewritten every call, so —
        // like _pathScratch — it carries no state between calls and is never
        // hashed.
        readonly int[] _splashHitScratch = new int[64];
        int _splashHitCount;

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
                    else if (u.Order == OrderType.Patrol)
                    {
                        // Resume the leg in progress. Unlike attack-move,
                        // GoalX/Y is the far end of the beat, not a destination
                        // to restore — OrderX/Y is already correct.
                        u.PathLength = 0;
                        u.PathCursor = 0;
                    }
                }

                // Periodic auto-acquisition (idle, attack-moving or patrolling),
                // staggered by slot so the scan cost spreads across ticks.
                if (u.AttackTarget == 0
                    && (u.Order == OrderType.None || u.Order == OrderType.AttackMove
                        || u.Order == OrderType.Patrol)
                    && (State.Tick + i) % SimConstants.AcquisitionPeriod == 0)
                {
                    u.AttackTarget = FindTargetInRange(ref u, row.ReactRangeHuman);
                }

                if (u.AttackTarget == 0)
                    continue;

                State.TryGetUnitIndex(UnitId.FromPacked(u.AttackTarget), out int ti);
                ref Unit target = ref State.Units[ti];

                if (FootprintDistance(ref u, ref target) <= EffectiveRange(ref u))
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
                            Strike(ref u, i, ti, ref row);
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
                        // Walk at the NEAREST tile of the target's footprint,
                        // not its top-left corner. A 4x4 keep attacked from the
                        // south-east would otherwise send the attacker marching
                        // all the way round the building to reach the corner
                        // tile, only for TickCombat to cancel the path the
                        // moment it was already in range — the pacing.
                        NearestFootprintTile(ref u, ref target, out u.OrderX, out u.OrderY);
                        u.PathLength = 0;
                        u.PathCursor = 0;
                    }
                }
            }
        }

        static int Sign(int v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        /// <summary>
        /// The tile of <paramref name="target"/>'s footprint closest to
        /// <paramref name="u"/> — the tile an attacker should actually walk at.
        /// </summary>
        void NearestFootprintTile(ref Unit u, ref Unit target, out ushort x, out ushort y)
        {
            int size = State.Footprint(target.TypeId);
            int lo = target.TileX, hi = target.TileX + size - 1;
            x = (ushort)(u.TileX < lo ? lo : u.TileX > hi ? hi : u.TileX);
            lo = target.TileY;
            hi = target.TileY + size - 1;
            y = (ushort)(u.TileY < lo ? lo : u.TileY > hi ? hi : u.TileY);
        }

        /// <summary>
        /// True when the unit holds a live target that is already inside its
        /// weapon range. TickMovement runs BEFORE TickCombat, so without this
        /// gate an engaged unit spends every tick stepping toward the target's
        /// tile and having the step cancelled a moment later — the "units pace
        /// around while fighting" bug, and a standing drain on the per-tick
        /// pathfinding budget.
        /// </summary>
        bool EngagedInRange(ref Unit u)
        {
            if (u.AttackTarget == 0)
                return false;
            if (!State.TryGetUnitIndex(UnitId.FromPacked(u.AttackTarget), out int ti))
                return false;
            ref Unit target = ref State.Units[ti];
            return FootprintDistance(ref u, ref target) <= EffectiveRange(ref u);
        }

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
                        // You cannot shoot what your sonar has not found —
                        // and, since IsUnitDetected also gates Invisibility,
                        // this is the only place that check applies: an
                        // already-locked explicit Attack order keeps hitting
                        // its target even if it turns invisible mid-fight,
                        // since the chase/swing logic below never re-checks
                        // detection, only whether the target handle still
                        // resolves.
                        if (!IsUnitDetected(u.Player, ref other))
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
        /// half + rng(half + 1). Strength/pierce/armor are the
        /// upgrade-adjusted values.
        /// </summary>
        int RollDamage(ref Unit attacker, ref Unit defender)
        {
            int dmg = EffectiveStrength(ref attacker) - EffectiveArmor(ref defender);
            if (dmg < 0) dmg = 0;
            dmg += EffectivePierce(ref attacker);
            int half = (dmg + 1) / 2;
            return half + State.Rng.Next(half + 1);
        }

        void Strike(ref Unit attacker, int attackerIndex, int targetIndex, ref UnitTypeData row)
        {
            ref Unit target = ref State.Units[targetIndex];

            if (row.MissileWeapon == SimConstants.MissileNone)
            {
                ApplyDamage(targetIndex, RollDamage(ref attacker, ref target), attacker.Player);
                return;
            }

            int size = State.Footprint(attacker.TypeId);
            int startX = attacker.PixX + size * SimConstants.TilePixels / 2;
            int startY = attacker.PixY + size * SimConstants.TilePixels / 2;

            // Catapult/ballista/ship-cannon: a ground-targeted splash shot. It
            // commits to the target's CURRENT tile the instant it fires — a
            // fixed impact point, never re-aimed — and splashes whatever is
            // standing there (or nearby) when it lands, exactly like the
            // original's BULLET.C (it is not a homing missile despite sharing
            // the projectile pool with one).
            //
            // Gryphon Rider / Dragon get the same ground-targeted splash shot,
            // but BULLET.C hard-codes these two unit types (not a UDTA flag) to
            // keep drifting past the impact point afterward, re-splashing every
            // few ticks instead of stopping at one hit.
            bool chainFireball = attacker.TypeId == (ushort)UnitTypeId.GryphonRider
                || attacker.TypeId == (ushort)UnitTypeId.Dragon;
            bool splash = row.Is(UnitTypeFlags.CanGroundAttack) || chainFireball;
            int damage = splash
                ? EffectiveStrength(ref attacker) + EffectivePierce(ref attacker)
                : RollDamage(ref attacker, ref target);

            for (int p = 0; p < State.Projectiles.Length; p++)
            {
                if (State.Projectiles[p].Active)
                    continue;

                if (splash)
                {
                    int tsize = State.Footprint(target.TypeId);
                    int destX = target.PixX + tsize * SimConstants.TilePixels / 2
                        + State.Rng.Next(SimConstants.SplashDriftRange) - SimConstants.SplashDriftOffset;
                    int destY = target.PixY + tsize * SimConstants.TilePixels / 2
                        + State.Rng.Next(SimConstants.SplashDriftRange) - SimConstants.SplashDriftOffset;
                    State.Projectiles[p] = new Projectile
                    {
                        Active = true,
                        MissileType = row.MissileWeapon,
                        PixX = startX,
                        PixY = startY,
                        Splash = true,
                        DestPixX = destX,
                        DestPixY = destY,
                        SourceUnit = new UnitId((ushort)attackerIndex, attacker.Gen).Packed,
                        Damage = damage,
                        SourcePlayer = attacker.Player,
                        ChainPulsesRemaining = (ushort)(chainFireball ? SimConstants.FireballChainPulses : 0),
                        ChainStepX = (sbyte)Sign(destX - startX),
                        ChainStepY = (sbyte)Sign(destY - startY),
                    };
                }
                else
                {
                    State.Projectiles[p] = new Projectile
                    {
                        Active = true,
                        MissileType = row.MissileWeapon,
                        PixX = startX,
                        PixY = startY,
                        TargetUnit = new UnitId((ushort)targetIndex, target.Gen).Packed,
                        Damage = damage,
                        SourcePlayer = attacker.Player,
                    };
                }
                return;
            }

            // Pool exhausted: land the hit instantly rather than lose it.
            if (splash)
                ApplySplashDamage(target.PixX + State.Footprint(target.TypeId) * SimConstants.TilePixels / 2,
                    target.PixY + State.Footprint(target.TypeId) * SimConstants.TilePixels / 2,
                    damage, attacker.Player, new UnitId((ushort)attackerIndex, attacker.Gen).Packed);
            else
                ApplyDamage(targetIndex, damage, attacker.Player);
        }

        void ApplyDamage(int targetIndex, int damage, byte attacker)
        {
            ref Unit target = ref State.Units[targetIndex];
            // Unholy Armor: total immunity while it lasts (DAMAGE.C
            // damage_damage_unit: `if (pTarget->unitArmor || !bDamage) return;`).
            if (target.ArmorTicks > 0)
                return;
            target.Hp -= damage;
            NotifyUnderAttack(ref target, targetIndex);
            if (target.Hp > 0)
                return;

            CreditKill(attacker, target.Player, (target.Flags & UnitFlags.Building) != 0);
            var deadId = new UnitId((ushort)targetIndex, target.Gen);

            // Raise Dead's scan target (SPELL.C action_raisedead) — only a
            // fleshy unit leaves one, never a building, ship or existing
            // undead corpse.
            if ((target.Flags & UnitFlags.Building) == 0
                && State.Rules.Units[target.TypeId].Is(UnitTypeFlags.Organic)
                && !State.Rules.Units[target.TypeId].Is(UnitTypeFlags.Undead))
                State.RegisterCorpse(target.TileX, target.TileY);

            bool carriedTroops = target.CargoCount > 0;
            // A razed construction site must free the builder hidden inside it,
            // or the worker stays Hidden forever — invisible and unkillable.
            if ((target.Flags & UnitFlags.Building) != 0
                && (target.Flags & UnitFlags.UnderConstruction) != 0)
                ReleaseBuilder(ref target, targetIndex);
            State.DestroyUnit(deadId);
            if (carriedTroops)
                DrownCargo(deadId); // a sinking transport takes its hold with it
        }

        /// <summary>Tally a kill for the end-game score screen: a loss for the
        /// dead unit's owner, and a kill/razing for the attacker when it is an
        /// enemy (different team). Neutral owners (mines, critters) don't count.</summary>
        void CreditKill(byte attacker, byte victim, bool isBuilding)
        {
            if (victim < SimConstants.MaxPlayers)
            {
                if (isBuilding) State.Players[victim].BuildingsLost++;
                else State.Players[victim].UnitsLost++;
            }
            if (attacker < SimConstants.MaxPlayers && victim < SimConstants.MaxPlayers
                && State.Players[attacker].Team != State.Players[victim].Team)
            {
                if (isBuilding) State.Players[attacker].BuildingsRazed++;
                else State.Players[attacker].UnitsKilled++;
            }
        }

        /// <summary>
        /// One "under attack" line per player per window — every damage tick
        /// would otherwise flood the feed. Stores Tick+1 so a zero entry still
        /// means "never fired" on the very first tick. Emission only; nothing
        /// here is hashed or read back by the sim.
        /// </summary>
        void NotifyUnderAttack(ref Unit target, int targetIndex)
        {
            if (target.Player >= SimConstants.MaxPlayers)
                return;
            int last = State.LastUnderAttackTick[target.Player];
            if (last != 0 && State.Tick - (last - 1) < SimConstants.UnderAttackNotifyTicks)
                return;
            State.LastUnderAttackTick[target.Player] = State.Tick + 1;
            Emit(SimEventKind.UnderAttack, target.Player, 0, target.TypeId,
                new UnitId((ushort)targetIndex, target.Gen).Packed);
        }

        void TickProjectiles()
        {
            int speed = SimConstants.ProjectileSpeedPxPerTick;
            for (int p = 0; p < State.Projectiles.Length; p++)
            {
                ref Projectile proj = ref State.Projectiles[p];
                if (!proj.Active)
                    continue;

                if (proj.Splash)
                {
                    int sdx = proj.DestPixX - proj.PixX;
                    int sdy = proj.DestPixY - proj.PixY;
                    if (sdx >= -speed && sdx <= speed && sdy >= -speed && sdy <= speed)
                    {
                        ApplySplashDamage(proj.DestPixX, proj.DestPixY, proj.Damage,
                            proj.SourcePlayer, proj.SourceUnit);
                        if (proj.ChainPulsesRemaining > 0)
                        {
                            // Gryphon/dragon fireball: keep drifting past the
                            // impact point and splash again, rather than
                            // stopping at this one hit.
                            proj.ChainPulsesRemaining--;
                            proj.DestPixX += proj.ChainStepX * SimConstants.FireballChainStepPx;
                            proj.DestPixY += proj.ChainStepY * SimConstants.FireballChainStepPx;
                            continue;
                        }
                        proj.Active = false;
                        continue;
                    }
                    proj.PixX += sdx > speed ? speed : sdx < -speed ? -speed : sdx;
                    proj.PixY += sdy > speed ? speed : sdy < -speed ? -speed : sdy;
                    continue;
                }

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
                    ApplyDamage(ti, proj.Damage, proj.SourcePlayer);
                    continue;
                }
                proj.PixX += dx > speed ? speed : dx < -speed ? -speed : dx;
                proj.PixY += dy > speed ? speed : dy < -speed ? -speed : dy;
            }
        }

        /// <summary>
        /// A catapult/ballista/cannon impact: full damage to whatever is at
        /// the exact hit pixel, a quarter to anything further out but still in
        /// the blast, ground units only (BULLET.C damage_area/damage_area_unit
        /// — a square blast against max(dx^2, dy^2), not a circle). Each
        /// victim's own armor is applied here, individually, rather than to
        /// the pre-rolled damage the way a direct hit is — a splash shot has
        /// no single "defender" at launch time. The shooter never damages
        /// itself; everyone else, including the shooter's own side, can be
        /// caught in the blast exactly as in the original.
        /// </summary>
        void ApplySplashDamage(int hitPixX, int hitPixY, int rawDamage, byte sourcePlayer, uint sourceUnit)
        {
            var terrain = State.Terrain;
            if (terrain == null)
                return;

            int hitTileX = hitPixX / SimConstants.TilePixels;
            int hitTileY = hitPixY / SimConstants.TilePixels;
            _splashHitCount = 0;

            // A 7x7 window, matching BULLET.C's damage_area — wide enough that
            // a big building's footprint centre (its occupancy is nearest-tile
            // only) still falls inside the blast-radius check below even when
            // the nearest occupied tile is a couple of tiles off from impact.
            for (int ty = hitTileY - 3; ty <= hitTileY + 3; ty++)
            {
                if (ty < 0 || ty >= terrain.Height)
                    continue;
                for (int tx = hitTileX - 3; tx <= hitTileX + 3; tx++)
                {
                    if (tx < 0 || tx >= terrain.Width)
                        continue;
                    uint packed = State.OccupancySurface[ty * terrain.Width + tx];
                    if (packed == 0 || packed == sourceUnit)
                        continue;
                    if (!State.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                        continue;

                    bool seen = false;
                    for (int k = 0; k < _splashHitCount; k++)
                        if (_splashHitScratch[k] == idx) { seen = true; break; }
                    if (seen)
                        continue;
                    if (_splashHitCount < _splashHitScratch.Length)
                        _splashHitScratch[_splashHitCount++] = idx;

                    ref Unit victim = ref State.Units[idx];
                    int vsize = State.Footprint(victim.TypeId);
                    int vx = victim.PixX + vsize * SimConstants.TilePixels / 2 - hitPixX;
                    int vy = victim.PixY + vsize * SimConstants.TilePixels / 2 - hitPixY;
                    int distSq = Max(vx * vx, vy * vy);
                    if (distSq > SimConstants.SplashOuterRadiusSqPx)
                        continue;

                    int dmg = distSq > SimConstants.SplashFullRadiusSqPx ? rawDamage / 4 : rawDamage;
                    dmg -= EffectiveArmor(ref victim);
                    if (dmg <= 0)
                        continue;
                    int half = (dmg + 1) / 2;
                    ApplyDamage(idx, half + State.Rng.Next(half + 1), sourcePlayer);
                }
            }
        }
    }
}
