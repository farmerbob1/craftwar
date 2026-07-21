namespace Craftwar.Sim.Ai
{
    public sealed partial class AiPlayer
    {
        /// <summary>Worker training toward the phase target, then gold/wood
        /// crew balancing around the original's thresholds.</summary>
        void ThinkEconomy()
        {
            if (!_emergency)
                TrainWorker();
            if (_budget > 0)
                AssignHarvesters();
        }

        void TrainWorker()
        {
            var phase = AiScript.Phase(_phase);
            ushort workerType = (ushort)AiRaceMap.Unit(AiUnit.Worker, _race);
            int have = AiQueries.CountAlive(_s, Slot, workerType, includeUnderConstruction: true)
                + AiQueries.CountInTraining(_s, Slot, workerType);
            if (have >= phase.WorkerTarget)
                return;

            ref PlayerState p = ref _s.Players[Slot];
            if (p.FoodUsed + 1 > p.FoodMax)
                return; // the farm manager reacts to the same pressure
            ref UnitTypeData row = ref _s.Rules.Units[workerType];
            if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                return;

            var hall = _race == Race.Orc ? UnitTypeId.GreatHall : UnitTypeId.TownHall;
            int b = AiQueries.FindIdleBuilding(_s, Slot, hall);
            if (b < 0)
                return;
            Emit(AiQueries.Command(CommandOp.Train, Slot, AiQueries.PackedId(_s, b),
                param: workerType));
        }

        /// <summary>
        /// Split the worker crew between gold and wood. Desired wood share
        /// follows the original's thresholds: starving for lumber puts half
        /// the crew on trees, plenty leaves one token chopper, and a gold
        /// shortage pulls the split back toward the mine. Idle workers are
        /// batched into single commands; at most one already-working crew
        /// member is retasked per think so the split converges without thrash.
        /// </summary>
        void AssignHarvesters()
        {
            int mine = AiQueries.NearestGoldMine(_s, _anchorX, _anchorY);
            bool haveWoodTile = AiQueries.NearestWoodTile(_s, _anchorX, _anchorY,
                SimConstants.WoodSearchRadius, out int woodX, out int woodY);
            if (mine < 0 && !haveWoodTile)
                return;

            int onGold = 0, onWood = 0;
            _scratchIds.Clear(); // idle workers
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player != Slot
                    || !_s.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                    continue;
                if (u.Order == OrderType.Harvest)
                {
                    if ((u.ResourceTarget & AiQueries.WoodTargetFlag) != 0)
                        onWood++;
                    else
                        onGold++;
                }
                else if ((u.Flags & UnitFlags.Hidden) == 0 && u.Order == OrderType.None
                    && _scratchIds.Count < GameCommand.MaxSelection)
                {
                    uint packed = AiQueries.PackedId(_s, i);
                    if (!IsClaimed(packed))
                        _scratchIds.Add(packed);
                }
            }

            int idle = _scratchIds.Count;
            int total = onGold + onWood + idle;
            if (total == 0)
                return;

            ref PlayerState p = ref _s.Players[Slot];
            int wantWood = p.Lumber < AiScript.LowTree ? total / 2
                : p.Lumber > AiScript.PlentyTree ? 1
                : total / 3;
            if (p.Gold < AiScript.LowGold && wantWood > total / 3)
                wantWood = total / 3;
            if (p.Gold < AiScript.MinGold && wantWood > 1)
                wantWood = 1;
            if (mine < 0)
                wantWood = total; // no mine left: everyone chops
            if (!haveWoodTile)
                wantWood = 0;     // and with no wood either, everyone mines

            // Idle workers first: one batched command per destination.
            if (idle > 0)
            {
                int idleToWood = wantWood - onWood;
                if (idleToWood > idle)
                    idleToWood = idle;
                if (idleToWood < 0)
                    idleToWood = 0;
                int idleToGold = idle - idleToWood;

                if (idleToGold > 0 && mine >= 0 && _budget > 0)
                {
                    var batch = new System.Collections.Generic.List<uint>();
                    for (int i = 0; i < idleToGold; i++)
                        batch.Add(_scratchIds[i]);
                    Emit(AiQueries.Command(CommandOp.Harvest, Slot, batch,
                        targetUnit: AiQueries.PackedId(_s, mine)));
                }
                if (idleToWood > 0 && _budget > 0)
                {
                    var batch = new System.Collections.Generic.List<uint>();
                    for (int i = idle - idleToWood; i < idle; i++)
                        batch.Add(_scratchIds[i]);
                    Emit(AiQueries.Command(CommandOp.Harvest, Slot, batch,
                        (ushort)woodX, (ushort)woodY));
                }
                return; // re-balance the working crew next think
            }

            // No idles: nudge one worker across if the split is off.
            if (onWood < wantWood && mine >= 0 && _budget > 0)
            {
                int w = AiQueries.FindHarvestingWorker(_s, Slot, wood: false, _claimed);
                if (w >= 0 && haveWoodTile)
                    Emit(AiQueries.Command(CommandOp.Harvest, Slot,
                        AiQueries.PackedId(_s, w), (ushort)woodX, (ushort)woodY));
            }
            else if (onWood > wantWood && mine >= 0 && _budget > 0)
            {
                int w = AiQueries.FindHarvestingWorker(_s, Slot, wood: true, _claimed);
                if (w >= 0)
                    Emit(AiQueries.Command(CommandOp.Harvest, Slot,
                        AiQueries.PackedId(_s, w),
                        targetUnit: AiQueries.PackedId(_s, mine)));
            }
        }
    }
}
