namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Tactical army handling for the higher tiers — concentration, focus-fire,
    /// reinforcement and active defense — the work the original WC2 did in its
    /// native ICE.C group manager (which the script layer never touched). Every
    /// method here is gated by a tier competence in <see cref="ThinkMilitary"/>,
    /// so Dumb/Normal never call any of it and stay the M9 baseline exactly.
    /// </summary>
    public sealed partial class AiPlayer
    {
        /// <summary>The current push's target tile (stored so reinforcements can
        /// follow the army instead of trickling out one at a time).</summary>
        int _waveTargetX, _waveTargetY;
        bool _waveActive;

        /// <summary>An enemy this close to one of our buildings triggers a recall.</summary>
        const int DefendRadiusTiles = 12;
        /// <summary>Focus-fire only kicks in once the army is this close to the foe.</summary>
        const int FocusFireRangeTiles = 8;

        // ------------------------------------------------------------------
        // Active defense: pull the army home when the base is under attack.
        // ------------------------------------------------------------------

        bool TryDefendBase()
        {
            if (_budget <= 0 || !FindBaseThreat(out int tx, out int ty))
                return false;
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex
                && _scratchIds.Count < GameCommand.MaxSelection; i++)
                if (IsCombatUnit(ref _s.Units[i]))
                    _scratchIds.Add(AiQueries.PackedId(_s, i));
            if (_scratchIds.Count == 0)
                return false;
            Emit(AiQueries.Command(CommandOp.AttackMove, Slot, _scratchIds,
                (ushort)tx, (ushort)ty));
            return true;
        }

        /// <summary>Nearest enemy attacker sitting within DefendRadius of any of
        /// our buildings (squared distance, lower index breaks ties).</summary>
        bool FindBaseThreat(out int tx, out int ty)
        {
            int myTeam = _s.Players[Slot].Team;
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit e = ref _s.Units[i];
                if (!e.IsAlive || e.Player >= SimConstants.MaxPlayers || e.Player == Slot)
                    continue;
                if (_s.Players[e.Player].Team == myTeam)
                    continue;
                if ((e.Flags & (UnitFlags.Building | UnitFlags.Hidden)) != 0)
                    continue;
                if (!_s.Rules.Units[e.TypeId].Is(UnitTypeFlags.CanAttack))
                    continue;
                if (!NearOwnBuilding(e.TileX, e.TileY, DefendRadiusTiles))
                    continue;
                int dx = e.TileX - _anchorX, dy = e.TileY - _anchorY;
                int d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best < 0)
            {
                tx = ty = 0;
                return false;
            }
            tx = _s.Units[best].TileX;
            ty = _s.Units[best].TileY;
            return true;
        }

        bool NearOwnBuilding(int x, int y, int radius)
        {
            int r2 = radius * radius;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit b = ref _s.Units[i];
                if (!b.IsAlive || b.Player != Slot || (b.Flags & UnitFlags.Building) == 0)
                    continue;
                int dx = b.TileX - x, dy = b.TileY - y;
                if (dx * dx + dy * dy <= r2)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Focus fire: concentrate the army on the weakest enemy in contact.
        // ------------------------------------------------------------------

        void ManageFocusFire()
        {
            if (_budget <= 0 || !ArmyCentroid(out int cx, out int cy))
                return;
            int weakest = FindWeakestEnemyNear(cx, cy, FocusFireRangeTiles);
            if (weakest < 0)
                return;
            uint targetPacked = AiQueries.PackedId(_s, weakest);
            int r2 = FocusFireRangeTiles * FocusFireRangeTiles * 4; // loosely "in the fight"
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex
                && _scratchIds.Count < GameCommand.MaxSelection; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!IsCombatUnit(ref u))
                    continue;
                int dx = u.TileX - cx, dy = u.TileY - cy;
                if (dx * dx + dy * dy > r2)
                    continue;
                _scratchIds.Add(AiQueries.PackedId(_s, i));
            }
            if (_scratchIds.Count == 0)
                return;
            Emit(AiQueries.Command(CommandOp.Attack, Slot, _scratchIds,
                targetUnit: targetPacked));
        }

        bool ArmyCentroid(out int cx, out int cy)
        {
            int sx = 0, sy = 0, n = 0;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                if (!IsCombatUnit(ref _s.Units[i]))
                    continue;
                sx += _s.Units[i].TileX;
                sy += _s.Units[i].TileY;
                n++;
            }
            if (n == 0)
            {
                cx = cy = 0;
                return false;
            }
            cx = sx / n;
            cy = sy / n;
            return true;
        }

        /// <summary>Lowest-HP enemy (unit or building) within range of a point;
        /// lower index breaks HP ties. Reads full state — the AI cheats fog for
        /// targeting, as the original did.</summary>
        int FindWeakestEnemyNear(int cx, int cy, int rangeTiles)
        {
            int r2 = rangeTiles * rangeTiles;
            int myTeam = _s.Players[Slot].Team;
            int best = -1, bestHp = int.MaxValue;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit e = ref _s.Units[i];
                if (!e.IsAlive || e.Player >= SimConstants.MaxPlayers || e.Player == Slot)
                    continue;
                if (_s.Players[e.Player].Team == myTeam || (e.Flags & UnitFlags.Hidden) != 0)
                    continue;
                int dx = e.TileX - cx, dy = e.TileY - cy;
                if (dx * dx + dy * dy > r2)
                    continue;
                if (e.Hp < bestHp)
                {
                    bestHp = e.Hp;
                    best = i;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Reinforcement: feed idle combat units to the ongoing push instead of
        // letting them stand at the muster point until the next 500-tick wave.
        // ------------------------------------------------------------------

        void ReinforceFront()
        {
            if (_budget <= 0 || !_waveActive)
                return;
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex
                && _scratchIds.Count < GameCommand.MaxSelection; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (IsCombatUnit(ref u) && u.Order == OrderType.None
                    && !IsClaimed(AiQueries.PackedId(_s, i)))
                    _scratchIds.Add(AiQueries.PackedId(_s, i));
            }
            if (_scratchIds.Count == 0)
                return;
            Emit(AiQueries.Command(CommandOp.AttackMove, Slot, _scratchIds,
                (ushort)_waveTargetX, (ushort)_waveTargetY));
        }
    }
}
