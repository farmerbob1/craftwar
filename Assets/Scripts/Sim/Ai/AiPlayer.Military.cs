namespace Craftwar.Sim.Ai
{
    public sealed partial class AiPlayer
    {
        /// <summary>Train toward the phase's standing-army targets. Every
        /// request resolves through TrainSubstitute first — once rangers are
        /// researched the barracks only accepts Ranger, never Archer.</summary>
        void ThinkTrain()
        {
            if (_emergency)
                return;
            var phase = Strategy.Phase(_phase);
            ref PlayerState p = ref _s.Players[Slot];
            for (int i = 0; i < phase.Army.Length && _budget > 0; i++)
            {
                var want = phase.Army[i];
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
                    return; // the farm manager reacts
                ref UnitTypeData row = ref _s.Rules.Units[(int)effType];
                if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                    continue;

                int trainer = FindIdleTrainerFor(baseType, effType);
                if (trainer < 0)
                    continue;
                Emit(AiQueries.Command(CommandOp.Train, Slot,
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

        /// <summary>Work through the cumulative research goals, one issue per
        /// think. Upgrade costs are stored x1 (unlike unit costs, x10).</summary>
        void ThinkResearch()
        {
            if (_emergency)
                return;
            ref PlayerState p = ref _s.Players[Slot];
            for (int pi = 0; pi <= _phase && pi < Strategy.Phases.Length; pi++)
            {
                var goals = Strategy.Phases[pi].ResearchGoals;
                for (int gi = 0; gi < goals.Length; gi++)
                {
                    var u = AiRaceMap.Upgrade(goals[gi], _race);
                    if (u == UpgradeId.None || p.HasResearched(u))
                        continue;
                    int lab = FindIdleLabFor(u);
                    if (lab < 0)
                        return; // provider missing or busy; later goals wait
                    ref UpgradeData row = ref _s.Rules.Upgrades[(int)u];
                    if (p.Oil < row.Oil)
                        continue; // permanently unaffordable on oil-less maps
                    if (EffectiveGold() < row.Gold || EffectiveLumber() < row.Lumber)
                        return;
                    Emit(AiQueries.Command(CommandOp.Research, Slot,
                        AiQueries.PackedId(_s, lab), param: (ushort)u));
                    return;
                }
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

        // ------------------------------------------------------------------
        // Waves
        // ------------------------------------------------------------------

        /// <summary>
        /// The original's muster-and-launch loop: when the standing army
        /// reaches the phase's wave size, send it at the strongest enemy in
        /// one AttackMove, sleep 500 ticks, advance the script. Plus the two
        /// desperation rules — the all-in when the base is nearly razed, and
        /// implicit defense from the sim's own auto-acquisition.
        /// </summary>
        void ThinkMilitary()
        {
            int buildings = CountOwnBuildings();
            if (buildings > _maxBuildingsSeen)
                _maxBuildingsSeen = buildings;
            if (_maxBuildingsSeen >= Strategy.SuicideBuildingCount
                && buildings < Strategy.SuicideBuildingCount)
            {
                if (_s.Tick >= _nextAllInTick && PickWaveTarget(out int ax, out int ay))
                {
                    LaunchWave(ax, ay, everything: true);
                    _nextAllInTick = _s.Tick + Strategy.PostWaveSleepTicks;
                }
                return;
            }

            if (_s.Tick < _sleepUntilTick)
                return;
            var phase = Strategy.Phase(_phase);
            int army = CountCombatUnits();
            if (army >= phase.WaveSize)
            {
                if (!PickWaveTarget(out int tx, out int ty))
                    return;
                LaunchWave(tx, ty, everything: false);
                _sleepUntilTick = _s.Tick + Strategy.PostWaveSleepTicks;
                _lastWaveTick = _s.Tick;
                _phase++;
                return;
            }

            // Dry-map liveness: with every gold mine gone the army can never
            // reach the muster size, so attack with what exists rather than
            // stalemating forever.
            if (army >= 1
                && _s.Tick - _lastWaveTick >= Strategy.DryWaveTicks
                && AiQueries.NearestGoldMine(_s, _anchorX, _anchorY) < 0
                && PickWaveTarget(out int dx, out int dy))
            {
                LaunchWave(dx, dy, everything: true);
                _sleepUntilTick = _s.Tick + Strategy.PostWaveSleepTicks;
                _lastWaveTick = _s.Tick;
            }
        }

        bool IsCombatUnit(ref Unit u) =>
            u.IsAlive && u.Player == Slot
            && (u.Flags & (UnitFlags.Building | UnitFlags.Hidden)) == 0
            && _s.Rules.Units[u.TypeId].Is(UnitTypeFlags.CanAttack)
            && !_s.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon)
            && UnitSpeeds.Get(u.TypeId) > 0;

        int CountCombatUnits()
        {
            int n = 0;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
                if (IsCombatUnit(ref _s.Units[i]))
                    n++;
            return n;
        }

        int CountOwnBuildings()
        {
            int n = 0;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (u.IsAlive && u.Player == Slot && (u.Flags & UnitFlags.Building) != 0
                    && !_s.Rules.Units[u.TypeId].Is(UnitTypeFlags.OilSource))
                    n++;
            }
            return n;
        }

        void LaunchWave(int tx, int ty, bool everything)
        {
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                if (!IsCombatUnit(ref _s.Units[i]))
                    continue;
                _scratchIds.Add(AiQueries.PackedId(_s, i));
                if (!everything && _scratchIds.Count >= GameCommand.MaxSelection)
                    break;
            }
            // The all-in ignores the per-think budget deliberately: it is a
            // one-shot, and it may need several 18-unit chunks.
            for (int start = 0; start < _scratchIds.Count;
                start += GameCommand.MaxSelection)
            {
                var chunk = new System.Collections.Generic.List<uint>();
                for (int i = start;
                    i < _scratchIds.Count && i - start < GameCommand.MaxSelection; i++)
                    chunk.Add(_scratchIds[i]);
                Emit(AiQueries.Command(CommandOp.AttackMove, Slot, chunk,
                    (ushort)tx, (ushort)ty));
                if (!everything)
                    break;
            }
        }

        /// <summary>
        /// Strongest enemy (most units alive, tie: lower slot), aiming at its
        /// first hall, else first building, else first unit. Reads full state —
        /// the AI ignores fog for targeting, exactly as the original does.
        /// </summary>
        bool PickWaveTarget(out int tx, out int ty)
        {
            int myTeam = _s.Players[Slot].Team;
            int bestSlot = -1, bestCount = -1;
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                if (p == Slot)
                    continue;
                ref PlayerState ps = ref _s.Players[p];
                if (!ps.InGame || ps.Controller == Controller.None
                    || ps.Outcome != PlayerOutcome.Playing || ps.Team == myTeam)
                    continue;
                int count = 0;
                for (int i = 0; i < _s.HighestUnitIndex; i++)
                {
                    ref Unit u = ref _s.Units[i];
                    if (u.IsAlive && u.Player == p)
                        count++;
                }
                if (count > bestCount)
                {
                    bestCount = count;
                    bestSlot = p;
                }
            }
            if (bestSlot < 0)
            {
                tx = 0;
                ty = 0;
                return false;
            }

            int firstBuilding = -1, firstUnit = -1;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player != bestSlot)
                    continue;
                if ((u.Flags & UnitFlags.Building) != 0)
                {
                    var t = (UnitTypeId)u.TypeId;
                    if (TechTree.Satisfies(t, UnitTypeId.TownHall)
                        || TechTree.Satisfies(t, UnitTypeId.GreatHall))
                    {
                        tx = u.TileX;
                        ty = u.TileY;
                        return true;
                    }
                    if (firstBuilding < 0)
                        firstBuilding = i;
                }
                else if (firstUnit < 0)
                {
                    firstUnit = i;
                }
            }
            int target = firstBuilding >= 0 ? firstBuilding : firstUnit;
            if (target < 0)
            {
                tx = 0;
                ty = 0;
                return false;
            }
            tx = _s.Units[target].TileX;
            ty = _s.Units[target].TileY;
            return true;
        }
    }
}
