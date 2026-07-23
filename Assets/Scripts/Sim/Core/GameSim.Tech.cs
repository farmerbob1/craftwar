namespace Craftwar.Sim
{
    /// <summary>
    /// Tech-tree side of the sim: build/train/research gating, research
    /// execution, building self-upgrades, repair, and the upgrade-adjusted
    /// combat stats (magnitudes from the original gbUpgradeStepsTbl:
    /// arrows +1, swords/axes +2, shields +2, ship attack/armor +5,
    /// catapult/ballista +15, longbow/lighter axes +1 range).
    /// </summary>
    public sealed partial class GameSim
    {
        // ------------------------------------------------------------------
        // Gating
        // ------------------------------------------------------------------

        bool HasCompleteBuilding(byte player, UnitTypeId required)
        {
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (u.IsAlive && u.Player == player
                    && (u.Flags & UnitFlags.Building) != 0
                    && (u.Flags & UnitFlags.UnderConstruction) == 0
                    && TechTree.Satisfies((UnitTypeId)u.TypeId, required))
                    return true;
            }
            return false;
        }

        bool PrereqsMet(byte player, UnitTypeId[] prereqs)
        {
            for (int i = 0; i < prereqs.Length; i++)
                if (!HasCompleteBuilding(player, prereqs[i]))
                    return false;
            return true;
        }

        bool UnitAllowed(byte player, UnitTypeId type)
        {
            int bit = TechTree.AlowUnitBit(type);
            return bit < 0 || (State.Players[player].AllowedUnits & (1u << bit)) != 0;
        }

        /// <summary>Full erection/production gate: ALOW plus prereq buildings.
        /// Public so the HUD can grey buttons from the same rule.</summary>
        public bool CanProduce(byte player, UnitTypeId type) =>
            UnitAllowed(player, type) && PrereqsMet(player, TechTree.Prereqs(type));

        /// <summary>Is `want` a valid Train order for this building right now?
        /// `want` must be the effective form — once rangers are researched
        /// the barracks trains rangers, never plain archers.</summary>
        public bool CanTrainAt(byte player, UnitTypeId building, UnitTypeId want)
        {
            var trains = TechTree.Trains(building);
            ulong researched = State.Players[player].Researched;
            for (int i = 0; i < trains.Length; i++)
                if (TechTree.TrainSubstitute(trains[i], researched) == want)
                    return CanProduce(player, want);
            return false;
        }

        public bool CanUpgradeBuildingTo(byte player, UnitTypeId building, UnitTypeId target)
        {
            var options = TechTree.UpgradesTo(building);
            for (int i = 0; i < options.Length; i++)
                if (options[i] == target)
                    return CanProduce(player, target);
            return false;
        }

        public bool CanResearchAt(byte player, UnitTypeId building, UpgradeId u)
        {
            if ((int)u >= UgrdParser.UpgradeCount)
                return false;
            ref PlayerState p = ref State.Players[player];
            if (p.HasResearched(u))
                return false;

            var offered = TechTree.Research(building);
            bool listed = false;
            for (int i = 0; i < offered.Length; i++)
                listed |= offered[i] == u;
            if (!listed)
                return false;

            var prior = TechTree.ResearchPrior(u);
            if (prior != UpgradeId.None && !p.HasResearched(prior))
                return false;
            if (!PrereqsMet(player, TechTree.ResearchPrereqBuildings(u)))
                return false;

            int ubit = TechTree.AlowUpgradeBit(u);
            if (ubit >= 0 && (p.AllowedUpgrades & (1u << ubit)) == 0)
                return false;
            int sbit = TechTree.AlowSpellBit(u);
            if (sbit >= 0 && (p.AllowedSpells & (1u << sbit)) == 0)
                return false;

            // Already being researched somewhere: no double spend.
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit b = ref State.Units[i];
                if (b.IsAlive && b.Player == player && b.ResearchId == (byte)((int)u + 1))
                    return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        unsafe void ApplyResearchCommand(in GameCommand cmd)
        {
            // Same one-event-per-command discipline as Train: remember the first
            // real reason, report it only if no lab takes the order.
            var deny = DenyReason.None;
            bool taken = false;
            for (int i = 0; i < cmd.SelectionCount && !taken; i++)
            {
                if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]), out int idx))
                    continue;
                ref Unit b = ref State.Units[idx];
                if (b.Player != cmd.Player || (b.Flags & UnitFlags.Building) == 0)
                    continue;
                if ((b.Flags & UnitFlags.UnderConstruction) != 0
                    || b.BuildType != 0 || b.ResearchId != 0)
                {
                    if (deny == DenyReason.None)
                        deny = DenyReason.Busy;
                    continue;
                }
                var u = (UpgradeId)cmd.Param;
                if (!CanResearchAt(b.Player, (UnitTypeId)b.TypeId, u))
                {
                    if (deny == DenyReason.None)
                        deny = DenyReason.TechUnavailable;
                    continue;
                }
                ref PlayerState p = ref State.Players[b.Player];
                ref UpgradeData row = ref State.Rules.Upgrades[(int)u];
                var shortfall = ShortfallFor(ref p, row.Gold, row.Lumber, row.Oil, needsFood: false);
                if (shortfall != DenyReason.None)
                {
                    if (deny == DenyReason.None || deny == DenyReason.Busy
                        || deny == DenyReason.TechUnavailable)
                        deny = shortfall;
                    continue;
                }
                p.Gold -= row.Gold;
                p.Lumber -= row.Lumber;
                p.Oil -= row.Oil;
                b.ResearchId = (byte)((int)u + 1);
                b.TrainTicks = BuildTicksFor(row.Time);
                taken = true; // one lab takes the order
            }
            if (!taken && deny != DenyReason.None)
                Emit(SimEventKind.CommandDenied, cmd.Player, (ushort)deny, cmd.Param);
        }

        /// <summary>Cancel whatever the selected building is working on —
        /// construction, research or training — with a full refund
        /// (the original refunds 100% on cancel).</summary>
        unsafe void ApplyCancelCommand(in GameCommand cmd)
        {
            for (int i = 0; i < cmd.SelectionCount; i++)
            {
                if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]), out int idx))
                    continue;
                ref Unit b = ref State.Units[idx];
                if (b.Player != cmd.Player || (b.Flags & UnitFlags.Building) == 0)
                    continue;
                ref PlayerState p = ref State.Players[b.Player];
                if ((b.Flags & UnitFlags.UnderConstruction) != 0)
                {
                    ref UnitTypeData row = ref State.Rules.Units[b.TypeId];
                    p.Gold += row.GoldCost;
                    p.Lumber += row.LumberCost;
                    p.Oil += row.OilCost;
                    ReleaseBuilder(ref b, idx);
                    State.DestroyUnit(new UnitId((ushort)idx, b.Gen));
                }
                else if (b.ResearchId != 0)
                {
                    ref UpgradeData row = ref State.Rules.Upgrades[b.ResearchId - 1];
                    p.Gold += row.Gold;
                    p.Lumber += row.Lumber;
                    p.Oil += row.Oil;
                    b.ResearchId = 0;
                    b.TrainTicks = 0;
                }
                else if (b.BuildType != 0)
                {
                    ref UnitTypeData row = ref State.Rules.Units[b.BuildType - 1];
                    p.Gold += row.GoldCost;
                    p.Lumber += row.LumberCost;
                    p.Oil += row.OilCost;
                    b.BuildType = 0;
                    b.TrainTicks = 0;
                }
            }
        }

        unsafe void ApplyRepairCommand(in GameCommand cmd)
        {
            if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.TargetUnit), out int ti))
                return;
            ref Unit target = ref State.Units[ti];
            if (target.Player != cmd.Player
                || (target.Flags & UnitFlags.Building) == 0
                || (target.Flags & UnitFlags.UnderConstruction) != 0)
                return;
            for (int i = 0; i < cmd.SelectionCount; i++)
            {
                if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]), out int idx))
                    continue;
                ref Unit u = ref State.Units[idx];
                if (u.Player != cmd.Player
                    || !State.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                    continue;
                u.Order = OrderType.Repair;
                u.ResourceTarget = cmd.TargetUnit;
                u.Harvest = HarvestStage.None;
                u.AttackTarget = 0;
                u.Timer = 0;
                u.PathLength = 0;
                u.PathCursor = 0;
            }
        }

        // ------------------------------------------------------------------
        // Per-tick work
        // ------------------------------------------------------------------

        /// <summary>Peon hammering a building back to health (DISPATCH.C
        /// pacing: +4 HP per event, 1 gold + 1 lumber every 2 events).</summary>
        void TickRepair(ref Unit u, int index)
        {
            if (!State.TryGetUnitIndex(UnitId.FromPacked(u.ResourceTarget), out int ti))
            {
                u.Order = OrderType.None;
                return;
            }
            ref Unit b = ref State.Units[ti];
            ref UnitTypeData row = ref State.Rules.Units[b.TypeId];
            if ((b.Flags & UnitFlags.Building) == 0 || b.Hp >= row.Hp)
            {
                u.Order = OrderType.None;
                u.PathLength = 0;
                return;
            }
            if (FootprintDistance(ref u, ref b) > 1)
            {
                WalkToBuilding(ref u, ref b);
                return;
            }

            // Adjacent: park on this tile and face the wall being fixed.
            u.OrderX = u.TileX;
            u.OrderY = u.TileY;
            u.PathLength = 0;
            int size = State.Footprint(b.TypeId);
            u.Facing = FacingFrom(
                Sign(ClampTo(u.TileX, b.TileX, b.TileX + size - 1) - u.TileX),
                Sign(ClampTo(u.TileY, b.TileY, b.TileY + size - 1) - u.TileY));

            if (u.Timer > 0)
            {
                u.Timer--;
                return;
            }
            u.Timer = SimConstants.RepairEventPeriodTicks;

            ref PlayerState p = ref State.Players[u.Player];
            if (p.Gold < SimConstants.RepairChargeGold
                || p.Lumber < SimConstants.RepairChargeLumber)
            {
                u.Order = OrderType.None; // out of materials
                return;
            }
            b.Hp += SimConstants.RepairHpPerEvent;
            if (b.Hp > row.Hp)
                b.Hp = row.Hp;
            // The building accumulates repair events; every second one is billed.
            b.Timer++;
            if (b.Timer >= SimConstants.RepairEventsPerCharge)
            {
                b.Timer = 0;
                p.Gold -= SimConstants.RepairChargeGold;
                p.Lumber -= SimConstants.RepairChargeLumber;
            }
        }

        void CompleteResearch(byte player, UpgradeId u)
        {
            State.Players[player].Researched |= 1ul << (int)u;

            // Unlock upgrades instantly convert the fielded base units.
            TechTree.TransformFor(u, out var from, out var to);
            if (from == UnitTypeId.None)
                return;
            int hpDiff = State.Rules.Units[(int)to].Hp - State.Rules.Units[(int)from].Hp;
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit t = ref State.Units[i];
                if (!t.IsAlive || t.Player != player || t.TypeId != (ushort)from)
                    continue;
                t.TypeId = (ushort)to;
                t.Hp += hpDiff;
                if (t.Hp < 1)
                    t.Hp = 1;
            }
        }

        /// <summary>Swap a building to its upgraded tier in place (same
        /// footprint for every WC2 upgrade pair), keeping the HP delta.</summary>
        void UpgradeBuildingType(ref Unit b, ushort target)
        {
            int hpDiff = State.Rules.Units[target].Hp - State.Rules.Units[b.TypeId].Hp;
            b.TypeId = target;
            b.Hp += hpDiff;
            if (b.Hp < 1)
                b.Hp = 1;
        }

        // ------------------------------------------------------------------
        // Upgrade-adjusted combat stats. A player only ever researches their
        // own race's line, so summing the paired human+orc levels is safe.
        // ------------------------------------------------------------------

        static bool IsBowUnit(ushort t) => (UnitTypeId)t is UnitTypeId.Archer
            or UnitTypeId.Axethrower or UnitTypeId.Ranger or UnitTypeId.Berserker;

        static bool IsSiegeUnit(ushort t) =>
            (UnitTypeId)t is UnitTypeId.Ballista or UnitTypeId.Catapult;

        /// <summary>
        /// Displayed unit level: one per researched upgrade that applies to this
        /// unit, on top of a base of 1. Counts tiers, never magnitudes — the
        /// per-line steps differ (arrows +1, swords +2, ballista +15) but each
        /// researched tier is worth exactly one level.
        /// </summary>
        public int UnitLevel(ref Unit u)
        {
            int level = 1;
            if (u.Player >= SimConstants.MaxPlayers)
                return level;
            ref PlayerState p = ref State.Players[u.Player];
            ref UnitTypeData row = ref State.Rules.Units[u.TypeId];

            // Weapon line — the same selection EffectiveStrength/Pierce make.
            if (IsBowUnit(u.TypeId))
                level += p.UpgradeLevel(UpgradeId.Arrow1, UpgradeId.Arrow2)
                    + p.UpgradeLevel(UpgradeId.Spear1, UpgradeId.Spear2);
            else if (row.WeaponsUpgradable)
            {
                if (IsSiegeUnit(u.TypeId))
                    level += p.UpgradeLevel(UpgradeId.Ballista1, UpgradeId.Ballista2)
                        + p.UpgradeLevel(UpgradeId.Catapult1, UpgradeId.Catapult2);
                else if (row.MoveDomain == 2)
                    level += p.UpgradeLevel(UpgradeId.HumanShipCannon1, UpgradeId.HumanShipCannon2)
                        + p.UpgradeLevel(UpgradeId.OrcShipCannon1, UpgradeId.OrcShipCannon2);
                else
                    level += p.UpgradeLevel(UpgradeId.Sword1, UpgradeId.Sword2)
                        + p.UpgradeLevel(UpgradeId.Axe1, UpgradeId.Axe2);
            }

            // Armor line.
            if (row.ArmorUpgradable)
            {
                if (row.MoveDomain == 2)
                    level += p.UpgradeLevel(UpgradeId.HumanShipArmor1, UpgradeId.HumanShipArmor2)
                        + p.UpgradeLevel(UpgradeId.OrcShipArmor1, UpgradeId.OrcShipArmor2);
                else
                    level += p.UpgradeLevel(UpgradeId.HumanShield1, UpgradeId.HumanShield2)
                        + p.UpgradeLevel(UpgradeId.OrcShield1, UpgradeId.OrcShield2);
            }

            return level;
        }

        public int EffectiveStrength(ref Unit u)
        {
            ref UnitTypeData row = ref State.Rules.Units[u.TypeId];
            int s = row.BasicDamage;
            if (u.Player >= SimConstants.MaxPlayers)
                return s;
            ref PlayerState p = ref State.Players[u.Player];
            // Marksmanship rangers deliver their strength as pierce (DAMAGE.C).
            if (u.TypeId == (ushort)UnitTypeId.Ranger
                && p.HasResearched(UpgradeId.RangerMarksmanship))
                return 0;
            if (!row.WeaponsUpgradable)
                return s;
            if (IsSiegeUnit(u.TypeId))
                s += (p.UpgradeLevel(UpgradeId.Ballista1, UpgradeId.Ballista2)
                    + p.UpgradeLevel(UpgradeId.Catapult1, UpgradeId.Catapult2)) * 15;
            else if (row.MoveDomain == 2)
                s += (p.UpgradeLevel(UpgradeId.HumanShipCannon1, UpgradeId.HumanShipCannon2)
                    + p.UpgradeLevel(UpgradeId.OrcShipCannon1, UpgradeId.OrcShipCannon2)) * 5;
            else if (!IsBowUnit(u.TypeId))
                s += (p.UpgradeLevel(UpgradeId.Sword1, UpgradeId.Sword2)
                    + p.UpgradeLevel(UpgradeId.Axe1, UpgradeId.Axe2)) * 2;
            return s;
        }

        public int EffectivePierce(ref Unit u)
        {
            ref UnitTypeData row = ref State.Rules.Units[u.TypeId];
            int pd = row.PiercingDamage;
            if (u.Player >= SimConstants.MaxPlayers || !IsBowUnit(u.TypeId))
                return pd;
            ref PlayerState p = ref State.Players[u.Player];
            pd += p.UpgradeLevel(UpgradeId.Arrow1, UpgradeId.Arrow2)
                + p.UpgradeLevel(UpgradeId.Spear1, UpgradeId.Spear2);
            if (u.TypeId == (ushort)UnitTypeId.Ranger
                && p.HasResearched(UpgradeId.RangerMarksmanship))
                pd += row.BasicDamage; // the strength moved over from EffectiveStrength
            return pd;
        }

        public int EffectiveArmor(ref Unit u)
        {
            ref UnitTypeData row = ref State.Rules.Units[u.TypeId];
            int a = row.Armor;
            if (u.Player >= SimConstants.MaxPlayers || !row.ArmorUpgradable)
                return a;
            ref PlayerState p = ref State.Players[u.Player];
            if (row.MoveDomain == 2)
                a += (p.UpgradeLevel(UpgradeId.HumanShipArmor1, UpgradeId.HumanShipArmor2)
                    + p.UpgradeLevel(UpgradeId.OrcShipArmor1, UpgradeId.OrcShipArmor2)) * 5;
            else
                a += (p.UpgradeLevel(UpgradeId.HumanShield1, UpgradeId.HumanShield2)
                    + p.UpgradeLevel(UpgradeId.OrcShield1, UpgradeId.OrcShield2)) * 2;
            return a;
        }

        public int EffectiveRange(ref Unit u)
        {
            ref UnitTypeData row = ref State.Rules.Units[u.TypeId];
            int r = row.AttackRange;
            if (u.Player >= SimConstants.MaxPlayers)
                return r;
            ref PlayerState p = ref State.Players[u.Player];
            if (u.TypeId == (ushort)UnitTypeId.Ranger && p.HasResearched(UpgradeId.Longbow))
                r += 1;
            if (u.TypeId == (ushort)UnitTypeId.Berserker && p.HasResearched(UpgradeId.LighterAxes))
                r += 1;
            return r;
        }

        /// <summary>For the M6 fog pass; scouting adds +3 sight.</summary>
        public int EffectiveSight(ref Unit u)
        {
            ref UnitTypeData row = ref State.Rules.Units[u.TypeId];
            int s = row.Sight;
            if (u.Player >= SimConstants.MaxPlayers)
                return s;
            ref PlayerState p = ref State.Players[u.Player];
            if (u.TypeId == (ushort)UnitTypeId.Ranger && p.HasResearched(UpgradeId.RangerScouting))
                s += 3;
            if (u.TypeId == (ushort)UnitTypeId.Berserker
                && p.HasResearched(UpgradeId.BerserkerScouting))
                s += 3;
            // AI vision handicap; zero outside a handicapped match.
            s += p.SightBonus;
            return s;
        }
    }
}
