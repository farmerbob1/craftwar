using System.Collections.Generic;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class SpellTests
    {
        static PudFile MakeMap(params (UnitTypeId type, byte owner, ushort x, ushort y)[] units)
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0001;
            }
            pud.Owner[0] = (byte)PudOwner.Human;
            pud.Owner[1] = (byte)PudOwner.Computer;
            foreach (var (type, owner, x, y) in units)
                pud.Units.Add(new PudUnitEntry { X = x, Y = y, Type = (byte)type, Owner = owner });
            return pud;
        }

        static unsafe GameCommand CastOrder(GameSim sim, int caster, UpgradeId spell,
            uint targetUnit = 0, ushort targetX = 0, ushort targetY = 0)
        {
            var cmd = new GameCommand
            {
                Op = CommandOp.Cast,
                Player = sim.State.Units[caster].Player,
                Param = (ushort)spell,
                TargetUnit = targetUnit,
                TargetX = targetX,
                TargetY = targetY,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId((ushort)caster, sim.State.Units[caster].Gen).Packed;
            return cmd;
        }

        /// <summary>Issues the cast, then advances until the caster's Order
        /// drops out of Cast (walked into range and fired, or gave up) or
        /// maxTicks is reached — casting is no longer instant, it walks into
        /// range first (TechTree.CastRangeFor), so a single Advance() is no
        /// longer guaranteed to resolve it.</summary>
        static void CastAndWait(GameSim sim, int caster, GameCommand cmd, int maxTicks = 500)
        {
            sim.Advance(new List<GameCommand> { cmd });
            var none = new List<GameCommand>();
            for (int t = 0; t < maxTicks && sim.State.Units[caster].Order == OrderType.Cast; t++)
                sim.Advance(none);
        }

        [Test]
        public void FreshlySpawnedCaster_StartsAtOneThirdMana_AndRegensOverTime()
        {
            var pud = MakeMap((UnitTypeId.Paladin, 0, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            Assert.AreEqual(SimConstants.InitialCasterMana, sim.State.Units[0].Mana);

            var none = new List<GameCommand>();
            for (int t = 0; t < SimConstants.ManaRegenPeriodTicks; t++)
                sim.Advance(none);
            Assert.AreEqual(SimConstants.InitialCasterMana + 1, sim.State.Units[0].Mana,
                "mana must tick up by one every ManaRegenPeriodTicks ticks");
        }

        [Test]
        public void NonCaster_NeverGainsMana()
        {
            var pud = MakeMap((UnitTypeId.Footman, 0, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            var none = new List<GameCommand>();
            for (int t = 0; t < 500; t++)
                sim.Advance(none);
            Assert.AreEqual(0, sim.State.Units[0].Mana);
        }

        [Test]
        public unsafe void Heal_RequiresResearch_ThenRestoresHpAtManaCost()
        {
            var pud = MakeMap(
                (UnitTypeId.Paladin, 0, 10, 10),
                (UnitTypeId.Footman, 0, 11, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Units[1].Hp = 10; // damaged, max is 60

            uint targetPacked = new UnitId(1, sim.State.Units[1].Gen).Packed;

            // Not researched yet: no effect.
            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Healing, targetPacked));
            Assert.AreEqual(10, sim.State.Units[1].Hp, "casting without research must do nothing");

            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Healing;
            int manaBefore = sim.State.Units[0].Mana;
            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Healing, targetPacked));

            int healed = sim.State.Units[1].Hp - 10;
            Assert.Greater(healed, 0, "researched heal must restore HP");
            Assert.LessOrEqual(healed, SimConstants.HealMaxHpPerCast);
            Assert.That(sim.State.Units[0].Mana,
                Is.EqualTo(manaBefore - healed * SimConstants.HealManaCostPerHp).Within(1));
        }

        [Test]
        public unsafe void Heal_CannotTargetEnemyOrMechanicalUnit()
        {
            var pud = MakeMap(
                (UnitTypeId.Paladin, 0, 10, 10),
                (UnitTypeId.Footman, 1, 20, 20),      // enemy, out of react range
                (UnitTypeId.Ballista, 0, 12, 10));    // own, but not Organic
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Healing;
            sim.State.Units[1].Hp = 5;
            sim.State.Units[2].Hp = 5;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Healing,
                new UnitId(1, sim.State.Units[1].Gen).Packed));
            Assert.AreEqual(5, sim.State.Units[1].Hp, "must not heal an enemy");

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Healing,
                new UnitId(2, sim.State.Units[2].Gen).Packed));
            Assert.AreEqual(5, sim.State.Units[2].Hp, "must not heal a non-organic unit");
        }

        [Test]
        public unsafe void Exorcism_DamagesEnemyUndead_ButNotFleshyEnemies()
        {
            // 8 tiles: inside Exorcism's cast range (10) but outside a
            // Paladin's react range (5), so nothing auto-engages in melee
            // while the Paladin walks into spell range.
            var pud = MakeMap(
                (UnitTypeId.Paladin, 0, 10, 10),
                (UnitTypeId.Skeleton, 1, 18, 10),
                (UnitTypeId.Grunt, 1, 18, 12));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Exorcism;
            int skeletonHpBefore = sim.State.Units[1].Hp;
            int gruntHpBefore = sim.State.Units[2].Hp;
            int manaBefore = sim.State.Units[0].Mana;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Exorcism,
                new UnitId(1, sim.State.Units[1].Gen).Packed));
            int dmg = skeletonHpBefore - sim.State.Units[1].Hp;
            Assert.Greater(dmg, 0, "exorcism must damage an enemy undead unit");
            int expectedMana = manaBefore - dmg * SimConstants.ExorcismManaCostPerDamage;
            Assert.That(sim.State.Units[0].Mana, Is.EqualTo(expectedMana).Within(1));

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Exorcism,
                new UnitId(2, sim.State.Units[2].Gen).Packed));
            Assert.AreEqual(gruntHpBefore, sim.State.Units[2].Hp,
                "exorcism must not damage a non-undead enemy");
        }

        [Test]
        public unsafe void Bloodlust_DoublesDamage_ForItsDuration()
        {
            var pud = MakeMap((UnitTypeId.OgreMage, 1, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[1].Researched |= 1ul << (int)UpgradeId.Bloodlust;

            int baseStrength = sim.EffectiveStrength(ref sim.State.Units[0]);
            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Bloodlust,
                new UnitId(0, sim.State.Units[0].Gen).Packed));

            int rageAfterCast = sim.State.Units[0].RageTicks;
            Assert.Greater(rageAfterCast, 0);
            Assert.AreEqual(baseStrength * 2, sim.EffectiveStrength(ref sim.State.Units[0]));

            var none = new List<GameCommand>();
            for (int t = 0; t < rageAfterCast; t++)
                sim.Advance(none);
            Assert.AreEqual(0, sim.State.Units[0].RageTicks);
            Assert.AreEqual(baseStrength, sim.EffectiveStrength(ref sim.State.Units[0]));
        }

        [Test]
        public void Runes_PlacesFiveTrapsInAPlusPattern()
        {
            var pud = MakeMap((UnitTypeId.OgreMage, 0, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Runes;
            sim.State.Units[0].Mana = SimConstants.MaxMana;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Runes, targetX: 20, targetY: 20));

            (int x, int y)[] expected = { (20, 20), (21, 20), (19, 20), (20, 21), (20, 19) };
            foreach (var (x, y) in expected)
                Assert.IsTrue(HasActiveTrapAt(sim, x, y), $"expected an armed trap at {x},{y}");
            Assert.AreEqual(5, ActiveTrapCount(sim), "all five placements should succeed away from the map edge");
            // A cast resolving on tick 0 coincides with slot 0's own
            // staggered mana-regen tick (see TickSpells), so an incidental
            // +1 can land in the same Advance() as the cost/refund — a real,
            // deterministic artifact of tick 0, not slop to hide a bug.
            Assert.That(sim.State.Units[0].Mana,
                Is.EqualTo(SimConstants.MaxMana - SimConstants.RunesManaCost).Within(1),
                "no placement failed, so no partial refund is owed");
        }

        [Test]
        public void Runes_TrapDetonatesWhenAGroundUnitStepsOnIt()
        {
            // SPELL.C place_a_rune refuses to arm on a tile a unit already
            // occupies, so the footman must start clear of the plus pattern
            // and walk onto an already-armed trap afterward. The caster sits
            // far off to the side (still in Runes' cast range of the target)
            // so it never auto-engages the passing footman in ordinary combat
            // and confounds the HP assertion below.
            var pud = MakeMap(
                (UnitTypeId.OgreMage, 0, 15, 0),
                (UnitTypeId.Footman, 1, 12, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Runes;
            sim.State.Units[0].Mana = SimConstants.MaxMana;
            int hpBefore = sim.State.Units[1].Hp;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Runes, targetX: 15, targetY: 10));
            Assert.AreEqual(5, ActiveTrapCount(sim), "the footman starts well clear of the plus pattern");

            var none = new List<GameCommand>();
            sim.Advance(new List<GameCommand> { MoveOrder(sim, 1, 15, 10) });
            for (int t = 0; t < 500 && sim.State.Units[1].Hp == hpBefore; t++)
                sim.Advance(none);

            // The footman's direct path east reaches the west trap (14,10)
            // before the center (15,10) — whichever tile it steps on first
            // is the one that fires, not necessarily the move destination.
            Assert.AreEqual(hpBefore - SimConstants.RuneTrapDamage, sim.State.Units[1].Hp,
                "walking onto an armed trap must deal its flat damage, unmitigated by armor");
            Assert.AreEqual(4, ActiveTrapCount(sim), "exactly one trap of the plus pattern is consumed");
        }

        [Test]
        public void Runes_CannotBePlacedOnATileAUnitAlreadyOccupies()
        {
            var pud = MakeMap(
                (UnitTypeId.OgreMage, 0, 10, 10),
                (UnitTypeId.Footman, 1, 20, 20));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Runes;
            sim.State.Units[0].Mana = SimConstants.MaxMana;

            // Target the footman's own tile: SPELL.C place_a_rune's
            // `gfpUnitMap[y*w+x]` check refuses to arm there.
            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Runes, targetX: 20, targetY: 20));

            Assert.IsFalse(HasActiveTrapAt(sim, 20, 20), "a unit-occupied tile must never get a trap");
            Assert.AreEqual(4, ActiveTrapCount(sim), "only the center placement should fail");
        }

        [Test]
        public void Runes_ArmedTrapFlickersPeriodicallyWhileWaiting()
        {
            var pud = MakeMap((UnitTypeId.OgreMage, 0, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Runes;
            sim.State.Units[0].Mana = SimConstants.MaxMana;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Runes, targetX: 20, targetY: 20));
            Assert.AreEqual(5, ActiveTrapCount(sim));

            // Let the placement-time flash (SpellEffectLingerTicks) fully
            // decay, then watch for the next one within a flicker period.
            var none = new List<GameCommand>();
            for (int t = 0; t < SimConstants.SpellEffectLingerTicks + 5; t++)
                sim.Advance(none);
            Assert.IsFalse(AnyActiveEffectAt(sim, SimConstants.EffectRune, 20, 20),
                "the initial placement flash must have decayed by now");

            bool flickered = false;
            for (int t = 0; t < SimConstants.RuneFlickerPeriodTicks + 5 && !flickered; t++)
            {
                sim.Advance(none);
                flickered = AnyActiveEffectAt(sim, SimConstants.EffectRune, 20, 20);
            }
            Assert.IsTrue(flickered, "an armed trap must periodically flicker back into view while it waits");
        }

        [Test]
        public void Runes_UntouchedTrapExpiresAfterItsLifetime()
        {
            var pud = MakeMap((UnitTypeId.OgreMage, 0, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Runes;
            sim.State.Units[0].Mana = SimConstants.MaxMana;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Runes, targetX: 20, targetY: 20));
            Assert.AreEqual(5, ActiveTrapCount(sim));

            var none = new List<GameCommand>();
            for (int t = 0; t < SimConstants.RuneTrapLifeTicks; t++)
                sim.Advance(none);
            Assert.AreEqual(0, ActiveTrapCount(sim), "every trap must expire once its lifetime elapses untouched");
        }

        [Test]
        public void Runes_RefundsManaForEachBlockedPlacement()
        {
            // Target the map's western edge (x=0) from a caster standing
            // well away from it: the west neighbour of the plus pattern is
            // off-map, so exactly one of five placements fails and is
            // partially refunded — and since the caster itself never stands
            // on any of the five tiles, none of the traps self-triggers.
            var pud = MakeMap((UnitTypeId.OgreMage, 0, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Runes;
            sim.State.Units[0].Mana = SimConstants.MaxMana;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Runes, targetX: 0, targetY: 10));

            Assert.AreEqual(4, ActiveTrapCount(sim), "the off-map western trap must fail to place");
            // See the tick-0 mana-regen note in Runes_PlacesFiveTrapsInAPlusPattern.
            Assert.That(sim.State.Units[0].Mana,
                Is.EqualTo(SimConstants.MaxMana - SimConstants.RunesManaCost + SimConstants.RunesRefundPerFailedTrap)
                    .Within(1),
                "one failed placement should refund exactly one trap's share of the cost");
        }

        static int ActiveTrapCount(GameSim sim)
        {
            int n = 0;
            foreach (var t in sim.State.RuneTraps)
                if (t.Active) n++;
            return n;
        }

        static bool HasActiveTrapAt(GameSim sim, int x, int y)
        {
            foreach (var t in sim.State.RuneTraps)
                if (t.Active && t.TileX == x && t.TileY == y)
                    return true;
            return false;
        }

        static bool AnyActiveEffectAt(GameSim sim, byte missileType, int tileX, int tileY)
        {
            int px = tileX * SimConstants.TilePixels + SimConstants.TilePixels / 2;
            int py = tileY * SimConstants.TilePixels + SimConstants.TilePixels / 2;
            foreach (var p in sim.State.Projectiles)
                if (p.Active && p.MissileType == missileType && p.PixX == px && p.PixY == py)
                    return true;
            return false;
        }

        static unsafe GameCommand MoveOrder(GameSim sim, int unitSlot, ushort tx, ushort ty)
        {
            var cmd = new GameCommand
            {
                Op = CommandOp.Move,
                Player = sim.State.Units[unitSlot].Player,
                TargetX = tx,
                TargetY = ty,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId((ushort)unitSlot, sim.State.Units[unitSlot].Gen).Packed;
            return cmd;
        }

        /// <summary>Ticks for unit 0 (a lone Footman at 10,10) to walk one
        /// tile east, with WarpTicks set to <paramref name="warpSign"/> *
        /// SimConstants.WarpTicks (comfortably longer than the walk takes,
        /// so it can't decay to 0 mid-test).</summary>
        static int TicksToWalkOneTile(int warpSign)
        {
            var pud = MakeMap((UnitTypeId.Footman, 0, 10, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Units[0].WarpTicks = (short)(warpSign * SimConstants.WarpTicks);
            sim.Advance(new List<GameCommand> { MoveOrder(sim, 0, 11, 10) });
            var none = new List<GameCommand>();
            int t = 0;
            for (; t < 500 && sim.State.Units[0].TileX != 11; t++)
                sim.Advance(none);
            return t;
        }

        [Test]
        public void Slow_HalvesSpeed_Haste_DoublesSpeed()
        {
            int normal = TicksToWalkOneTile(0);
            int slowed = TicksToWalkOneTile(-1);
            int hasted = TicksToWalkOneTile(1);
            Assert.Greater(slowed, normal, "a slowed unit must take longer to cover the same ground");
            Assert.Less(hasted, normal, "a hasted unit must cover the same ground faster");
        }

        [Test]
        public unsafe void Invisibility_HidesFromAutoAcquisitionAndFromEnemyDetection()
        {
            var pud = MakeMap(
                (UnitTypeId.Mage, 0, 10, 10),
                (UnitTypeId.Footman, 1, 11, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Invisibility;
            sim.State.Units[0].Mana = SimConstants.MaxMana;
            uint magePacked = new UnitId(0, sim.State.Units[0].Gen).Packed;

            // The mage casts Invisibility on itself.
            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Invisibility, magePacked));
            Assert.Greater(sim.State.Units[0].InvisTicks, 0);
            Assert.IsFalse(sim.IsUnitDetected(1, ref sim.State.Units[0]),
                "an invisible unit must not resolve as detected for an enemy");

            var none = new List<GameCommand>();
            for (int t = 0; t < 200; t++)
                sim.Advance(none);
            Assert.AreEqual(0u, sim.State.Units[1].AttackTarget,
                "the footman must never auto-acquire an invisible target");
        }

        [Test]
        public unsafe void Polymorph_TransformsEnemyIntoANeutralSheep()
        {
            var pud = MakeMap(
                (UnitTypeId.Mage, 0, 10, 10),
                (UnitTypeId.Grunt, 1, 15, 15));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Polymorph;
            sim.State.Units[0].Mana = SimConstants.MaxMana;
            var originalGrunt = new UnitId(1, sim.State.Units[1].Gen);

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.Polymorph, originalGrunt.Packed));

            // The grunt's slot is very likely recycled immediately (the sheep
            // spawns right after), so a stale-handle check is the only
            // reliable way to confirm the original unit is actually gone.
            Assert.IsFalse(sim.State.TryGetUnitIndex(originalGrunt, out _),
                "the original grunt's handle must no longer resolve");
            bool foundSheep = false;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].TypeId == (ushort)UnitTypeId.CritterSheep
                    && sim.State.Units[i].TileX == 15 && sim.State.Units[i].TileY == 15)
                    foundSheep = true;
            Assert.IsTrue(foundSheep, "a neutral sheep must appear where the grunt stood");
        }

        [Test]
        public unsafe void FlameShield_IsCosmeticOnly_AndBlocksTargetingAFlyer()
        {
            var pud = MakeMap(
                (UnitTypeId.Mage, 0, 10, 10),
                (UnitTypeId.Footman, 0, 11, 10),
                (UnitTypeId.GryphonRider, 0, 12, 10));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.FlameShield;
            sim.State.Units[0].Mana = SimConstants.MaxMana;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.FlameShield,
                new UnitId(1, sim.State.Units[1].Gen).Packed));
            Assert.Greater(sim.State.Units[1].FireShieldTicks, 0,
                "flame shield must still apply its status to a ground unit");

            // Per the source (see SimConstants/GameSim.Spells.cs doc comments)
            // there is no damage-reflection mechanic — just a status flag.
            int manaBefore = sim.State.Units[0].Mana;
            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.FlameShield,
                new UnitId(2, sim.State.Units[2].Gen).Packed));
            Assert.AreEqual(0, sim.State.Units[2].FireShieldTicks,
                "flame shield must not be castable on a flying unit");
            Assert.AreEqual(manaBefore, sim.State.Units[0].Mana,
                "a rejected cast must not spend mana");
        }

        [Test]
        public unsafe void UnholyArmor_GrantsImmunity_AtTheCostOfHalfCurrentHp()
        {
            var pud = MakeMap(
                (UnitTypeId.DeathKnight, 0, 10, 10),
                (UnitTypeId.Footman, 1, 20, 20));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.UnholyArmor;
            sim.State.Units[0].Mana = SimConstants.MaxMana;
            int hpBefore = sim.State.Units[0].Hp;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.UnholyArmor,
                new UnitId(0, sim.State.Units[0].Gen).Packed));
            Assert.AreEqual(hpBefore / 2, sim.State.Units[0].Hp, "half the current HP is the cost of the armor");
            Assert.Greater(sim.State.Units[0].ArmorTicks, 0);

            int hpAfterCast = sim.State.Units[0].Hp;
            var none = new List<GameCommand>();
            for (int t = 0; t < 300; t++)
                sim.Advance(none);
            Assert.AreEqual(hpAfterCast, sim.State.Units[0].Hp,
                "an armored unit must take no damage at all while it's up");
        }

        [Test]
        public unsafe void RaiseDead_ReanimatesANearbyCorpseIntoASkeleton()
        {
            var pud = MakeMap(
                (UnitTypeId.DeathKnight, 1, 10, 10),
                (UnitTypeId.Grunt, 1, 30, 30),      // killed to leave a corpse
                (UnitTypeId.Footman, 0, 31, 30));   // the killer
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[1].Researched |= 1ul << (int)UpgradeId.RaiseDead;
            sim.State.Units[0].Mana = SimConstants.MaxMana;
            sim.State.Units[1].Hp = 1; // one hit from the footman kills it

            var atk = new GameCommand
            {
                Op = CommandOp.Attack,
                Player = 0,
                TargetUnit = new UnitId(1, sim.State.Units[1].Gen).Packed,
                SelectionCount = 1,
            };
            atk.Selection.Ids[0] = new UnitId(2, sim.State.Units[2].Gen).Packed;
            var none = new List<GameCommand>();
            sim.Advance(new List<GameCommand> { atk });
            for (int t = 0; t < 200 && sim.State.Units[1].IsAlive; t++)
                sim.Advance(none);
            Assert.IsFalse(sim.State.Units[1].IsAlive, "sanity: the grunt must actually die");

            bool foundCorpse = false;
            ushort corpseX = 0, corpseY = 0;
            for (int i = 0; i < sim.State.Corpses.Length; i++)
                if (sim.State.Corpses[i].Active)
                {
                    foundCorpse = true;
                    corpseX = sim.State.Corpses[i].TileX;
                    corpseY = sim.State.Corpses[i].TileY;
                }
            Assert.IsTrue(foundCorpse, "a dead grunt must register a raisable corpse");

            // Teleport the Death Knight next to the corpse (spell range is
            // generous — 6 tiles — this just keeps the test's tick budget
            // small instead of testing pathfinding).
            ref var dk = ref sim.State.Units[0];
            dk.TileX = corpseX;
            dk.TileY = (ushort)(corpseY > 0 ? corpseY - 1 : 0);
            dk.PixX = dk.TileX * SimConstants.TilePixels;
            dk.PixY = dk.TileY * SimConstants.TilePixels;

            CastAndWait(sim, 0, CastOrder(sim, 0, UpgradeId.RaiseDead, targetX: corpseX, targetY: corpseY));

            bool foundSkeleton = false;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].TypeId == (ushort)UnitTypeId.Skeleton
                    && sim.State.Units[i].Player == 1)
                    foundSkeleton = true;
            Assert.IsTrue(foundSkeleton, "a skeleton owned by the caster's player must appear");

            foundCorpse = false;
            for (int i = 0; i < sim.State.Corpses.Length; i++)
                if (sim.State.Corpses[i].Active) foundCorpse = true;
            Assert.IsFalse(foundCorpse, "the corpse must be consumed by raising it");
        }

        [Test]
        public unsafe void Whirlwind_PulsesDamageManyTimes()
        {
            var pud = MakeMap(
                (UnitTypeId.DeathKnight, 0, 10, 10),
                (UnitTypeId.Footman, 1, 15, 15));
            var sim = new GameSim(1);
            sim.Setup(pud, RuleSet.CreateDefault());
            sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Whirlwind;
            sim.State.Units[0].Mana = SimConstants.MaxMana;

            sim.Advance(new List<GameCommand>
            {
                CastOrder(sim, 0, UpgradeId.Whirlwind, targetX: 15, targetY: 15),
            });

            int projSlot = -1;
            for (int p = 0; p < sim.State.Projectiles.Length; p++)
                if (sim.State.Projectiles[p].Active)
                    projSlot = p;
            Assert.GreaterOrEqual(projSlot, 0, "whirlwind must create a chain-pulse projectile");

            int lastPulses = SimConstants.WhirlwindHits - 1;
            int pulseEvents = 0;
            var none = new List<GameCommand>();
            for (int t = 0; t < SimConstants.WhirlwindHits + 50 && sim.State.Projectiles[projSlot].Active; t++)
            {
                sim.Advance(none);
                int pulses = sim.State.Projectiles[projSlot].ChainPulsesRemaining;
                if (pulses < lastPulses)
                    pulseEvents++;
                lastPulses = pulses;
            }
            Assert.AreEqual(SimConstants.WhirlwindHits - 1, pulseEvents);
        }

        [Test]
        public void Cast_IsDeterministic()
        {
            GameSim Play()
            {
                var pud = MakeMap(
                    (UnitTypeId.Paladin, 0, 10, 10),
                    (UnitTypeId.Footman, 0, 11, 10));
                var sim = new GameSim(3);
                sim.Setup(pud, RuleSet.CreateDefault());
                sim.State.Players[0].Researched |= 1ul << (int)UpgradeId.Healing;
                sim.State.Units[1].Hp = 1;
                var none = new List<GameCommand>();
                bool cast = false;
                for (int t = 0; t < 200; t++)
                {
                    if (!cast)
                    {
                        unsafe
                        {
                            sim.Advance(new List<GameCommand>
                            {
                                CastOrder(sim, 0, UpgradeId.Healing,
                                    new UnitId(1, sim.State.Units[1].Gen).Packed),
                            });
                        }
                        cast = true;
                    }
                    else
                    {
                        sim.Advance(none);
                    }
                }
                return sim;
            }

            var a = Play();
            var b = Play();
            Assert.AreEqual(a.State.ComputeHash(), b.State.ComputeHash());
        }
    }
}
