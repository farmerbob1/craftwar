namespace Craftwar.Sim
{
    public sealed partial class GameSim
    {
        const uint WoodTargetFlag = 0x80000000;

        unsafe void ApplyEconomyCommand(in GameCommand cmd)
        {
            switch (cmd.Op)
            {
                case CommandOp.Harvest:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]), out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player ||
                            !State.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                            continue;
                        u.Order = OrderType.Harvest;
                        u.AttackTarget = 0;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                        u.Timer = 0;
                        // Park the walk order on our own tile (or the step in
                        // flight): TickMovement runs before TickHarvest picks
                        // the real target, and must not chase the stale OrderX/Y.
                        u.OrderX = (ushort)(u.TileX + u.StepDX);
                        u.OrderY = (ushort)(u.TileY + u.StepDY);
                        if (cmd.TargetUnit != 0)
                        {
                            u.ResourceTarget = cmd.TargetUnit;
                            u.Harvest = HarvestStage.ToMine;
                        }
                        else
                        {
                            u.ResourceTarget = WoodTargetFlag
                                | (uint)(cmd.TargetY * State.Terrain.Width + cmd.TargetX);
                            u.Harvest = HarvestStage.ToWood;
                        }
                    }
                    break;

                case CommandOp.Build:
                    // The order must be on the player's race build menu and
                    // pass the tech/ALOW gate before a worker even walks out.
                    if (cmd.Param >= UdtaParser.UnitCount
                        || !OnWorkerBuildMenu(cmd.Player, (UnitTypeId)cmd.Param)
                        || !CanProduce(cmd.Player, (UnitTypeId)cmd.Param))
                    {
                        Emit(SimEventKind.CommandDenied, cmd.Player,
                            (ushort)DenyReason.TechUnavailable, cmd.Param);
                        break;
                    }
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]), out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player ||
                            !State.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                            continue;
                        u.Order = OrderType.Build;
                        u.BuildType = (ushort)(cmd.Param + 1); // 1-based, 0 = idle
                        u.OrderX = cmd.TargetX;
                        u.OrderY = cmd.TargetY;
                        u.AttackTarget = 0;
                        u.Harvest = HarvestStage.None;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                        break; // one builder per site
                    }
                    break;

                case CommandOp.Train:
                    // Trains a unit — or, when Param is a building type, starts
                    // the building's own tier upgrade (Hall→Keep, tower guns).
                    if (cmd.Param >= UdtaParser.UnitCount)
                        break;
                {
                    // One CommandDenied per command, not per candidate: keep the
                    // first real reason we hit and report it only if no building
                    // in the selection ends up taking the order.
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
                        var want = (UnitTypeId)cmd.Param;
                        ref UnitTypeData row = ref State.Rules.Units[cmd.Param];
                        bool selfUpgrade = row.Is(UnitTypeFlags.Building);
                        bool ok = selfUpgrade
                            ? CanUpgradeBuildingTo(b.Player, (UnitTypeId)b.TypeId, want)
                            : CanTrainAt(b.Player, (UnitTypeId)b.TypeId, want);
                        if (!ok)
                        {
                            if (deny == DenyReason.None)
                                deny = DenyReason.TechUnavailable;
                            continue;
                        }
                        ref PlayerState p = ref State.Players[b.Player];
                        var short_ = ShortfallFor(ref p, row.GoldCost, row.LumberCost, row.OilCost,
                            needsFood: !selfUpgrade);
                        if (short_ != DenyReason.None)
                        {
                            if (deny == DenyReason.None || deny == DenyReason.Busy
                                || deny == DenyReason.TechUnavailable)
                                deny = short_;
                            continue;
                        }
                        p.Gold -= row.GoldCost;
                        p.Lumber -= row.LumberCost;
                        p.Oil -= row.OilCost;
                        b.BuildType = (ushort)(cmd.Param + 1); // 1-based, 0 = idle
                        b.TrainTicks = BuildTicksFor(row.BuildTime);
                        taken = true; // one building takes the order
                    }
                    if (!taken && deny != DenyReason.None)
                        Emit(SimEventKind.CommandDenied, cmd.Player, (ushort)deny, cmd.Param);
                }
                    break;

                case CommandOp.SetRally:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]), out int idx))
                            continue;
                        ref Unit b = ref State.Units[idx];
                        if (b.Player != cmd.Player || (b.Flags & UnitFlags.Building) == 0)
                            continue;
                        b.RallyX = cmd.TargetX;
                        b.RallyY = cmd.TargetY;
                    }
                    break;
            }
        }

        bool OnWorkerBuildMenu(byte player, UnitTypeId type)
        {
            var menu = TechTree.WorkerBuildings(State.Players[player].Race);
            for (int i = 0; i < menu.Length; i++)
                if (menu[i] == type)
                    return true;
            return false;
        }

        /// <summary>
        /// Which resource a player is short of, in a fixed check order so the
        /// reported reason is deterministic. None = affordable.
        /// </summary>
        static DenyReason ShortfallFor(ref PlayerState p, int gold, int lumber, int oil, bool needsFood)
        {
            if (p.Gold < gold)
                return DenyReason.NotEnoughGold;
            if (p.Lumber < lumber)
                return DenyReason.NotEnoughLumber;
            if (p.Oil < oil)
                return DenyReason.NotEnoughOil;
            if (needsFood && p.FoodUsed + 1 > p.FoodMax)
                return DenyReason.NotEnoughFood;
            return DenyReason.None;
        }

        /// <summary>UDTA build time: 6 units = 1 second -> ticks at 50/s.
        /// Public so the UI can turn a TrainTicks countdown into a progress
        /// fraction from the same formula.</summary>
        public static ushort BuildTicksFor(int buildTime) => (ushort)(buildTime * 50 / 6);

        // ------------------------------------------------------------------
        // Production: building construction progress + training queues + food.
        // ------------------------------------------------------------------
        void TickProduction()
        {
            if (State.Terrain == null)
                return;

            if (State.Tick % 10 == 0)
                RecountFood();

            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit b = ref State.Units[i];
                if (!b.IsAlive)
                    continue;

                // Berserker regeneration (staggered by slot for smoothness).
                if (b.TypeId == (ushort)UnitTypeId.Berserker
                    && b.Player < SimConstants.MaxPlayers
                    && (State.Tick + i) % SimConstants.RegenPeriodTicks == 0
                    && State.Players[b.Player].HasResearched(UpgradeId.BerserkerRegeneration)
                    && b.Hp < State.Rules.Units[b.TypeId].Hp)
                    b.Hp++;

                if ((b.Flags & UnitFlags.Building) == 0)
                    continue;

                if ((b.Flags & UnitFlags.UnderConstruction) != 0)
                {
                    ref UnitTypeData row = ref State.Rules.Units[b.TypeId];
                    int total = BuildTicksFor(row.BuildTime);
                    if (b.TrainTicks > 0)
                    {
                        b.TrainTicks--;
                        // HP ramps from ~10% to full across the build.
                        int target = row.Hp - (row.Hp * b.TrainTicks / (total == 0 ? 1 : total));
                        if (target > b.Hp)
                            b.Hp = target;
                    }
                    if (b.TrainTicks == 0)
                    {
                        b.Flags &= ~UnitFlags.UnderConstruction;
                        b.Hp = row.Hp;
                        ReleaseBuilder(ref b, i);
                        Emit(SimEventKind.ConstructionComplete, b.Player, 0, b.TypeId,
                            new UnitId((ushort)i, b.Gen).Packed);
                    }
                    continue;
                }

                if (b.ResearchId != 0)
                {
                    if (b.TrainTicks > 0)
                        b.TrainTicks--;
                    if (b.TrainTicks == 0)
                    {
                        var done = (UpgradeId)(b.ResearchId - 1);
                        b.ResearchId = 0;
                        CompleteResearch(b.Player, done);
                        Emit(SimEventKind.ResearchComplete, b.Player, 0, (ushort)done,
                            new UnitId((ushort)i, b.Gen).Packed);
                    }
                    continue;
                }

                if (b.BuildType != 0)
                {
                    if (b.TrainTicks > 0)
                        b.TrainTicks--;
                    if (b.TrainTicks == 0)
                    {
                        ushort trained = (ushort)(b.BuildType - 1); // decode 1-based
                        b.BuildType = 0;
                        if (State.Rules.Units[trained].Is(UnitTypeFlags.Building))
                        {
                            UpgradeBuildingType(ref b, trained);
                            Emit(SimEventKind.UpgradeComplete, b.Player, 0, trained,
                                new UnitId((ushort)i, b.Gen).Packed);
                        }
                        else if (TryFindSpawnTile(ref b, out int sx, out int sy))
                        {
                            var id = State.SpawnUnit(trained, b.Player, (ushort)sx, (ushort)sy);
                            if (State.TryGetUnitIndex(id, out int ui))
                            {
                                ref Unit nu = ref State.Units[ui];
                                nu.Hp = State.Rules.Units[trained].Hp;
                                nu.Facing = 4; // south, fresh out the door
                                if (b.RallyX != 0 || b.RallyY != 0)
                                {
                                    nu.Order = OrderType.Move;
                                    nu.OrderX = b.RallyX;
                                    nu.OrderY = b.RallyY;
                                }
                            }
                            Emit(SimEventKind.TrainComplete, b.Player, 0, trained,
                                new UnitId((ushort)i, b.Gen).Packed);
                        }
                        else
                        {
                            b.BuildType = (ushort)(trained + 1); // blocked exits: retry (re-encode)
                            b.TrainTicks = 25;
                        }
                    }
                }
            }
        }

        void RecountFood()
        {
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                State.Players[p].FoodMax = 0;
                State.Players[p].FoodUsed = 0;
            }
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive || u.Player >= SimConstants.MaxPlayers)
                    continue;
                ref PlayerState p = ref State.Players[u.Player];
                if ((u.Flags & UnitFlags.Building) != 0)
                {
                    if ((u.Flags & UnitFlags.UnderConstruction) != 0)
                        continue;
                    var t = (UnitTypeId)u.TypeId;
                    if (t == UnitTypeId.Farm || t == UnitTypeId.PigFarm)
                        p.FoodMax += SimConstants.FoodPerFarm;
                    else if (t is UnitTypeId.TownHall or UnitTypeId.Keep or UnitTypeId.Castle
                        or UnitTypeId.GreatHall or UnitTypeId.Stronghold or UnitTypeId.Fortress)
                        p.FoodMax += 1;
                }
                else
                {
                    p.FoodUsed += 1;
                }
            }
        }

        /// <summary>First free land tile in a ring around the footprint.</summary>
        bool TryFindSpawnTile(ref Unit b, out int sx, out int sy) =>
            TryFindSpawnTileNear(ref b, b.TileX, b.TileY, out sx, out sy);

        /// <summary>Free ring tile closest to (prefX, prefY) — exits face the destination.</summary>
        bool TryFindSpawnTileNear(ref Unit b, int prefX, int prefY, out int sx, out int sy)
        {
            int size = State.Footprint(b.TypeId);
            for (int ring = 1; ring <= 3; ring++)
            {
                int bestDist = int.MaxValue;
                sx = sy = 0;
                for (int dy = -ring; dy <= size - 1 + ring; dy++)
                {
                    for (int dx = -ring; dx <= size - 1 + ring; dx++)
                    {
                        // ring edge only
                        if (dx > -ring && dx < size - 1 + ring && dy > -ring && dy < size - 1 + ring)
                            continue;
                        int x = b.TileX + dx, y = b.TileY + dy;
                        if (!State.Terrain.IsPassable(MoveDomain.Land, x, y)
                            || State.OccupancySurface[y * State.Terrain.Width + x] != 0)
                            continue;
                        int dist = Chebyshev(x, y, prefX, prefY);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            sx = x;
                            sy = y;
                        }
                    }
                }
                if (bestDist != int.MaxValue)
                    return true;
            }
            sx = sy = 0;
            return false;
        }

        void ReleaseBuilder(ref Unit building, int buildingIndex)
        {
            // The peasant hidden inside pops out next to the finished building.
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive || (u.Flags & UnitFlags.Hidden) == 0)
                    continue;
                if (u.Order != OrderType.Build || u.OrderX != building.TileX || u.OrderY != building.TileY)
                    continue;
                if (TryFindSpawnTile(ref building, out int sx, out int sy))
                {
                    u.Flags &= ~UnitFlags.Hidden;
                    u.TileX = (ushort)sx;
                    u.TileY = (ushort)sy;
                    u.PixX = sx * SimConstants.TilePixels;
                    u.PixY = sy * SimConstants.TilePixels;
                    u.Order = OrderType.None;
                    State.Occupy(new UnitId((ushort)i, u.Gen), u.TypeId, sx, sy);
                }
                return;
            }
        }

        // ------------------------------------------------------------------
        // Harvest + construction-walk per tick.
        // ------------------------------------------------------------------
        void TickHarvest()
        {
            if (State.Terrain == null)
                return;
            int w = State.Terrain.Width;

            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive)
                    continue;

                if (u.Order == OrderType.Build && (u.Flags & UnitFlags.Hidden) == 0)
                {
                    TickBuilderWalk(ref u, i);
                    continue;
                }
                if (u.Order == OrderType.Repair)
                {
                    TickRepair(ref u, i);
                    continue;
                }
                if (u.Order != OrderType.Harvest)
                    continue;

                switch (u.Harvest)
                {
                    case HarvestStage.ToMine:
                    {
                        if (!State.TryGetUnitIndex(UnitId.FromPacked(u.ResourceTarget), out int mi))
                        {
                            EndHarvest(ref u);
                            break;
                        }
                        ref Unit mine = ref State.Units[mi];
                        if (u.StepRemaining == 0 && FootprintDistance(ref u, ref mine) <= 1)
                        {
                            HideUnit(ref u, i);
                            u.Harvest = HarvestStage.InMine;
                            u.Timer = SimConstants.InMineTicks;
                        }
                        else
                        {
                            WalkToBuilding(ref u, ref mine);
                        }
                        break;
                    }

                    case HarvestStage.InMine:
                    {
                        if (u.Timer > 0) { u.Timer--; break; }
                        if (State.TryGetUnitIndex(UnitId.FromPacked(u.ResourceTarget), out int mi))
                        {
                            ref Unit mine = ref State.Units[mi];
                            mine.ResourceAmount -= SimConstants.CarryAmount;
                            u.Carry = CarryType.Gold;
                            // Exit on the side facing the drop-off.
                            int depot = FindDepot(ref u);
                            UnhideNear(ref u, i, mi,
                                depot >= 0 ? State.Units[depot].TileX : u.TileX,
                                depot >= 0 ? State.Units[depot].TileY : u.TileY);
                            if (mine.ResourceAmount <= 0)
                            {
                                State.DestroyUnit(new UnitId((ushort)mi, mine.Gen)); // mine collapses
                                Emit(SimEventKind.MineCollapsed, u.Player, 0, mine.TypeId);
                            }
                        }
                        u.Harvest = HarvestStage.ToDepot;
                        u.PathLength = 0;
                        break;
                    }

                    case HarvestStage.ToWood:
                    {
                        int tile = (int)(u.ResourceTarget & ~WoodTargetFlag);
                        int tx = tile % w, ty = tile / w;
                        if (!State.Terrain.HasWood(tx, ty))
                        {
                            // Tree gone (felled by us or someone else): next
                            // tree near the OLD one — the original's saved
                            // location — so return trips go back to the same
                            // forest instead of searching around the depot.
                            if (FindNearbyWood(tx, ty, out int nx, out int ny)
                                || FindNearbyWood(u.TileX, u.TileY, out nx, out ny))
                            {
                                u.ResourceTarget = WoodTargetFlag | (uint)(ny * w + nx);
                                u.Timer = 0;
                            }
                            else
                            {
                                EndHarvest(ref u);
                            }
                            break;
                        }
                        // Only start chopping once fully on a tile: mid-step
                        // the unit still occupies TileX/Y, and parking the
                        // order there would drag it a tile backwards.
                        if (u.StepRemaining == 0 && Chebyshev(u.TileX, u.TileY, tx, ty) <= 1)
                        {
                            u.Harvest = HarvestStage.Chopping;
                            u.Timer = SimConstants.ChopTicks;
                            u.PathLength = 0;
                            // Park the movement order on our own tile so the
                            // mover doesn't keep pathing at the tree.
                            u.OrderX = u.TileX;
                            u.OrderY = u.TileY;
                            u.Facing = FacingFrom(Sign(tx - u.TileX), Sign(ty - u.TileY));
                        }
                        else
                        {
                            WalkTo(ref u, (ushort)tx, (ushort)ty);
                            // Walled-off tree (e.g. forest behind a mine row):
                            // movement can't produce a path, so the peon would
                            // stand forever. Count the standstill, retarget a
                            // tree near where we stand, and finally give up.
                            if (u.StepRemaining == 0 && u.PathCursor >= u.PathLength)
                            {
                                if (++u.Timer >= SimConstants.WoodStuckTicks)
                                {
                                    u.Timer = 0;
                                    if (FindNearbyWood(u.TileX, u.TileY, out int nx, out int ny)
                                        && ny * w + nx != tile)
                                        u.ResourceTarget = WoodTargetFlag | (uint)(ny * w + nx);
                                    else
                                        EndHarvest(ref u);
                                }
                            }
                            else
                            {
                                u.Timer = 0;
                            }
                        }
                        break;
                    }

                    case HarvestStage.Chopping:
                    {
                        int tile = (int)(u.ResourceTarget & ~WoodTargetFlag);
                        int tx = tile % w, ty = tile / w;
                        if (!State.Terrain.HasWood(tx, ty))
                        {
                            u.Harvest = HarvestStage.ToWood; // someone else felled it
                            break;
                        }
                        if (u.Timer > 0) { u.Timer--; break; }
                        State.Terrain.Chop(tx, ty);
                        RetileForestAround(tx, ty);
                        u.Carry = CarryType.Wood;
                        u.Harvest = HarvestStage.ToDepot;
                        u.PathLength = 0;
                        break;
                    }

                    case HarvestStage.ToDepot:
                    {
                        int depot = FindDepot(ref u);
                        if (depot < 0)
                        {
                            EndHarvest(ref u);
                            break;
                        }
                        ref Unit d = ref State.Units[depot];
                        if (u.StepRemaining == 0 && FootprintDistance(ref u, ref d) <= 1)
                        {
                            HideUnit(ref u, i);
                            u.Harvest = HarvestStage.InDepot;
                            u.Timer = SimConstants.InDepotTicks;
                            u.ChaseX = (ushort)depot; // remember depot slot for bonus
                        }
                        else
                        {
                            WalkToBuilding(ref u, ref d);
                        }
                        break;
                    }

                    case HarvestStage.InDepot:
                    {
                        if (u.Timer > 0) { u.Timer--; break; }
                        Deposit(ref u);
                        int depotSlot = u.ChaseX;
                        u.Carry = CarryType.None;
                        // Exit facing the resource we are returning to.
                        int prefX = u.TileX, prefY = u.TileY;
                        if ((u.ResourceTarget & WoodTargetFlag) != 0)
                        {
                            int tile = (int)(u.ResourceTarget & ~WoodTargetFlag);
                            prefX = tile % State.Terrain.Width;
                            prefY = tile / State.Terrain.Width;
                        }
                        else if (State.TryGetUnitIndex(UnitId.FromPacked(u.ResourceTarget), out int rmi))
                        {
                            prefX = State.Units[rmi].TileX;
                            prefY = State.Units[rmi].TileY;
                        }
                        UnhideNear(ref u, i, depotSlot, prefX, prefY);
                        // Loop back for the next trip.
                        if ((u.ResourceTarget & WoodTargetFlag) != 0)
                        {
                            u.Harvest = HarvestStage.ToWood;
                        }
                        else if (State.TryGetUnitIndex(UnitId.FromPacked(u.ResourceTarget), out _))
                        {
                            u.Harvest = HarvestStage.ToMine;
                        }
                        else
                        {
                            EndHarvest(ref u);
                        }
                        u.PathLength = 0;
                        break;
                    }
                }
            }
        }

        void Deposit(ref Unit u)
        {
            if (u.Player >= SimConstants.MaxPlayers)
                return;
            ref PlayerState p = ref State.Players[u.Player];
            int amount = SimConstants.CarryAmount;
            var depotType = u.ChaseX < State.HighestUnitIndex && State.Units[u.ChaseX].IsAlive
                ? (UnitTypeId)State.Units[u.ChaseX].TypeId : UnitTypeId.None;
            if (u.Carry == CarryType.Gold)
            {
                if (depotType is UnitTypeId.Keep or UnitTypeId.Stronghold)
                    amount += amount * SimConstants.KeepFactorPct / 100;
                else if (depotType is UnitTypeId.Castle or UnitTypeId.Fortress)
                    amount += amount * SimConstants.CastleFactorPct / 100;
                p.Gold += amount;
            }
            else if (u.Carry == CarryType.Wood)
            {
                if (depotType is UnitTypeId.ElvenLumberMill or UnitTypeId.TrollLumberMill)
                    amount += amount * SimConstants.MillFactorPct / 100;
                p.Lumber += amount;
            }
        }

        int FindDepot(ref Unit u)
        {
            // Halls (GoldDepot) accept wood too — the original hardcodes this;
            // the UDTA wood-storage bit is only set on lumber mills.
            UnitTypeFlags need = u.Carry == CarryType.Wood
                ? UnitTypeFlags.LumberDepot | UnitTypeFlags.GoldDepot
                : UnitTypeFlags.GoldDepot;
            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit d = ref State.Units[i];
                if (!d.IsAlive || d.Player != u.Player
                    || (d.Flags & UnitFlags.Building) == 0
                    || (d.Flags & UnitFlags.UnderConstruction) != 0
                    || !State.Rules.Units[d.TypeId].Is(need))
                    continue;
                int dist = FootprintDistance(ref u, ref d);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        bool FindNearbyWood(int cx, int cy, out int wx, out int wy)
        {
            for (int r = 1; r <= SimConstants.WoodSearchRadius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx > -r && dx < r && dy > -r && dy < r)
                            continue;
                        if (State.Terrain.HasWood(cx + dx, cy + dy))
                        {
                            wx = cx + dx;
                            wy = cy + dy;
                            return true;
                        }
                    }
            wx = wy = 0;
            return false;
        }

        static int Chebyshev(int ax, int ay, int bx, int by)
        {
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            return dx > dy ? dx : dy;
        }

        void WalkTo(ref Unit u, ushort tx, ushort ty)
        {
            if (u.OrderX != tx || u.OrderY != ty)
            {
                u.OrderX = tx;
                u.OrderY = ty;
                u.PathLength = 0;
                u.PathCursor = 0;
            }
        }

        /// <summary>
        /// Walk toward the NEAREST edge of a building's footprint instead of
        /// its top-left tile — kills the "circle around the mine" look.
        /// </summary>
        void WalkToBuilding(ref Unit u, ref Unit b)
        {
            int size = State.Footprint(b.TypeId);
            ushort tx = (ushort)ClampTo(u.TileX, b.TileX, b.TileX + size - 1);
            ushort ty = (ushort)ClampTo(u.TileY, b.TileY, b.TileY + size - 1);
            WalkTo(ref u, tx, ty);
        }

        void HideUnit(ref Unit u, int index)
        {
            var id = new UnitId((ushort)index, u.Gen);
            // A mid-step unit has already reserved its step destination;
            // release it too or the tile stays blocked forever.
            if (u.StepRemaining > 0)
                State.Vacate(id, u.TypeId, u.TileX + u.StepDX, u.TileY + u.StepDY);
            State.Vacate(id, u.TypeId, u.TileX, u.TileY);
            u.Flags |= UnitFlags.Hidden;
            u.PathLength = 0;
            u.StepRemaining = 0;
            u.StepDX = 0;
            u.StepDY = 0;
        }

        void UnhideNear(ref Unit u, int index, int nearSlot, int prefX, int prefY)
        {
            ref Unit host = ref State.Units[nearSlot];
            if (TryFindSpawnTileNear(ref host, prefX, prefY, out int sx, out int sy))
            {
                u.TileX = (ushort)sx;
                u.TileY = (ushort)sy;
            }
            u.PixX = u.TileX * SimConstants.TilePixels;
            u.PixY = u.TileY * SimConstants.TilePixels;
            // Park the walk order on the exit tile: movement runs before the
            // harvest stage picks the next destination, and following the
            // pre-hide order for even one step walks the wrong way.
            u.OrderX = u.TileX;
            u.OrderY = u.TileY;
            u.Flags &= ~UnitFlags.Hidden;
            State.Occupy(new UnitId((ushort)index, u.Gen), u.TypeId, u.TileX, u.TileY);
        }

        void EndHarvest(ref Unit u)
        {
            u.Order = OrderType.None;
            u.Harvest = HarvestStage.None;
            u.PathLength = 0;
        }

        void TickBuilderWalk(ref Unit u, int index)
        {
            // Walking to the construction site; on arrival validate + erect.
            ushort buildType = (ushort)(u.BuildType - 1); // decode 1-based
            int size = State.Footprint(buildType);
            int dist = Chebyshev(u.TileX, u.TileY,
                ClampTo(u.TileX, u.OrderX, u.OrderX + size - 1),
                ClampTo(u.TileY, u.OrderY, u.OrderY + size - 1));
            if (dist > 1)
                return; // movement system keeps walking toward OrderX/Y

            ref PlayerState p = ref State.Players[u.Player];
            ref UnitTypeData row = ref State.Rules.Units[buildType];
            // Re-check the tech gate on arrival — a prereq may have died mid-walk.
            var shortfall = ShortfallFor(ref p, row.GoldCost, row.LumberCost, row.OilCost,
                needsFood: false);
            bool affordable = shortfall == DenyReason.None;
            bool allowed = CanProduce(u.Player, (UnitTypeId)buildType);
            bool ok = affordable && allowed;
            if (ok)
            {
                for (int dy = 0; dy < size && ok; dy++)
                    for (int dx = 0; dx < size && ok; dx++)
                    {
                        int x = u.OrderX + dx, y = u.OrderY + dy;
                        uint occ = State.Terrain.InBounds(x, y)
                            ? State.OccupancySurface[y * State.Terrain.Width + x] : 1u;
                        bool self = occ == new UnitId((ushort)index, u.Gen).Packed;
                        if (!State.Terrain.IsPassable(MoveDomain.Land, x, y) || (occ != 0 && !self))
                            ok = false;
                    }
            }
            if (!ok)
            {
                // The order is dropped either way; tell the UI which wall it hit.
                if (!affordable)
                    Emit(SimEventKind.CommandDenied, u.Player, (ushort)shortfall, buildType);
                else if (!allowed)
                    Emit(SimEventKind.CommandDenied, u.Player,
                        (ushort)DenyReason.TechUnavailable, buildType);
                else
                    Emit(SimEventKind.BuildSiteBlocked, u.Player, 0, buildType);
                u.Order = OrderType.None;
                u.BuildType = 0;
                u.PathLength = 0;
                return;
            }

            p.Gold -= row.GoldCost;
            p.Lumber -= row.LumberCost;
            p.Oil -= row.OilCost;
            HideUnit(ref u, index);

            var id = State.SpawnUnit(buildType, u.Player, u.OrderX, u.OrderY);
            if (State.TryGetUnitIndex(id, out int bi))
            {
                ref Unit b = ref State.Units[bi];
                b.Flags |= UnitFlags.Building | UnitFlags.UnderConstruction;
                b.Hp = row.Hp / 10 == 0 ? 1 : row.Hp / 10;
                b.TrainTicks = BuildTicksFor(row.BuildTime);
            }
        }

        static int ClampTo(int v, int min, int max) => v < min ? min : v > max ? max : v;

        /// <summary>
        /// Recompute forest art around a felled tree (the original retiles the
        /// same 3x3 window — TILE.C tile_finish_tree_harvest).
        ///
        /// Corner-vertex model matching the PUD boundary encoding (0x07SV,
        /// pud_format.txt Appendix D: S says which part of the tile shows
        /// forest): a tile-corner vertex is forest only while ALL four tiles
        /// sharing it still hold wood. Transitions are therefore drawn on the
        /// forest side, and non-wood terrain is never repainted.
        ///
        /// Remnants the boundary shapes can't draw use the tileset's special
        /// megatiles: 1-wide vertical strips become one-tree column pieces,
        /// and anything else (lone trees, 1-tall rows) loses its wood
        /// entirely, leaving stumps — the original does the same (the PSX
        /// F_TREE.BIN table flattens them to the stump matrix). Each removal
        /// ripples outward through the worklist.
        /// </summary>
        void RetileForestAround(int cx, int cy)
        {
            var work = new System.Collections.Generic.List<(int x, int y)> { (cx, cy) };
            for (int i = 0; i < work.Count; i++)
                RetileForestWindow(work[i].x, work[i].y, work);
        }

        void RetileForestWindow(int cx, int cy, System.Collections.Generic.List<(int x, int y)> work)
        {
            int w = State.Terrain.Width, h = State.Terrain.Height;
            // Off-map counts as wood so forests stay solid at the border.
            bool Wood(int x, int y) =>
                x < 0 || y < 0 || x >= w || y >= h || State.Terrain.HasWood(x, y);
            // Vertex (vx,vy) = shared corner of the four tiles whose
            // top-left tile is (vx-1, vy-1).
            bool Vert(int vx, int vy) =>
                Wood(vx - 1, vy - 1) && Wood(vx, vy - 1) && Wood(vx - 1, vy) && Wood(vx, vy);

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h)
                        continue;
                    bool hasWood = State.Terrain.HasWood(x, y);
                    bool isCenter = x == cx && y == cy;
                    if (!hasWood && !isCenter)
                        continue; // only the felled tile and live forest repaint

                    int bits = 0;
                    if (Vert(x, y)) bits |= 1;             // UL corner
                    if (Vert(x + 1, y)) bits |= 2;         // UR
                    if (Vert(x, y + 1)) bits |= 4;         // LL
                    if (Vert(x + 1, y + 1)) bits |= 8;     // LR

                    ushort cur = State.Tiles[y * w + x];
                    ushort id;
                    if (!hasWood)
                    {
                        // A non-wood tile shares all four of its own vertices,
                        // so the felled center always has bits == 0: stumps.
                        id = SimConstants.ChoppedTileId;
                    }
                    else if (bits == 15)
                    {
                        // Deep forest: solid tree tile (keep authored variation).
                        id = (cur & 0xFFF0) == 0x0070 ? cur : (ushort)(0x0070 + (x + y) % 3);
                    }
                    else if (bits != 0)
                    {
                        id = bits switch
                        {
                            // Appendix D shapes: filled = forest.
                            1 => 0x0700,   // UL
                            2 => 0x0710,   // UR
                            3 => 0x0720,   // upper half
                            4 => 0x0730,   // LL
                            5 => 0x0740,   // left half
                            6 => 0x0750,   // UR+LL (clear UL/LR)
                            7 => 0x0760,   // clear LR
                            8 => 0x0770,   // LR
                            9 => 0x0780,   // UL+LR
                            10 => 0x0790,  // right half (clear left)
                            11 => 0x07A0,  // clear LL
                            12 => 0x07B0,  // lower half (clear upper)
                            13 => 0x07C0,  // clear UR
                            _ => 0x07D0,   // 14: clear UL
                        };
                        // Same shape group -> keep the authored variation.
                        if ((cur & 0xFFF0) == (id & 0xFFF0))
                            id = cur;
                    }
                    else
                    {
                        // Wood with no forest corners: a remnant. Vertical
                        // single-file trees have dedicated column art; anything
                        // else can't be drawn and is removed (no lumber).
                        bool woodN = State.Terrain.HasWood(x, y - 1);
                        bool woodS = State.Terrain.HasWood(x, y + 1);
                        if (woodN && woodS)
                        {
                            id = SimConstants.OneTreeMidTileId;
                        }
                        else if (woodS)
                        {
                            id = SimConstants.OneTreeTopTileId;
                        }
                        else if (woodN)
                        {
                            id = SimConstants.OneTreeBotTileId;
                        }
                        else
                        {
                            State.Terrain.Chop(x, y);
                            id = SimConstants.ChoppedTileId;
                            work.Add((x, y)); // removal ripples outward
                        }
                    }

                    if (cur != id)
                    {
                        State.Tiles[y * w + x] = id;
                        State.TileChanges.Add(((ushort)x, (ushort)y, id));
                    }
                }
            }
        }
    }
}
