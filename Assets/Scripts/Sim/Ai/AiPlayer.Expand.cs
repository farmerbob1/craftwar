namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Scouting and expansion for the higher tiers. Both are gated in
    /// <see cref="Think"/> (Smart scouts; God scouts and expands), so Dumb/Normal
    /// never run them and stay the M9 baseline.
    ///
    /// Expansion deliberately stops at "build a second hall by a fresh mine"
    /// rather than a full multi-base harvest rebalance: the economy manager keys
    /// its mine off the base anchor, so once the home mine runs dry it already
    /// routes workers to the next-nearest live mine — the one the new hall now
    /// sits beside, which the sim's depot search picks as their drop-off. That
    /// keeps a mined-out map from stalemating (the whole point) without the risky
    /// per-base worker split; the fuller rebalance is noted for later.
    /// </summary>
    public sealed partial class AiPlayer
    {
        bool _scouted;

        /// <summary>Send the scout only once the economy is on its feet.</summary>
        const int ScoutTick = 150;
        /// <summary>…and only with a worker to spare, so the (cosmetic) scout
        /// never robs a thin early economy — the original scouted from surplus too.</summary>
        const int ScoutMinWorkers = 5;
        /// <summary>Grab a second base once the worker count outgrows one mine…</summary>
        const int ExpandWorkers = 12;
        /// <summary>…or the home mine is nearly tapped out.</summary>
        const int ExpandMineLow = 5000;
        /// <summary>A mine with a friendly building this close counts as ours.</summary>
        const int ExpandNearHallTiles = 8;

        // ------------------------------------------------------------------
        // Scouting: one early poke toward the enemy. The AI already cheats fog
        // for targeting, so this is for feel (and a future honest-fog mode); it
        // costs one worker a round trip, exactly as the original's peon scout did.
        // ------------------------------------------------------------------
        void TryScout()
        {
            if (_scouted || _budget <= 0 || _s.Tick < ScoutTick)
                return;
            if (WorkerCount() < ScoutMinWorkers)
                return; // wait for a surplus so the scout is free
            if (!PickWaveTarget(out int ex, out int ey))
                return; // no enemy to scout yet
            int w = AiQueries.FindIdleWorker(_s, Slot, _claimed);
            if (w < 0)
                w = AiQueries.FindHarvestingWorker(_s, Slot, wood: false, _claimed);
            if (w < 0)
                w = AiQueries.FindHarvestingWorker(_s, Slot, wood: true, _claimed);
            if (w < 0)
                return; // no spare worker yet — try again next think (still unscouted)
            Emit(AiQueries.Command(CommandOp.Move, Slot, AiQueries.PackedId(_s, w),
                (ushort)ex, (ushort)ey));
            _scouted = true;
        }

        // ------------------------------------------------------------------
        // Expansion: a second town hall by an untapped gold mine.
        // ------------------------------------------------------------------
        void ThinkExpansion()
        {
            if (_budget <= 0 || _emergency)
                return;
            ushort hall = (ushort)AiRaceMap.Unit(AiUnit.Hall, _race);
            // One expansion, one at a time: cap at two halls and never double-issue.
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

            int w = AiQueries.FindIdleWorker(_s, Slot, _claimed);
            if (w < 0)
                w = AiQueries.FindHarvestingWorker(_s, Slot, wood: false, _claimed);
            if (w < 0)
                return;
            uint packed = AiQueries.PackedId(_s, w);

            // Search around the fresh mine (the mine-lane keep-out places the hall
            // a few tiles clear of it, as a real base would sit).
            if (!AiSiteSearch.FindSite(_s, hall, mineX, mineY, AiSiteSearch.MaxRadius,
                    packed, out int x, out int y))
                return;

            Emit(AiQueries.Command(CommandOp.Build, Slot, packed,
                (ushort)x, (ushort)y, param: hall));
            _pending.Add(new PendingBuild
            {
                BuilderPacked = packed,
                TypeId = hall,
                X = (ushort)x,
                Y = (ushort)y,
                IssuedTick = _s.Tick,
            });
        }

        int HallCount()
        {
            var req = _race == Race.Orc ? UnitTypeId.GreatHall : UnitTypeId.TownHall;
            return AiQueries.CountSatisfying(_s, Slot, req, includeUnderConstruction: true);
        }

        int WorkerCount()
        {
            ushort worker = (ushort)AiRaceMap.Unit(AiUnit.Worker, _race);
            return AiQueries.CountAlive(_s, Slot, worker, includeUnderConstruction: false);
        }

        /// <summary>Nearest gold mine (to the base anchor) that has no friendly
        /// building beside it — one we have not tapped yet.</summary>
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
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best < 0)
            {
                mineX = mineY = 0;
                return false;
            }
            mineX = _s.Units[best].TileX;
            mineY = _s.Units[best].TileY;
            return true;
        }
    }
}
