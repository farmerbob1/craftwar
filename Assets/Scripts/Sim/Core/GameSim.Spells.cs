namespace Craftwar.Sim
{
    /// <summary>
    /// Spellcasting: mana/status ticking (UNIT.C update_spells), corpse decay,
    /// the Cast command (arm-then-walk-then-fire, DSPTBL.C gbRangedOrderTbl /
    /// SPELL.C dispatch_spell_*), and every Church/Altar/Mage-Tower/Temple
    /// spell (SPELL.C action_*). Fireball, Death Coil, Eye of Kilrogg and Holy
    /// Vision are the remaining gap — the research side already exists
    /// (<see cref="PlayerState.Researched"/>), this file is only what's
    /// needed to actually cast something.
    ///
    /// Simplifications versus BULLET.C/SPELL.C, deliberate:
    ///  * Heal/Bloodlust/Slow/Haste/Invisibility/Flame Shield/Unholy Armor
    ///    require the target to be the caster's own unit (the original
    ///    enforces this at the click-to-target cursor, not in the action
    ///    routine itself); Polymorph requires an enemy target. Exorcism needs
    ///    no such simplification — it's ground-targeted and area-effect in
    ///    the original too (see CastExorcism), so there's no single "the
    ///    target" to restrict.
    ///  * Blizzard/Death and Decay reuse the exact chain-pulse mechanic
    ///    BULLET.C's disp_bullet_blizzard/rot use (a fixed number of repeat
    ///    hits, exact damage and hit counts, exact scatter for Blizzard/Death
    ///    and Decay's 5 independent landing points); Death and Decay and
    ///    Whirlwind land all their hits stationary, but Blizzard's shards
    ///    genuinely fly in from the northwest each hit, per blizzard_shards
    ///    (see SpawnAreaBlast/TickProjectiles). Whirlwind's original typhoon
    ///    also wanders slowly around its cast point for its whole 800-tick
    ///    life (disp_bullet_typhoon) — the wandering itself isn't reproduced.
    ///  * Flame Shield: exhaustively grepped for `unitFire` (its status flag)
    ///    across the whole available source — it is only ever a "don't
    ///    recast" / "can't target a flyer" guard. No damage-reflection or any
    ///    other combat effect exists for it anywhere in that source, contrary
    ///    to common Warcraft-lore belief, so none is implemented here either.
    /// </summary>
    public sealed partial class GameSim
    {
        /// <summary>Mana regen, buff/debuff decay (UNIT.C update_spells), and
        /// corpse expiry — called once per tick alongside the other systems.</summary>
        void TickSpells()
        {
            if (State.Rules != null)
            {
                for (int i = 0; i < State.HighestUnitIndex; i++)
                {
                    ref Unit u = ref State.Units[i];
                    if (!u.IsAlive)
                        continue;

                    if (u.RageTicks > 0)
                        u.RageTicks--;
                    if (u.WarpTicks > 0) u.WarpTicks--;
                    else if (u.WarpTicks < 0) u.WarpTicks++;
                    if (u.InvisTicks > 0)
                        u.InvisTicks--;
                    if (u.FireShieldTicks > 0)
                        u.FireShieldTicks--;
                    if (u.ArmorTicks > 0)
                        u.ArmorTicks--;

                    if (u.Mana >= SimConstants.MaxMana)
                        continue;
                    if (!State.Rules.Units[u.TypeId].Is(UnitTypeFlags.CanCast))
                        continue;
                    // Staggered by slot, like Berserker regen: spreads the scan
                    // cost across ticks instead of touching every caster at once.
                    if ((State.Tick + i) % SimConstants.ManaRegenPeriodTicks == 0)
                        u.Mana++;
                }
            }

            for (int i = 0; i < State.Corpses.Length; i++)
            {
                ref Corpse c = ref State.Corpses[i];
                if (!c.Active)
                    continue;
                if (--c.TicksRemaining <= 0)
                    c.Active = false;
            }
        }

        /// <summary>SPELL.C update_runes: each armed trap flickers back into
        /// view every RuneFlickerPeriodTicks (8 times across its life, exactly
        /// `(gwRuneDelay[i] &amp; RUNE_DELAY) == 1`'s cadence) and expires after
        /// RuneTrapLifeTicks, or detonates the instant a ground unit — any
        /// player's — stands on its exact tile. ApplyDamage's own Unholy
        /// Armor immunity check covers "unarmored units aren't damaged but
        /// still trigger it" for free; no separate check needed here.</summary>
        void TickRunes()
        {
            if (State.Terrain == null)
                return;
            int w = State.Terrain.Width;
            for (int i = 0; i < State.RuneTraps.Length; i++)
            {
                ref RuneTrap r = ref State.RuneTraps[i];
                if (!r.Active)
                    continue;
                if (--r.TicksRemaining <= 0)
                {
                    r.Active = false;
                    continue;
                }

                int px = r.TileX * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                int py = r.TileY * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                if (r.TicksRemaining % SimConstants.RuneFlickerPeriodTicks == 1)
                    SpawnEffect(px, py, SimConstants.EffectRune, r.OwnerPlayer);

                uint occ = State.OccupancySurface[r.TileY * w + r.TileX];
                if (occ == 0 || !State.TryGetUnitIndex(UnitId.FromPacked(occ), out int idx))
                    continue;

                r.Active = false;
                SpawnEffect(px, py, SimConstants.EffectBoom, r.OwnerPlayer);
                Emit(SimEventKind.RuneTriggered, r.OwnerPlayer, r.TileX, r.TileY);
                ApplyDamage(idx, SimConstants.RuneTrapDamage, r.OwnerPlayer);
            }
        }

        /// <summary>SPELL.C place_a_rune: fails (returns false) off-map, on a
        /// tile a unit is standing on right now, or on a tile another active
        /// trap already occupies — the pool is shared match-wide, not per
        /// player.</summary>
        bool PlaceRune(int x, int y, byte owner)
        {
            if (State.Terrain == null || !State.Terrain.InBounds(x, y))
                return false;
            if (State.OccupancySurface[y * State.Terrain.Width + x] != 0)
                return false;
            for (int i = 0; i < State.RuneTraps.Length; i++)
                if (State.RuneTraps[i].Active && State.RuneTraps[i].TileX == x && State.RuneTraps[i].TileY == y)
                    return false;
            for (int i = 0; i < State.RuneTraps.Length; i++)
            {
                if (State.RuneTraps[i].Active)
                    continue;
                State.RuneTraps[i] = new RuneTrap
                {
                    Active = true,
                    TileX = (ushort)x,
                    TileY = (ushort)y,
                    TicksRemaining = SimConstants.RuneTrapLifeTicks,
                    OwnerPlayer = owner,
                };
                int px = x * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                int py = y * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                SpawnEffect(px, py, SimConstants.EffectRune, owner);
                return true;
            }
            return false; // pool exhausted (all 50 match-wide slots in use)
        }

        /// <summary>Mana a freshly created caster starts with (UNIT.C
        /// unit_new: <c>unitMP = 0xff / 3</c>) — call at every unit-creation
        /// site, same as the original's single shared constructor does.</summary>
        void InitCasterMana(ref Unit u)
        {
            if (State.Rules.Units[u.TypeId].Is(UnitTypeFlags.CanCast))
                u.Mana = SimConstants.InitialCasterMana;
        }

        /// <summary>Is this specific caster type allowed to cast this
        /// specific spell — not just "has the owner researched it somewhere".
        /// See <see cref="TechTree.CastableSpellsFor"/>.</summary>
        static bool SpellAllowedFor(UnitTypeId caster, UpgradeId spell)
        {
            var allowed = TechTree.CastableSpellsFor(caster);
            for (int i = 0; i < allowed.Length; i++)
                if (allowed[i] == spell)
                    return true;
            return false;
        }

        /// <summary>
        /// Arms the cast (DSPTBL.C's per-spell RANGE_* + SPELL.C's
        /// dispatch_spell_*): each selected, eligible caster starts walking
        /// toward the target — TickCasting fires the spell once it's actually
        /// in range, exactly like an Attack order chases before it swings.
        /// </summary>
        unsafe void ApplyCastCommand(in GameCommand cmd)
        {
            if (cmd.Param > byte.MaxValue)
                return;
            var spell = (UpgradeId)cmd.Param;
            if (cmd.Player >= SimConstants.MaxPlayers
                || !State.Players[cmd.Player].HasResearched(spell))
            {
                Emit(SimEventKind.CommandDenied, cmd.Player,
                    (ushort)DenyReason.TechUnavailable, cmd.Param);
                return;
            }

            bool ground = TechTree.IsGroundTargetSpell(spell);
            for (int i = 0; i < cmd.SelectionCount; i++)
            {
                if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]), out int idx))
                    continue;
                ref Unit caster = ref State.Units[idx];
                if (caster.Player != cmd.Player
                    || !State.Rules.Units[caster.TypeId].Is(UnitTypeFlags.CanCast)
                    || !SpellAllowedFor((UnitTypeId)caster.TypeId, spell))
                    continue;
                if (!ground && cmd.TargetUnit == 0)
                    continue; // nothing to cast at

                caster.Order = OrderType.Cast;
                caster.PendingSpell = (byte)((byte)spell + 1);
                caster.SpellTargetUnit = ground ? 0 : cmd.TargetUnit;
                caster.SpellTargetX = cmd.TargetX;
                caster.SpellTargetY = cmd.TargetY;
                caster.AttackTarget = 0;
                caster.PathLength = 0;
                caster.PathCursor = 0;
                caster.WaitTicks = 0;
                // Park the walk order on our own tile: movement runs before
                // TickCasting recomputes the real destination.
                caster.OrderX = (ushort)(caster.TileX + caster.StepDX);
                caster.OrderY = (ushort)(caster.TileY + caster.StepDY);
            }
        }

        /// <summary>Walk casters into range, then fire — the movement half of
        /// dispatch_spell_unit/area (target_gone/target_too_far).</summary>
        void TickCasting()
        {
            if (State.Terrain == null)
                return;
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive || u.Order != OrderType.Cast)
                    continue;
                if (u.PendingSpell == 0)
                {
                    u.Order = OrderType.None;
                    continue;
                }

                var spell = (UpgradeId)(u.PendingSpell - 1);
                ushort targetX, targetY;
                if (TechTree.IsGroundTargetSpell(spell))
                {
                    targetX = u.SpellTargetX;
                    targetY = u.SpellTargetY;
                }
                else
                {
                    if (!State.TryGetUnitIndex(UnitId.FromPacked(u.SpellTargetUnit), out int ti)
                        || !State.Units[ti].IsAlive)
                    {
                        CancelCast(ref u);
                        continue;
                    }
                    ref Unit target = ref State.Units[ti];
                    targetX = target.TileX;
                    targetY = target.TileY;
                }

                if (DistanceToPoint(ref u, targetX, targetY) > TechTree.CastRangeFor(spell))
                {
                    if (u.StepRemaining == 0)
                        WalkTo(ref u, targetX, targetY);
                    continue;
                }

                u.PathLength = 0;
                u.PathCursor = 0;
                u.Facing = FacingFrom(Sign(targetX - u.TileX), Sign(targetY - u.TileY));
                ExecuteSpell(i, spell);
                // Casting resolves in a single tick — with no other signal, the
                // view has nothing to tell it the caster just did something and
                // would leave it standing. Reusing the attack cooldown is what
                // already drives the attack-pose window for a normal swing (see
                // UnitViewPool.PickAnimBlock), so a cast plays the same pose for
                // the same brief window, with no new view-side concept needed.
                u.Cooldown = (byte)SimConstants.AttackCooldownTicks;
                CancelCast(ref u);
            }
        }

        static void CancelCast(ref Unit u)
        {
            u.Order = OrderType.None;
            u.PendingSpell = 0;
            u.SpellTargetUnit = 0;
            u.SpellTargetX = 0;
            u.SpellTargetY = 0;
        }

        /// <summary>Chebyshev tile distance from a unit's footprint to a bare
        /// point — same convention as FootprintDistance, but the "other side"
        /// has no footprint of its own.</summary>
        int DistanceToPoint(ref Unit u, int tx, int ty)
        {
            int size = State.Footprint(u.TypeId);
            int dx = Max0(Max(tx - (u.TileX + size - 1), u.TileX - tx));
            int dy = Max0(Max(ty - (u.TileY + size - 1), u.TileY - ty));
            return dx > dy ? dx : dy;
        }

        void ExecuteSpell(int casterIdx, UpgradeId spell)
        {
            switch (spell)
            {
                case UpgradeId.Healing: CastHeal(casterIdx); break;
                case UpgradeId.Exorcism: CastExorcism(casterIdx); break;
                case UpgradeId.Bloodlust: CastBloodlust(casterIdx); break;
                case UpgradeId.Runes: CastRunes(casterIdx); break;
                case UpgradeId.Slow:
                    CastWarp(casterIdx, (short)-SimConstants.WarpTicks, SimConstants.SlowManaCost);
                    break;
                case UpgradeId.Haste:
                    CastWarp(casterIdx, (short)SimConstants.WarpTicks, SimConstants.HasteManaCost);
                    break;
                case UpgradeId.Invisibility: CastInvisibility(casterIdx); break;
                case UpgradeId.Polymorph: CastPolymorph(casterIdx); break;
                case UpgradeId.FlameShield: CastFlameShield(casterIdx); break;
                case UpgradeId.UnholyArmor: CastUnholyArmor(casterIdx); break;
                case UpgradeId.RaiseDead: CastRaiseDead(casterIdx); break;
                case UpgradeId.Blizzard: CastBlizzard(casterIdx); break;
                case UpgradeId.Whirlwind: CastWhirlwind(casterIdx); break;
                case UpgradeId.DeathAndDecay: CastDeathAndDecay(casterIdx); break;
            }
        }

        (int x, int y) CenterOf(ref Unit u)
        {
            int size = State.Footprint(u.TypeId);
            return (u.PixX + size * SimConstants.TilePixels / 2, u.PixY + size * SimConstants.TilePixels / 2);
        }

        void EmitSpellCast(int casterIdx, UpgradeId spell)
        {
            ref Unit caster = ref State.Units[casterIdx];
            Emit(SimEventKind.SpellCast, caster.Player, (ushort)spell, 0,
                new UnitId((ushort)casterIdx, caster.Gen).Packed);
        }

        /// <summary>A cosmetic, damage-free lingering sprite (BULLET.C's
        /// bullet_create_on(pTarget,BT_SPARKLE)-family calls) — reuses the
        /// splash-projectile pool with 0 damage so it just sits and renders
        /// for a while instead of resolving in a single tick.</summary>
        void SpawnEffect(int pixX, int pixY, byte missileType, byte player)
        {
            for (int p = 0; p < State.Projectiles.Length; p++)
            {
                if (State.Projectiles[p].Active)
                    continue;
                State.Projectiles[p] = new Projectile
                {
                    Active = true,
                    MissileType = missileType,
                    PixX = pixX,
                    PixY = pixY,
                    Splash = true,
                    DestPixX = pixX,
                    DestPixY = pixY,
                    Damage = 0,
                    SourcePlayer = player,
                    ChainPulsesRemaining = (ushort)SimConstants.SpellEffectLingerTicks,
                    ChainStepX = 0,
                    ChainStepY = 0,
                };
                return;
            }
            // Pool exhausted: no fallback needed, it's cosmetic only.
        }

        /// <summary>
        /// Shared by Runes/Blizzard/Whirlwind/Death and Decay: spawns
        /// <paramref name="chainCount"/> independent chain-pulse splash shots
        /// (each <paramref name="hitsPerChain"/> hits at the same landing
        /// point, one per tick — BULLET.C disp_bullet_blizzard/rot's exact
        /// recursive re-arm, reusing the gryphon fireball's chain-pulse
        /// projectile), each landing within <paramref name="scatterTiles"/>
        /// of the target. Whirlwind/Death and Decay pulse in place at their
        /// landing point (<see cref="Projectile.PixX"/> starts equal to
        /// <see cref="Projectile.DestPixX"/>); Blizzard (<see
        /// cref="SimConstants.EffectBlizzard"/>) is the one exception —
        /// BULLET.C's blizzard_shards launches every individual shard,
        /// including the first, from a point northwest of where it lands
        /// (see TickProjectiles' matching re-launch on each chain pulse), so
        /// it starts already in flight instead of already arrived.
        /// </summary>
        void SpawnAreaBlast(int casterIdx, ushort tileX, ushort tileY, byte missileType,
            int damagePerHit, int chainCount, int hitsPerChain, int scatterTiles)
        {
            ref Unit caster = ref State.Units[casterIdx];
            uint sourcePacked = new UnitId((ushort)casterIdx, caster.Gen).Packed;
            int w = State.Terrain.Width, h = State.Terrain.Height;
            bool flyingShards = missileType == SimConstants.EffectBlizzard;

            for (int c = 0; c < chainCount; c++)
            {
                int hx = tileX, hy = tileY;
                if (scatterTiles > 0)
                {
                    hx = ClampTo(tileX + State.Rng.Next(scatterTiles * 2 + 1) - scatterTiles, 0, w - 1);
                    hy = ClampTo(tileY + State.Rng.Next(scatterTiles * 2 + 1) - scatterTiles, 0, h - 1);
                }
                int pixX = hx * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                int pixY = hy * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                int startPixX = pixX, startPixY = pixY;
                if (flyingShards)
                {
                    startPixX = BlizzardShardLaunchCoord(pixX, SimConstants.BlizzardShardOffsetX);
                    startPixY = BlizzardShardLaunchCoord(pixY, SimConstants.BlizzardShardOffsetY);
                }

                bool spawned = false;
                for (int p = 0; p < State.Projectiles.Length; p++)
                {
                    if (State.Projectiles[p].Active)
                        continue;
                    State.Projectiles[p] = new Projectile
                    {
                        Active = true,
                        MissileType = missileType,
                        PixX = startPixX,
                        PixY = startPixY,
                        Splash = true,
                        DestPixX = pixX,
                        DestPixY = pixY,
                        SourceUnit = sourcePacked,
                        Damage = damagePerHit,
                        SourcePlayer = caster.Player,
                        ChainPulsesRemaining = (ushort)(hitsPerChain - 1),
                        ChainStepX = 0,
                        ChainStepY = 0,
                    };
                    spawned = true;
                    break;
                }
                // Pool exhausted: land one hit instantly rather than lose the
                // whole chain (Strike()'s own fallback does the same).
                if (!spawned)
                    ApplySplashDamage(pixX, pixY, damagePerHit, caster.Player, sourcePacked);
            }
        }

        /// <summary>BULLET.C blizzard_shards: a shard's launch point sits
        /// <paramref name="offsetMagnitude"/> px northwest of where it lands,
        /// jittered by up to <see cref="SimConstants.BlizzardShardJitterPx"/>
        /// so consecutive shards in the same chain don't all fly the exact
        /// same line.</summary>
        int BlizzardShardLaunchCoord(int landingPix, int offsetMagnitude)
        {
            int jitter = SimConstants.BlizzardShardJitterPx;
            return landingPix - offsetMagnitude + State.Rng.Next(jitter * 2 + 1) - jitter;
        }

        /// <summary>SPELL.C action_heal: mana buys HP at a fixed rate, capped
        /// per cast and by how much the target is actually missing.</summary>
        void CastHeal(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (!State.TryGetUnitIndex(UnitId.FromPacked(caster.SpellTargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || target.Player != caster.Player
                || !State.Rules.Units[target.TypeId].Is(UnitTypeFlags.Organic))
                return;

            int missing = State.Rules.Units[target.TypeId].Hp - target.Hp;
            if (missing <= 0)
                return;
            int canHeal = caster.Mana / SimConstants.HealManaCostPerHp;
            if (canHeal > SimConstants.HealMaxHpPerCast)
                canHeal = SimConstants.HealMaxHpPerCast;
            int healed = missing < canHeal ? missing : canHeal;
            if (healed <= 0)
                return;

            target.Hp += healed;
            caster.Mana -= (byte)(healed * SimConstants.HealManaCostPerHp);
            var (ex, ey) = CenterOf(ref target);
            SpawnEffect(ex, ey, SimConstants.EffectHeal, caster.Player);
            EmitSpellCast(casterIdx, UpgradeId.Healing);
        }

        /// <summary>SPELL.C action_exorcism: an AREA spell (dispatch_spell_area,
        /// see <see cref="TechTree.IsGroundTargetSpell"/>), not a single-target
        /// one — it sweeps an expanding square ring (Chebyshev distance 0..3)
        /// out from the cast point, hitting every Undead unit the ring touches
        /// (<see cref="ExorcismStrike"/> == the original's exorcism()), and
        /// stops expanding the instant mana drops below one hit's cost (the
        /// original's <c>enough_mana</c> check, made once per ring — so a ring
        /// already in progress can still land a few more, even a 0-damage,
        /// hits on whatever mana remains before the NEXT ring's check catches
        /// it). Delta 0 is just the cast-point cell itself, hit twice — the
        /// original's own dx/dy loops collapse to the same cell there, an
        /// upstream quirk kept for fidelity rather than special-cased away.
        /// No ownership check: SPELL.C's exorcism() tests only
        /// <c>IS_UNDEAD</c>, so this can strike the caster's own Undead units
        /// too, exactly like the original.</summary>
        void CastExorcism(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            ushort tx = caster.SpellTargetX, ty = caster.SpellTargetY;
            if (State.Terrain == null || !State.Terrain.InBounds(tx, ty))
                return;

            bool hitAny = false;
            for (int delta = 0; delta <= SimConstants.ExorcismMaxRingDelta; delta++)
            {
                if (caster.Mana < SimConstants.ExorcismManaCostPerDamage)
                    break;

                for (int dx = -delta; dx <= delta; dx++)
                {
                    int x1 = tx + dx;
                    hitAny |= ExorcismStrike(casterIdx, x1, ty + delta);
                    hitAny |= ExorcismStrike(casterIdx, x1, ty - delta);
                }
                for (int dy = -delta + 1; dy <= delta - 1; dy++)
                {
                    int y1 = ty + dy;
                    hitAny |= ExorcismStrike(casterIdx, tx + delta, y1);
                    hitAny |= ExorcismStrike(casterIdx, tx - delta, y1);
                }
            }
            if (hitAny)
                EmitSpellCast(casterIdx, UpgradeId.Exorcism);
        }

        /// <summary>SPELL.C exorcism(): one ring cell's worth of the Exorcism
        /// sweep. No-ops (and spends nothing) off-map or on a non-Undead cell;
        /// otherwise buys unarmored damage at the caster's current mana,
        /// capped by the target's remaining HP, even down to a 0-damage hit
        /// that still flashes and consumes 0 mana once the caster is too poor
        /// to hurt it — matching the original exactly rather than skipping
        /// the flash below some minimum.</summary>
        bool ExorcismStrike(int casterIdx, int x, int y)
        {
            if (State.Terrain == null || !State.Terrain.InBounds(x, y))
                return false;
            uint occ = State.OccupancySurface[y * State.Terrain.Width + x];
            if (occ == 0 || !State.TryGetUnitIndex(UnitId.FromPacked(occ), out int ti))
                return false;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || !State.Rules.Units[target.TypeId].Is(UnitTypeFlags.Undead))
                return false;

            ref Unit caster = ref State.Units[casterIdx];
            int dmg = caster.Mana / SimConstants.ExorcismManaCostPerDamage;
            if (dmg > target.Hp)
                dmg = target.Hp;

            var (ex, ey) = CenterOf(ref target);
            SpawnEffect(ex, ey, SimConstants.EffectExorcism, caster.Player);
            if (dmg > 0)
                ApplyDamage(ti, dmg, caster.Player);
            caster.Mana -= (byte)(dmg * SimConstants.ExorcismManaCostPerDamage);
            return true;
        }

        /// <summary>SPELL.C action_bloodlust: a flat mana cost enrages the
        /// target for a fixed duration — DAMAGE.C doubles both damage
        /// components (<see cref="RageScale"/>) while <see cref="Unit.RageTicks"/>
        /// is nonzero.</summary>
        void CastBloodlust(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.BloodlustManaCost)
                return;
            if (!State.TryGetUnitIndex(UnitId.FromPacked(caster.SpellTargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || target.Player != caster.Player
                || !State.Rules.Units[target.TypeId].Is(UnitTypeFlags.Organic))
                return;

            caster.Mana -= (byte)SimConstants.BloodlustManaCost;
            target.RageTicks = SimConstants.BloodlustRageTicks;
            var (ex, ey) = CenterOf(ref target);
            SpawnEffect(ex, ey, SimConstants.EffectSparkle, caster.Player);
            EmitSpellCast(casterIdx, UpgradeId.Bloodlust);
        }

        /// <summary>SPELL.C action_runes/place_a_rune: one cast pays the full
        /// mana cost up front and attempts to arm 5 traps in a plus pattern
        /// (target tile + its 4 orthogonal neighbours). Each placement that
        /// fails — off-map, standing on a unit, or a tile some other active
        /// trap already occupies — partially refunds the cast; the traps
        /// themselves flicker briefly into view every so often (see
        /// <see cref="TickRunes"/>) until they either expire or detonate.</summary>
        void CastRunes(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.RunesManaCost)
                return;
            ushort tx = caster.SpellTargetX, ty = caster.SpellTargetY;
            if (State.Terrain == null || !State.Terrain.InBounds(tx, ty))
                return;

            caster.Mana -= (byte)SimConstants.RunesManaCost;
            byte owner = caster.Player;

            int placed = 0;
            if (PlaceRune(tx, ty, owner)) placed++;
            if (PlaceRune(tx + 1, ty, owner)) placed++;
            if (PlaceRune(tx - 1, ty, owner)) placed++;
            if (PlaceRune(tx, ty + 1, owner)) placed++;
            if (PlaceRune(tx, ty - 1, owner)) placed++;

            int refund = SimConstants.RunesRefundPerFailedTrap * (5 - placed);
            caster.Mana = (byte)Min(SimConstants.MaxMana, caster.Mana + refund);

            EmitSpellCast(casterIdx, UpgradeId.Runes);
        }

        /// <summary>SPELL.C action_slow/action_haste: a flat mana cost sets
        /// the target's <see cref="Unit.WarpTicks"/> — negative slows,
        /// positive hastes, matching the original's own unitWarp sign.
        /// Simplified to a flat overwrite rather than the original's
        /// override-if-opposite / extend-if-same accumulation.</summary>
        void CastWarp(int casterIdx, short warpTicks, int manaCost)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < manaCost)
                return;
            if (!State.TryGetUnitIndex(UnitId.FromPacked(caster.SpellTargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || target.Player != caster.Player)
                return;

            caster.Mana -= (byte)manaCost;
            target.WarpTicks = warpTicks;
            var (ex, ey) = CenterOf(ref target);
            SpawnEffect(ex, ey, SimConstants.EffectSparkle, caster.Player);
            EmitSpellCast(casterIdx, warpTicks > 0 ? UpgradeId.Haste : UpgradeId.Slow);
        }

        /// <summary>SPELL.C action_invis: untargetable by auto-acquisition
        /// (<see cref="FindTargetInRange"/>) and hidden from enemy vision
        /// while it lasts (see TickFog's InvisTicks check).</summary>
        void CastInvisibility(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.InvisibilityManaCost)
                return;
            if (!State.TryGetUnitIndex(UnitId.FromPacked(caster.SpellTargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || target.Player != caster.Player)
                return;

            caster.Mana -= (byte)SimConstants.InvisibilityManaCost;
            target.InvisTicks = SimConstants.InvisibilityTicks;
            var (ex, ey) = CenterOf(ref target);
            SpawnEffect(ex, ey, SimConstants.EffectSparkle, caster.Player);
            EmitSpellCast(casterIdx, UpgradeId.Invisibility);
        }

        /// <summary>SPELL.C action_polymorph: an unconditional kill (bypasses
        /// Unholy Armor immunity, like the original's unit_kill — not a
        /// damage roll) that drops a neutral sheep in the target's place.</summary>
        void CastPolymorph(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.PolymorphManaCost)
                return;
            if (!State.TryGetUnitIndex(UnitId.FromPacked(caster.SpellTargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || target.Player == caster.Player
                || target.Player >= SimConstants.MaxPlayers)
                return;

            caster.Mana -= (byte)SimConstants.PolymorphManaCost;
            ushort tx = target.TileX, ty = target.TileY;
            var (ex, ey) = CenterOf(ref target);
            target.ArmorTicks = 0;
            ApplyDamage(ti, target.Hp > 0 ? target.Hp : 1, caster.Player);
            State.SpawnUnit((ushort)UnitTypeId.CritterSheep, SimConstants.NeutralPlayer, tx, ty);
            SpawnEffect(ex, ey, SimConstants.EffectSparkle, caster.Player);
            EmitSpellCast(casterIdx, UpgradeId.Polymorph);
        }

        /// <summary>SPELL.C action_fireshield: a cosmetic status only (see the
        /// type doc comment) — blocks re-casting while already up, and can't
        /// be cast on a flying unit (unit_is_flyer check).</summary>
        void CastFlameShield(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (!State.TryGetUnitIndex(UnitId.FromPacked(caster.SpellTargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || target.Player != caster.Player || target.FireShieldTicks > 0
                || State.Rules.Units[target.TypeId].Is(UnitTypeFlags.AirUnit))
                return;
            if (caster.Mana < SimConstants.FlameShieldManaCost)
                return;

            caster.Mana -= (byte)SimConstants.FlameShieldManaCost;
            target.FireShieldTicks = SimConstants.FlameShieldTicks;
            var (ex, ey) = CenterOf(ref target);
            SpawnEffect(ex, ey, SimConstants.EffectSparkle, caster.Player);
            EmitSpellCast(casterIdx, UpgradeId.FlameShield);
        }

        /// <summary>SPELL.C action_armor: total damage immunity for a fixed
        /// duration, paid for with half the target's current HP as well as
        /// mana.</summary>
        void CastUnholyArmor(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.UnholyArmorManaCost)
                return;
            if (!State.TryGetUnitIndex(UnitId.FromPacked(caster.SpellTargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (!target.IsAlive || target.Player != caster.Player)
                return;

            caster.Mana -= (byte)SimConstants.UnholyArmorManaCost;
            target.ArmorTicks = SimConstants.UnholyArmorTicks;
            if (target.Hp >= 2)
                target.Hp >>= 1;
            var (ex, ey) = CenterOf(ref target);
            SpawnEffect(ex, ey, SimConstants.EffectSparkle, caster.Player);
            EmitSpellCast(casterIdx, UpgradeId.UnholyArmor);
        }

        /// <summary>SPELL.C action_raisedead: scans corpses within
        /// RaiseDeadScanRadius of the target point (nearest first isn't
        /// required by the original — a slot-order scan is fine, it's not
        /// choosing "the best" corpse), converting each into a Skeleton at a
        /// mana cost per skeleton, until mana runs out.</summary>
        void CastRaiseDead(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.RaiseDeadManaCost)
                return;
            int tx = caster.SpellTargetX, ty = caster.SpellTargetY;
            int radiusSq = SimConstants.RaiseDeadScanRadius * SimConstants.RaiseDeadScanRadius;
            int raised = 0;
            bool any = false;

            for (int i = 0; i < State.Corpses.Length && raised < SimConstants.RaiseDeadMaxSkeletons; i++)
            {
                ref Corpse c = ref State.Corpses[i];
                if (!c.Active)
                    continue;
                if (caster.Mana < SimConstants.RaiseDeadManaCost)
                    break;
                int dx = c.TileX - tx, dy = c.TileY - ty;
                if (dx * dx + dy * dy > radiusSq)
                    continue;

                caster.Mana -= (byte)SimConstants.RaiseDeadManaCost;
                State.SpawnUnit((ushort)UnitTypeId.Skeleton, caster.Player, c.TileX, c.TileY);
                int px = c.TileX * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                int py = c.TileY * SimConstants.TilePixels + SimConstants.TilePixels / 2;
                c.Active = false;
                SpawnEffect(px, py, SimConstants.EffectSparkle, caster.Player);
                raised++;
                any = true;
            }
            if (any)
                EmitSpellCast(casterIdx, UpgradeId.RaiseDead);
        }

        void CastBlizzard(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.BlizzardManaCost)
                return;
            ushort tx = caster.SpellTargetX, ty = caster.SpellTargetY;
            if (State.Terrain == null || !State.Terrain.InBounds(tx, ty))
                return;

            caster.Mana -= (byte)SimConstants.BlizzardManaCost;
            SpawnAreaBlast(casterIdx, tx, ty, SimConstants.EffectBlizzard, SimConstants.BlizzardDamagePerHit,
                SimConstants.BlizzardChains, SimConstants.BlizzardHitsPerChain,
                scatterTiles: SimConstants.BlizzardScatterTiles);
            EmitSpellCast(casterIdx, UpgradeId.Blizzard);
        }

        void CastWhirlwind(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.WhirlwindManaCost)
                return;
            ushort tx = caster.SpellTargetX, ty = caster.SpellTargetY;
            if (State.Terrain == null || !State.Terrain.InBounds(tx, ty))
                return;

            caster.Mana -= (byte)SimConstants.WhirlwindManaCost;
            SpawnAreaBlast(casterIdx, tx, ty, SimConstants.EffectWhirlwind, SimConstants.WhirlwindDamagePerHit,
                chainCount: 1, hitsPerChain: SimConstants.WhirlwindHits, scatterTiles: 0);
            EmitSpellCast(casterIdx, UpgradeId.Whirlwind);
        }

        void CastDeathAndDecay(int casterIdx)
        {
            ref Unit caster = ref State.Units[casterIdx];
            if (caster.Mana < SimConstants.DeathAndDecayManaCost)
                return;
            ushort tx = caster.SpellTargetX, ty = caster.SpellTargetY;
            if (State.Terrain == null || !State.Terrain.InBounds(tx, ty))
                return;

            caster.Mana -= (byte)SimConstants.DeathAndDecayManaCost;
            SpawnAreaBlast(casterIdx, tx, ty, SimConstants.EffectDecay, SimConstants.DeathAndDecayDamagePerHit,
                SimConstants.DeathAndDecayChains, SimConstants.DeathAndDecayHitsPerChain,
                SimConstants.DeathAndDecayScatterTiles);
            EmitSpellCast(casterIdx, UpgradeId.DeathAndDecay);
        }
    }
}
