namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Shared read-only scans over GameState for the AI managers. Everything
    /// here is a pure function of state with ordered iteration and lower-index
    /// tie-breaks, so identical state always yields identical answers.
    /// </summary>
    public static class AiQueries
    {
        public const uint WoodTargetFlag = 0x80000000;

        /// <summary>Alive units of exactly this type. UnderConstruction counts
        /// when <paramref name="includeUnderConstruction"/> is set.</summary>
        public static int CountAlive(GameState s, byte player, ushort typeId,
            bool includeUnderConstruction)
        {
            int n = 0;
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (!u.IsAlive || u.Player != player || u.TypeId != typeId)
                    continue;
                if (!includeUnderConstruction && (u.Flags & UnitFlags.UnderConstruction) != 0)
                    continue;
                n++;
            }
            return n;
        }

        /// <summary>Alive buildings satisfying `required` (upgraded halls stand
        /// in for their earlier tiers, per TechTree.Satisfies).</summary>
        public static int CountSatisfying(GameState s, byte player, UnitTypeId required,
            bool includeUnderConstruction)
        {
            int n = 0;
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (!u.IsAlive || u.Player != player || (u.Flags & UnitFlags.Building) == 0)
                    continue;
                if (!includeUnderConstruction && (u.Flags & UnitFlags.UnderConstruction) != 0)
                    continue;
                if (TechTree.Satisfies((UnitTypeId)u.TypeId, required))
                    n++;
            }
            return n;
        }

        /// <summary>Buildings currently training/upgrading into this type.</summary>
        public static int CountInTraining(GameState s, byte player, ushort typeId)
        {
            int n = 0;
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (u.IsAlive && u.Player == player && (u.Flags & UnitFlags.Building) != 0
                    && u.BuildType == typeId + 1)
                    n++;
            }
            return n;
        }

        /// <summary>Lowest-index idle worker: alive, visible, no order, no
        /// harvest cycle, not in `exclude`. -1 when none.</summary>
        public static int FindIdleWorker(GameState s, byte player,
            System.Collections.Generic.List<uint> exclude = null)
        {
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (u.IsAlive && u.Player == player
                    && s.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon)
                    && (u.Flags & UnitFlags.Hidden) == 0
                    && u.Order == OrderType.None && u.Harvest == HarvestStage.None
                    && (exclude == null || !exclude.Contains(PackedId(s, i))))
                    return i;
            }
            return -1;
        }

        /// <summary>Lowest-index worker on the harvest cycle (any stage, not
        /// hidden inside a mine/depot), not in `exclude`. -1 when none.</summary>
        public static int FindHarvestingWorker(GameState s, byte player, bool wood,
            System.Collections.Generic.List<uint> exclude = null)
        {
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (u.IsAlive && u.Player == player
                    && s.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon)
                    && (u.Flags & UnitFlags.Hidden) == 0
                    && u.Order == OrderType.Harvest
                    && ((u.ResourceTarget & WoodTargetFlag) != 0) == wood
                    && (exclude == null || !exclude.Contains(PackedId(s, i))))
                    return i;
            }
            return -1;
        }

        /// <summary>Lowest-index complete, unoccupied building whose type
        /// satisfies `required`. -1 when none.</summary>
        public static int FindIdleBuilding(GameState s, byte player, UnitTypeId required)
        {
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (u.IsAlive && u.Player == player && (u.Flags & UnitFlags.Building) != 0
                    && (u.Flags & UnitFlags.UnderConstruction) == 0
                    && u.BuildType == 0 && u.ResearchId == 0
                    && TechTree.Satisfies((UnitTypeId)u.TypeId, required))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The tile the AI plans around: its first hall (any tier), else its
        /// first building, else its first unit. False only when the player has
        /// nothing left at all.
        /// </summary>
        public static bool FindBaseAnchor(GameState s, byte player, out int x, out int y)
        {
            int firstBuilding = -1, firstUnit = -1;
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (!u.IsAlive || u.Player != player)
                    continue;
                if ((u.Flags & UnitFlags.Building) != 0)
                {
                    var t = (UnitTypeId)u.TypeId;
                    if (TechTree.Satisfies(t, UnitTypeId.TownHall)
                        || TechTree.Satisfies(t, UnitTypeId.GreatHall))
                    {
                        x = u.TileX;
                        y = u.TileY;
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
            int best = firstBuilding >= 0 ? firstBuilding : firstUnit;
            if (best < 0)
            {
                x = 0;
                y = 0;
                return false;
            }
            x = s.Units[best].TileX;
            y = s.Units[best].TileY;
            return true;
        }

        /// <summary>Nearest live gold mine by squared tile distance (lower
        /// index wins ties). -1 when the map has none left.</summary>
        public static int NearestGoldMine(GameState s, int fromX, int fromY)
        {
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ref Unit u = ref s.Units[i];
                if (!u.IsAlive || !s.Rules.Units[u.TypeId].Is(UnitTypeFlags.GoldMine))
                    continue;
                int dx = u.TileX - fromX, dy = u.TileY - fromY;
                int d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Nearest wood tile via a fixed-order ring spiral. False when
        /// no wood exists within the radius.</summary>
        public static bool NearestWoodTile(GameState s, int fromX, int fromY, int maxRadius,
            out int woodX, out int woodY)
        {
            var t = s.Terrain;
            if (t.HasWood(fromX, fromY))
            {
                woodX = fromX;
                woodY = fromY;
                return true;
            }
            for (int r = 1; r <= maxRadius; r++)
            {
                for (int i = 0; i < AiSiteSearch.RingLength(r); i++)
                {
                    AiSiteSearch.RingTile(fromX, fromY, r, i, out int x, out int y);
                    if (t.InBounds(x, y) && t.HasWood(x, y))
                    {
                        woodX = x;
                        woodY = y;
                        return true;
                    }
                }
            }
            woodX = 0;
            woodY = 0;
            return false;
        }

        public static uint PackedId(GameState s, int index) =>
            new UnitId((ushort)index, s.Units[index].Gen).Packed;

        // ------------------------------------------------------------------
        // Command builders. The only place AI code touches the unsafe fixed
        // selection buffer.
        // ------------------------------------------------------------------

        public static unsafe GameCommand Command(CommandOp op, byte player, uint selected,
            ushort targetX = 0, ushort targetY = 0, uint targetUnit = 0, ushort param = 0)
        {
            var cmd = new GameCommand
            {
                Op = op,
                Player = player,
                TargetX = targetX,
                TargetY = targetY,
                TargetUnit = targetUnit,
                Param = param,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = selected;
            return cmd;
        }

        /// <summary>Multi-selection variant; takes at most MaxSelection ids.</summary>
        public static unsafe GameCommand Command(CommandOp op, byte player,
            System.Collections.Generic.List<uint> selected,
            ushort targetX = 0, ushort targetY = 0, uint targetUnit = 0, ushort param = 0)
        {
            var cmd = new GameCommand
            {
                Op = op,
                Player = player,
                TargetX = targetX,
                TargetY = targetY,
                TargetUnit = targetUnit,
                Param = param,
            };
            int n = selected.Count < GameCommand.MaxSelection
                ? selected.Count
                : GameCommand.MaxSelection;
            cmd.SelectionCount = (byte)n;
            for (int i = 0; i < n; i++)
                cmd.Selection.Ids[i] = selected[i];
            return cmd;
        }
    }
}
