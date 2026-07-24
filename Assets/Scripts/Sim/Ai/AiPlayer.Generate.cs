using System.Collections.Generic;
using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Economy/build/tech generators. Each turns the current state into scored
    /// candidate actions for the arbiter; the feasibility logic (tech gates, food,
    /// cost reservation, idle-provider search, connectivity-gated placement) is the
    /// same the sim enforces, so a candidate that scores is a candidate that works.
    /// </summary>
    public sealed partial class AiPlayer
    {
        enum BuildAttempt : byte { Issued, NoResources, NoBuilder, NoSite, NoTech, NoOil }

        // ---- Build: a farm under food pressure, then the first unmet build-order role ----

        void GenBuild()
        {
            // Farm pre-empt: food is a hard prerequisite for everything downstream,
            // so it gets a high, near-constant priority when pressure looms.
            if (NeedFarm())
            {
                ushort farm = (ushort)AiRaceMap.Unit(AiUnit.Farm, _race);
                if (TryBuildCommand(farm, out var fcmd, out ushort fx, out ushort fy, out _)
                    == BuildAttempt.Issued)
                    AddCandidate(AiActionKind.BuildFarm,
                        Util.Score(Profile.WeightFarm, AiMath.One),
                        in fcmd, farm, fx, fy);
            }

            var order = Profile.BuildOrder;
            var desired = new int[32];
            for (int ei = 0; ei < order.Length; ei++)
            {
                var role = order[ei];
                desired[(int)role]++;
                if (_emergency && role != AiUnit.Hall)
                    continue;
                if (_skippedGoals.Contains(ei))
                    continue;
                if (OwnedForRole(role) >= desired[(int)role])
                    continue;

                // First unmet role only: strict order preserves tech dependencies,
                // and the utility layer arbitrates build vs. train vs. econ globally.
                var attempt = AttemptBuild(role, out var cmd, out ushort sx, out ushort sy,
                    out ushort pendType);
                if (attempt == BuildAttempt.Issued)
                {
                    _stallThinks = 0;
                    // Earlier build-order entries matter more; safety damps building
                    // out in the open while under attack.
                    int need = AiMath.Normalize(order.Length - ei, 0, order.Length);
                    if (need < AiMath.Half) need = AiMath.Half;
                    int score = Util.Score(Profile.WeightBuild, need, BaseSafety());
                    AddCandidate(pendType != 0 ? AiActionKind.Build : AiActionKind.UpgradeBuilding,
                        score, in cmd, pendType, sx, sy);
                    return;
                }
                if (attempt == BuildAttempt.NoOil)
                {
                    _skippedGoals.Add(ei); // land profile never builds oil — futile
                    continue;
                }
                if (attempt == BuildAttempt.NoSite)
                {
                    _stallThinks++;
                    if (_stallThinks >= 2 * StallRelaxThinks)
                    {
                        _skippedGoals.Add(ei);
                        _stallThinks = 0;
                    }
                }
                return; // wait on the first unmet goal
            }
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
                    return AiQueries.CountSatisfying(_s, Slot, (UnitTypeId)typeId, true)
                        + AiQueries.CountInTraining(_s, Slot, typeId)
                        + (role == AiUnit.Hall ? PendingOfType(typeId) : 0);
                default:
                    return AiQueries.CountAlive(_s, Slot, typeId, true)
                        + AiQueries.CountInTraining(_s, Slot, typeId)
                        + PendingOfType(typeId);
            }
        }

        BuildAttempt AttemptBuild(AiUnit role, out GameCommand cmd,
            out ushort sx, out ushort sy, out ushort pendType)
        {
            ushort typeId = (ushort)AiRaceMap.Unit(role, _race);
            if (IsTierUpgrade(role))
            {
                pendType = 0;
                sx = sy = 0;
                return TryUpgradeCommand(typeId, out cmd);
            }
            pendType = typeId;
            return TryBuildCommand(typeId, out cmd, out sx, out sy, out _);
        }

        BuildAttempt TryBuildCommand(ushort typeId, out GameCommand cmd,
            out ushort sx, out ushort sy, out uint builderPacked)
        {
            cmd = default;
            sx = sy = 0;
            builderPacked = 0;
            if (!_sim.CanProduce(Slot, (UnitTypeId)typeId))
                return BuildAttempt.NoTech;
            ref UnitTypeData row = ref _s.Rules.Units[typeId];
            if (row.OilCost > _s.Players[Slot].Oil)
                return BuildAttempt.NoOil;
            if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                return BuildAttempt.NoResources;

            int w = FindBuilder();
            if (w < 0)
                return BuildAttempt.NoBuilder;
            uint packed = AiQueries.PackedId(_s, w);

            int radius = _stallThinks >= StallRelaxThinks
                ? 2 * AiSiteSearch.MaxRadius
                : AiSiteSearch.MaxRadius;
            if (!_planner.FindSite(_s, Slot, typeId, _anchorX, _anchorY, radius, packed,
                    _blacklistedSites, _threat, out int x, out int y))
                return BuildAttempt.NoSite;

            sx = (ushort)x;
            sy = (ushort)y;
            builderPacked = packed;
            cmd = AiQueries.Command(CommandOp.Build, Slot, packed, sx, sy, param: typeId);
            return BuildAttempt.Issued;
        }

        BuildAttempt TryUpgradeCommand(ushort targetType, out GameCommand cmd)
        {
            cmd = default;
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
                    if (options[o] == (UnitTypeId)targetType) { baseIdx = i; break; }
                if (baseIdx >= 0) break;
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
            cmd = AiQueries.Command(CommandOp.Train, Slot,
                AiQueries.PackedId(_s, baseIdx), param: targetType);
            return BuildAttempt.Issued;
        }

        /// <summary>Pick a builder and earmark it for this think so no other
        /// candidate (or the harvest balancer) re-uses the same worker. Prefers an
        /// idle worker, then a gold harvester, then a wood harvester.</summary>
        int FindBuilder()
        {
            int w = AiQueries.FindIdleWorker(_s, Slot, _genReserved);
            if (w < 0) w = AiQueries.FindHarvestingWorker(_s, Slot, wood: false, _genReserved);
            if (w < 0) w = AiQueries.FindHarvestingWorker(_s, Slot, wood: true, _genReserved);
            if (w >= 0)
                _genReserved.Add(AiQueries.PackedId(_s, w));
            return w;
        }

        bool NeedFarm()
        {
            ref PlayerState p = ref _s.Players[Slot];
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
            if (PendingOfType(farm) > 0)
                return false;
            int complete = AiQueries.CountAlive(_s, Slot, farm, false);
            int all = AiQueries.CountAlive(_s, Slot, farm, true);
            return all == complete;
        }

        // ---- Economy: worker training + gold/wood balancing ----

        void GenEconomy()
        {
            if (!_emergency)
                GenTrainWorker();
            GenHarvest();
        }

        void GenTrainWorker()
        {
            ushort workerType = (ushort)AiRaceMap.Unit(AiUnit.Worker, _race);
            int target = Profile.WorkerTarget * HallCountForWorkers();
            int have = AiQueries.CountAlive(_s, Slot, workerType, true)
                + AiQueries.CountInTraining(_s, Slot, workerType);
            if (have >= target)
                return;
            ref PlayerState p = ref _s.Players[Slot];
            if (p.FoodUsed + 1 > p.FoodMax)
                return;
            ref UnitTypeData row = ref _s.Rules.Units[workerType];
            if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                return;
            var hall = _race == Race.Orc ? UnitTypeId.GreatHall : UnitTypeId.TownHall;
            int b = AiQueries.FindIdleBuilding(_s, Slot, hall);
            if (b < 0)
                return;
            int deficit = AiMath.Normalize(target - have, 0, target);
            int score = Util.Score(Profile.WeightWorker, deficit);
            AddCandidate(AiActionKind.TrainWorker, score,
                AiQueries.Command(CommandOp.Train, Slot, AiQueries.PackedId(_s, b),
                    param: workerType));
        }

        int HallCountForWorkers()
        {
            var req = _race == Race.Orc ? UnitTypeId.GreatHall : UnitTypeId.TownHall;
            int h = AiQueries.CountSatisfying(_s, Slot, req, false);
            return h < 1 ? 1 : h;
        }

        void GenHarvest()
        {
            int mine = AiQueries.NearestGoldMine(_s, _anchorX, _anchorY);
            bool haveWoodTile = AiQueries.NearestWoodTile(_s, _anchorX, _anchorY,
                SimConstants.WoodSearchRadius, out int woodX, out int woodY);
            if (mine < 0 && !haveWoodTile)
                return;

            int onGold = 0, onWood = 0;
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player != Slot
                    || !_s.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                    continue;
                if (u.Order == OrderType.Harvest)
                {
                    if ((u.ResourceTarget & AiQueries.WoodTargetFlag) != 0) onWood++;
                    else onGold++;
                }
                else if ((u.Flags & UnitFlags.Hidden) == 0 && u.Order == OrderType.None
                    && _scratchIds.Count < GameCommand.MaxSelection)
                {
                    uint packed = AiQueries.PackedId(_s, i);
                    // Skip workers a build/expand/scout has already earmarked.
                    if (!IsClaimed(packed) && !_genReserved.Contains(packed))
                        _scratchIds.Add(packed);
                }
            }

            int idle = _scratchIds.Count;
            int total = onGold + onWood + idle;
            if (total == 0)
                return;

            ref PlayerState p = ref _s.Players[Slot];
            int wantWood = p.Lumber < Profile.LowTree ? total / 2
                : p.Lumber > Profile.PlentyTree ? 1
                : total / 3;
            if (p.Gold < Profile.LowGold && wantWood > total / 3) wantWood = total / 3;
            if (p.Gold < Profile.MinGold && wantWood > 1) wantWood = 1;
            if (mine < 0) wantWood = total;
            if (!haveWoodTile) wantWood = 0;

            // Idle workers are pure waste — assign them at full harvest weight.
            if (idle > 0)
            {
                int idleToWood = wantWood - onWood;
                if (idleToWood > idle) idleToWood = idle;
                if (idleToWood < 0) idleToWood = 0;
                int idleToGold = idle - idleToWood;

                if (idleToGold > 0 && mine >= 0)
                {
                    var batch = new List<uint>();
                    for (int i = 0; i < idleToGold; i++) batch.Add(_scratchIds[i]);
                    AddCandidate(AiActionKind.HarvestBalance,
                        Util.Score(Profile.WeightHarvest, AiMath.One),
                        AiQueries.Command(CommandOp.Harvest, Slot, batch,
                            targetUnit: AiQueries.PackedId(_s, mine)));
                }
                if (idleToWood > 0)
                {
                    var batch = new List<uint>();
                    for (int i = idle - idleToWood; i < idle; i++) batch.Add(_scratchIds[i]);
                    AddCandidate(AiActionKind.HarvestBalance,
                        Util.Score(Profile.WeightHarvest, AiMath.One),
                        AiQueries.Command(CommandOp.Harvest, Slot, batch,
                            (ushort)woodX, (ushort)woodY));
                }
                return;
            }

            // Otherwise nudge one worker across if the split is meaningfully off.
            // The >=2 deadband is load-bearing: yanking a worker mid-trip on an
            // off-by-one drops its in-progress haul, so a fast-thinking AI that
            // re-balanced every think would thrash its own economy (why cadence 18
            // developed WORSE than 25 before this guard).
            if (onWood + 1 < wantWood && mine >= 0)
            {
                int w = AiQueries.FindHarvestingWorker(_s, Slot, wood: false, _genReserved);
                if (w >= 0 && haveWoodTile)
                    AddCandidate(AiActionKind.HarvestBalance,
                        Util.Score(Profile.WeightHarvest, AiMath.Half),
                        AiQueries.Command(CommandOp.Harvest, Slot,
                            AiQueries.PackedId(_s, w), (ushort)woodX, (ushort)woodY));
            }
            else if (onWood > wantWood + 1 && mine >= 0)
            {
                int w = AiQueries.FindHarvestingWorker(_s, Slot, wood: true, _genReserved);
                if (w >= 0)
                    AddCandidate(AiActionKind.HarvestBalance,
                        Util.Score(Profile.WeightHarvest, AiMath.Half),
                        AiQueries.Command(CommandOp.Harvest, Slot,
                            AiQueries.PackedId(_s, w),
                            targetUnit: AiQueries.PackedId(_s, mine)));
            }
        }

        // ---- Army training ----

        void GenTrain()
        {
            if (_emergency)
                return;
            ref PlayerState p = ref _s.Players[Slot];
            for (int i = 0; i < Profile.Army.Length; i++)
            {
                var want = Profile.Army[i];
                var baseType = AiRaceMap.Unit(want.Unit, _race);
                var effType = TechTree.TrainSubstitute(baseType, p.Researched);
                int have = AiQueries.CountAlive(_s, Slot, (ushort)effType, true)
                    + AiQueries.CountInTraining(_s, Slot, (ushort)effType);
                if (effType != baseType)
                    have += AiQueries.CountAlive(_s, Slot, (ushort)baseType, true)
                        + AiQueries.CountInTraining(_s, Slot, (ushort)baseType);
                if (have >= want.Count)
                    continue;
                if (p.FoodUsed + 1 > p.FoodMax)
                    continue;
                ref UnitTypeData row = ref _s.Rules.Units[(int)effType];
                if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                    continue;
                int trainer = FindIdleTrainerFor(baseType, effType);
                if (trainer < 0)
                    continue;
                int deficit = AiMath.Normalize(want.Count - have, 0, want.Count);
                int score = Util.Score(Profile.WeightArmy, deficit);
                AddCandidate(AiActionKind.TrainUnit, score,
                    AiQueries.Command(CommandOp.Train, Slot,
                        AiQueries.PackedId(_s, trainer), param: (ushort)effType));
            }
        }

        int FindIdleTrainerFor(UnitTypeId baseType, UnitTypeId effType)
        {
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player != Slot || (u.Flags & UnitFlags.Building) == 0
                    || (u.Flags & UnitFlags.UnderConstruction) != 0
                    || u.BuildType != 0 || u.ResearchId != 0)
                    continue;
                var trains = TechTree.Trains((UnitTypeId)u.TypeId);
                for (int t = 0; t < trains.Length; t++)
                    if (trains[t] == baseType
                        && _sim.CanTrainAt(Slot, (UnitTypeId)u.TypeId, effType))
                        return i;
            }
            return -1;
        }

        // ---- Research ----

        void GenResearch()
        {
            if (_emergency)
                return;
            ref PlayerState p = ref _s.Players[Slot];
            for (int gi = 0; gi < Profile.Research.Length; gi++)
            {
                var u = AiRaceMap.Upgrade(Profile.Research[gi], _race);
                if (u == UpgradeId.None || p.HasResearched(u))
                    continue;
                int lab = FindIdleLabFor(u);
                if (lab < 0)
                    return; // provider missing/busy; later goals wait, as before
                ref UpgradeData row = ref _s.Rules.Upgrades[(int)u];
                if (p.Oil < row.Oil)
                    continue;
                if (EffectiveGold() < row.Gold || EffectiveLumber() < row.Lumber)
                    return;
                AddCandidate(AiActionKind.Research,
                    Util.Score(Profile.WeightResearch, AiMath.One),
                    AiQueries.Command(CommandOp.Research, Slot,
                        AiQueries.PackedId(_s, lab), param: (ushort)u));
                return;
            }
        }

        int FindIdleLabFor(UpgradeId u)
        {
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit b = ref _s.Units[i];
                if (!b.IsAlive || b.Player != Slot || (b.Flags & UnitFlags.Building) == 0
                    || (b.Flags & UnitFlags.UnderConstruction) != 0
                    || b.BuildType != 0 || b.ResearchId != 0)
                    continue;
                var offered = TechTree.Research((UnitTypeId)b.TypeId);
                for (int o = 0; o < offered.Length; o++)
                    if (offered[o] == u && _sim.CanResearchAt(Slot, (UnitTypeId)b.TypeId, u))
                        return i;
            }
            return -1;
        }
    }
}
