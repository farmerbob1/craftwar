using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Naval economy: raising oil platforms. Platforms are raised by *tankers*,
    /// never workers (TechTree.TankerBuildings is a disjoint build menu from the
    /// worker's — see GameSim.Economy.OnAnyBuildMenu), so this runs outside
    /// GenBuild's build-order walk and picks its own builder and site.
    ///
    /// Everything else naval (shipyard/foundry/refinery construction, tanker/
    /// transport/warship/battleship training) already flows through the generic
    /// GenBuild/GenTrain machinery once those roles appear in a profile's
    /// build/army lists — TechTree.Trains(Shipyard) and the ordinary worker
    /// build menu cover them with no naval-specific code. Warships and dragons
    /// alike fold into the existing wave/defend/focus-fire logic for free: they
    /// satisfy IsCombatUnit (CanAttack, not a Peon, non-zero speed) regardless
    /// of move domain.
    /// </summary>
    public sealed partial class AiPlayer
    {
        void GenNaval()
        {
            if (_emergency)
                return;
            GenOilWell();
        }

        void GenOilWell()
        {
            ushort oilWell = (ushort)AiRaceMap.Unit(AiUnit.OilWell, _race);
            if (PendingOfType(oilWell) > 0)
                return;
            if (!_sim.CanProduce(Slot, (UnitTypeId)oilWell))
                return;
            ref UnitTypeData row = ref _s.Rules.Units[oilWell];
            if (!CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                return;

            int t = FindIdleTanker();
            if (t < 0)
                return;
            if (!FindNearestOilPatch(_s.Units[t].TileX, _s.Units[t].TileY, out int px, out int py))
                return;

            uint packed = AiQueries.PackedId(_s, t);
            var cmd = AiQueries.Command(CommandOp.Build, Slot, packed,
                (ushort)px, (ushort)py, param: oilWell);
            AddCandidate(AiActionKind.Build,
                Util.Score(Profile.WeightBuild, AiMath.One), in cmd, oilWell,
                (ushort)px, (ushort)py);
        }

        int FindIdleTanker()
        {
            ushort tankerType = (ushort)AiRaceMap.Unit(AiUnit.Tanker, _race);
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (u.IsAlive && u.Player == Slot && u.TypeId == tankerType
                    && (u.Flags & UnitFlags.Hidden) == 0
                    && u.Order == OrderType.None && u.Harvest == HarvestStage.None)
                    return i;
            }
            return -1;
        }

        /// <summary>Nearest live oil patch by squared tile distance (lower index
        /// wins ties) — same shape as AiQueries.NearestGoldMine.</summary>
        bool FindNearestOilPatch(int fromX, int fromY, out int patchX, out int patchY)
        {
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || !_s.Rules.Units[u.TypeId].Is(UnitTypeFlags.OilPatch))
                    continue;
                int dx = u.TileX - fromX, dy = u.TileY - fromY;
                int d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) { patchX = patchY = 0; return false; }
            patchX = _s.Units[best].TileX;
            patchY = _s.Units[best].TileY;
            return true;
        }
    }
}
