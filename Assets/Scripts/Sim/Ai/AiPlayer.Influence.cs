using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Spatial awareness: the integer threat influence map plus the shared
    /// combat-unit predicate and strength tallies the generators reason with.
    /// Recomputed on a throttle, not every think, to respect the frame budget.
    /// </summary>
    public sealed partial class AiPlayer
    {
        /// <summary>Enemy influence past this value at a tile reads as "fully
        /// threatened" (score 0 safety). Calibrated so a couple of soldiers on a
        /// building matter.</summary>
        public const int ThreatFullScale = 120;

        void UpdateInfluence()
        {
            _threat.Ensure(_s.Terrain);
            if (_s.Tick - _lastInfluenceTick < InfluenceRefreshTicks
                && _lastInfluenceTick != int.MinValue)
                return;
            _lastInfluenceTick = _s.Tick;

            _threat.Clear();
            int myTeam = _s.Players[Slot].Team;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player >= SimConstants.MaxPlayers || u.Player == Slot)
                    continue;
                if (_s.Players[u.Player].Team == myTeam)
                    continue;
                if ((u.Flags & (UnitFlags.Building | UnitFlags.Hidden)) != 0)
                    continue;
                ref UnitTypeData row = ref _s.Rules.Units[u.TypeId];
                if (!row.Is(UnitTypeFlags.CanAttack))
                    continue;
                int peak = row.BasicDamage + row.PiercingDamage + 4;
                int radius = row.AttackRange + 4;
                if (radius < 4) radius = 4;
                _threat.AddDisc(u.TileX, u.TileY, peak, radius);
            }
        }

        /// <summary>Threat influence at the base, normalized to [0,1] (Q16.16).</summary>
        int BaseThreatInput()
        {
            int t = _threat.Sample(_anchorX, _anchorY);
            return AiMath.Normalize(t, 0, ThreatFullScale);
        }

        /// <summary>Safety consideration (Q16.16): high when the base is calm, low
        /// under threat, shaped by the profile's threatSafety curve.</summary>
        int BaseSafety() => Profile.ThreatSafety.Eval(BaseThreatInput());

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

        /// <summary>Units alive for an enemy slot (buildings excluded), the same
        /// simple proxy the original used for target choice.</summary>
        int EnemyUnitCount(int slot)
        {
            int n = 0;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (u.IsAlive && u.Player == slot && (u.Flags & UnitFlags.Building) == 0)
                    n++;
            }
            return n;
        }

        /// <summary>Enemy COMBAT units only (no workers, no buildings) — the fair
        /// comparison for "can my army win this fight?". Comparing army against the
        /// enemy's whole population (workers included) made the AI feel perpetually
        /// outnumbered and never commit.</summary>
        int EnemyCombatCount(int slot)
        {
            int n = 0;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player != slot) continue;
                if ((u.Flags & (UnitFlags.Building | UnitFlags.Hidden)) != 0) continue;
                ref UnitTypeData row = ref _s.Rules.Units[u.TypeId];
                if (row.Is(UnitTypeFlags.CanAttack) && !row.Is(UnitTypeFlags.Peon))
                    n++;
            }
            return n;
        }
    }
}
