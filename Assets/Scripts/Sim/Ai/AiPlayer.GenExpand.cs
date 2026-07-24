using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Expansion (a second hall by a fresh mine) and scouting, for the tiers that
    /// enable them. Both are scored candidates; expansion places its hall through
    /// the same connectivity-gated planner, so a second base can never wall itself
    /// in either.
    /// </summary>
    public sealed partial class AiPlayer
    {
        const int ScoutTick = 150;
        const int ScoutMinWorkers = 5;
        const int ExpandWorkers = 12;
        const int ExpandMineLow = 5000;
        const int ExpandNearHallTiles = 8;

        void GenExpand()
        {
            if (_emergency)
                return;
            ushort hall = (ushort)AiRaceMap.Unit(AiUnit.Hall, _race);
            if (HallCount() >= 2 || PendingOfType(hall) > 0)
                return;

            int home = AiQueries.NearestGoldMine(_s, _anchorX, _anchorY);
            bool mineLow = home >= 0 && _s.Units[home].ResourceAmount <= ExpandMineLow;
            bool saturated = WorkerCount() >= ExpandWorkers;
            if (!mineLow && !saturated)
                return;
            if (!FindFreshMine(out int mineX, out int mineY))
                return;
            if (!_sim.CanProduce(Slot, (UnitTypeId)hall))
                return;
            ref UnitTypeData row = ref _s.Rules.Units[hall];
            if (row.OilCost > _s.Players[Slot].Oil
                || !CanAfford(row.GoldCost, row.LumberCost, row.OilCost))
                return;
            int w = FindBuilder();
            if (w < 0)
                return;
            uint packed = AiQueries.PackedId(_s, w);

            // Plan the hall around the fresh mine, connectivity-gated so the new
            // base keeps the mine, wood and an exit reachable.
            if (!_planner.FindSite(_s, Slot, hall, mineX, mineY, AiSiteSearch.MaxRadius,
                    packed, _blacklistedSites, _threat, out int x, out int y))
                return;

            int score = Util.Score(Profile.WeightExpand, AiMath.One, BaseSafety());
            AddCandidate(AiActionKind.Expand, score,
                AiQueries.Command(CommandOp.Build, Slot, packed,
                    (ushort)x, (ushort)y, param: hall),
                hall, (ushort)x, (ushort)y);
        }

        int HallCount()
        {
            var req = _race == Race.Orc ? UnitTypeId.GreatHall : UnitTypeId.TownHall;
            return AiQueries.CountSatisfying(_s, Slot, req, true);
        }

        int WorkerCount()
        {
            ushort worker = (ushort)AiRaceMap.Unit(AiUnit.Worker, _race);
            return AiQueries.CountAlive(_s, Slot, worker, false);
        }

        bool FindFreshMine(out int mineX, out int mineY)
        {
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || !_s.Rules.Units[u.TypeId].Is(UnitTypeFlags.GoldMine))
                    continue;
                if (NearOwnBuilding(u.TileX, u.TileY, ExpandNearHallTiles))
                    continue;
                int dx = u.TileX - _anchorX, dy = u.TileY - _anchorY;
                int d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) { mineX = mineY = 0; return false; }
            mineX = _s.Units[best].TileX;
            mineY = _s.Units[best].TileY;
            return true;
        }

        void GenScout()
        {
            if (_scouted || _s.Tick < ScoutTick)
                return;
            if (WorkerCount() < ScoutMinWorkers)
                return;
            if (!PickWaveTarget(out int ex, out int ey))
                return;
            int w = FindBuilder();
            if (w < 0)
                return;
            AddCandidate(AiActionKind.Scout,
                Util.Score(Profile.WeightScout, AiMath.One),
                AiQueries.Command(CommandOp.Move, Slot, AiQueries.PackedId(_s, w),
                    (ushort)ex, (ushort)ey));
        }
    }
}
