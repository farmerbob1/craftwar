namespace Craftwar.Sim.Ai
{
    public sealed partial class AiPlayer
    {
        enum BuildAttempt : byte
        {
            Issued,
            NoResources,
            NoBuilder,
            NoSite,
            NoTech,
            /// <summary>Needs more oil than the player holds. The land-attack
            /// script never builds an oil economy, so the stock can only
            /// fall — waiting on this goal is provably futile.</summary>
            NoOil,
        }

        /// <summary>
        /// Drop ledger entries whose build order is no longer in flight. The
        /// sim deducts a Build's cost only on builder arrival, so the ledger
        /// must mirror the walk exactly: an entry lives while the builder is
        /// still visibly walking with that order, and dies when the builder is
        /// gone, was denied on arrival (order cleared), or arrived and paid
        /// (builder hides inside the site — the deduction is now real). A
        /// timed-out walk is cancelled with a Stop so ledger and world agree.
        /// </summary>
        void ReconcilePending()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pb = _pending[i];
                if (!_s.TryGetUnitIndex(UnitId.FromPacked(pb.BuilderPacked), out int bi))
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                ref Unit b = ref _s.Units[bi];
                if (b.Order != OrderType.Build || b.BuildType != pb.TypeId + 1
                    || (b.Flags & UnitFlags.Hidden) != 0)
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                if (_s.Tick - pb.IssuedTick > PendingBuildTimeoutTicks)
                {
                    Emit(AiQueries.Command(CommandOp.Stop, Slot, pb.BuilderPacked));
                    _blacklistedSites.Add(pb.Y * _s.Terrain.Width + pb.X);
                    _pending.RemoveAt(i);
                }
            }
        }

        int PendingOfType(ushort typeId)
        {
            int n = 0;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].TypeId == typeId)
                    n++;
            return n;
        }

        /// <summary>
        /// Walk the script's cumulative build goals strictly in order and act
        /// on the first unmet one — plus, ahead of everything, a farm whenever
        /// food pressure looms (the original auto-builds farms outside its
        /// script too). One build start per think.
        /// </summary>
        void ThinkBuild()
        {
            if (NeedFarm() && TryIssueBuild((ushort)AiRaceMap.Unit(AiUnit.Farm, _race))
                == BuildAttempt.Issued)
                return;

            var desired = new int[32]; // per-AiUnit running totals while walking
            int flat = -1;
            for (int pi = 0; pi <= _phase && pi < Strategy.Phases.Length; pi++)
            {
                var unlock = Strategy.Phases[pi].Unlock;
                for (int ei = 0; ei < unlock.Length; ei++)
                {
                    flat = (pi << 8) | ei;
                    var role = unlock[ei];
                    desired[(int)role]++;
                    if (_emergency && role != AiUnit.Hall)
                        continue;
                    if (_skippedGoals.Contains(flat))
                        continue;
                    if (OwnedForRole(role) >= desired[(int)role])
                        continue;

                    var attempt = AttemptGoal(role);
                    if (attempt == BuildAttempt.Issued)
                    {
                        _stallThinks = 0;
                        return;
                    }
                    if (attempt == BuildAttempt.NoOil)
                    {
                        _skippedGoals.Add(flat); // futile forever; walk on
                        continue;
                    }
                    // NoSite while otherwise able is the one true stall; count
                    // it and relax (wider radius, then skip this entry). Other
                    // failures resolve themselves: resources accrue, builders
                    // free up, prereqs finish constructing.
                    if (attempt == BuildAttempt.NoSite)
                    {
                        _stallThinks++;
                        if (_stallThinks >= 2 * StallRelaxThinks)
                        {
                            _skippedGoals.Add(flat);
                            _stallThinks = 0;
                        }
                    }
                    return; // strict order: wait on the first unmet goal
                }
            }
        }

        bool NeedFarm()
        {
            ref PlayerState p = ref _s.Players[Slot];
            // Count queued trainees: each lands as +1 food use on completion.
            int queued = 0;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (u.IsAlive && u.Player == Slot && (u.Flags & UnitFlags.Building) != 0
                    && u.BuildType != 0
                    && !_s.Rules.Units[u.BuildType - 1].Is(UnitTypeFlags.Building))
                    queued++;
            }
            if (p.FoodUsed + queued + 1 <= p.FoodMax)
                return false;

            ushort farm = (ushort)AiRaceMap.Unit(AiUnit.Farm, _race);
            // One at a time: none pending and none under construction.
            if (PendingOfType(farm) > 0)
                return false;
            int complete = AiQueries.CountAlive(_s, Slot, farm, includeUnderConstruction: false);
            int all = AiQueries.CountAlive(_s, Slot, farm, includeUnderConstruction: true);
            return all == complete;
        }

        static bool IsTierUpgrade(AiUnit role) =>
            role is AiUnit.Keep or AiUnit.Castle or AiUnit.GuardTower or AiUnit.CannonTower;

        int OwnedForRole(AiUnit role)
        {
            ushort typeId = (ushort)AiRaceMap.Unit(role, _race);
            switch (role)
            {
                case AiUnit.Hall:
                case AiUnit.Keep:
                case AiUnit.Castle:
                    // Higher tiers stand in for lower ones (Satisfies), and a
                    // hall mid-upgrade counts as its target tier already.
                    return AiQueries.CountSatisfying(_s, Slot, (UnitTypeId)typeId,
                            includeUnderConstruction: true)
                        + AiQueries.CountInTraining(_s, Slot, typeId)
                        + (role == AiUnit.Hall ? PendingOfType(typeId) : 0);
                default:
                    return AiQueries.CountAlive(_s, Slot, typeId, includeUnderConstruction: true)
                        + AiQueries.CountInTraining(_s, Slot, typeId)
                        + PendingOfType(typeId);
            }
        }

        BuildAttempt AttemptGoal(AiUnit role)
        {
            ushort typeId = (ushort)AiRaceMap.Unit(role, _race);
            return IsTierUpgrade(role) ? TryIssueUpgrade(typeId) : TryIssueBuild(typeId);
        }

        BuildAttempt TryIssueBuild(ushort typeId)
        {
            if (!_sim.CanProduce(Slot, (UnitTypeId)typeId))
                return BuildAttempt.NoTech;
            ref UnitTypeData row = ref _s.Rules.Units[typeId];
            if (row.OilCost > _s.Players[Slot].Oil)
                return BuildAttempt.NoOil;
            if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                return BuildAttempt.NoResources;

            int w = AiQueries.FindIdleWorker(_s, Slot, _claimed);
            if (w < 0)
                w = AiQueries.FindHarvestingWorker(_s, Slot, wood: false, _claimed);
            if (w < 0)
                w = AiQueries.FindHarvestingWorker(_s, Slot, wood: true, _claimed);
            if (w < 0)
                return BuildAttempt.NoBuilder;
            uint packed = AiQueries.PackedId(_s, w);

            int radius = _stallThinks >= StallRelaxThinks
                ? 2 * AiSiteSearch.MaxRadius
                : AiSiteSearch.MaxRadius;
            if (!FindSiteAvoidingBlacklist(typeId, radius, packed, out int x, out int y))
                return BuildAttempt.NoSite;

            Emit(AiQueries.Command(CommandOp.Build, Slot, packed,
                (ushort)x, (ushort)y, param: typeId));
            _pending.Add(new PendingBuild
            {
                BuilderPacked = packed,
                TypeId = typeId,
                X = (ushort)x,
                Y = (ushort)y,
                IssuedTick = _s.Tick,
            });
            return BuildAttempt.Issued;
        }

        bool FindSiteAvoidingBlacklist(ushort typeId, int radius, uint builderPacked,
            out int x, out int y)
        {
            // Higher tiers plan a clustered, non-boxing layout; if it can't find a
            // plot it falls through to the naive spiral so a build still happens.
            if (_tier.PlannedLayout
                && AiBasePlan.FindSite(_s, Slot, typeId, _anchorX, _anchorY, radius,
                    builderPacked, _blacklistedSites, out x, out y))
                return true;

            // The spiral is deterministic, so a handful of poisoned top-left
            // tiles (timed-out walks) is all the exclusion state needed.
            int size = _s.Footprint(typeId);
            for (int r = AiSiteSearch.MinRadius; r <= radius; r++)
            {
                int len = AiSiteSearch.RingLength(r);
                for (int i = 0; i < len; i++)
                {
                    AiSiteSearch.RingTile(_anchorX, _anchorY, r, i, out x, out y);
                    if (_blacklistedSites.Contains(y * _s.Terrain.Width + x))
                        continue;
                    if (AiSiteSearch.FindSiteAt(_s, typeId, size, x, y, builderPacked))
                        return true;
                }
            }
            x = 0;
            y = 0;
            return false;
        }

        BuildAttempt TryIssueUpgrade(ushort targetType)
        {
            // Find an idle building whose self-upgrade list offers the target.
            int baseIdx = -1;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player != Slot || (u.Flags & UnitFlags.Building) == 0
                    || (u.Flags & UnitFlags.UnderConstruction) != 0
                    || u.BuildType != 0 || u.ResearchId != 0)
                    continue;
                var options = TechTree.UpgradesTo((UnitTypeId)u.TypeId);
                for (int o = 0; o < options.Length; o++)
                    if (options[o] == (UnitTypeId)targetType)
                    {
                        baseIdx = i;
                        break;
                    }
                if (baseIdx >= 0)
                    break;
            }
            if (baseIdx < 0)
                return BuildAttempt.NoBuilder;
            if (!_sim.CanUpgradeBuildingTo(Slot,
                    (UnitTypeId)_s.Units[baseIdx].TypeId, (UnitTypeId)targetType))
                return BuildAttempt.NoTech;
            ref UnitTypeData row = ref _s.Rules.Units[targetType];
            if (row.OilCost > _s.Players[Slot].Oil)
                return BuildAttempt.NoOil;
            if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                return BuildAttempt.NoResources;

            Emit(AiQueries.Command(CommandOp.Train, Slot,
                AiQueries.PackedId(_s, baseIdx), param: targetType));
            return BuildAttempt.Issued;
        }
    }
}
