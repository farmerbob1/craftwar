using System.Collections.Generic;
using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Military generators: the muster-and-launch wave, the desperation all-in, and
    /// the tactical trio (active defense, focus fire, reinforcement) for capable
    /// tiers. All are scored candidates competing in the arbiter; because they share
    /// the army selection, the arbiter's actor-claim skip makes defense naturally
    /// preempt an attack (higher weight wins the squad), as the old pipeline did by
    /// call order.
    /// </summary>
    public sealed partial class AiPlayer
    {
        const int DefendRadiusTiles = 12;
        const int FocusFireRangeTiles = 8;

        /// <summary>Reinforce only once a worthwhile group has gathered, so new
        /// units march to the front together instead of trickling in and dying one
        /// at a time.</summary>
        const int ReinforceMinGroup = 5;

        void GenMilitary()
        {
            int buildings = CountOwnBuildings();
            if (buildings > _maxBuildingsSeen)
                _maxBuildingsSeen = buildings;
            // A push is over once its army is spent — stop feeding stragglers to a
            // dead wave's target (the old bug that bled the higher tiers dry).
            if (_waveActive && CountCombatUnits() == 0)
                _waveActive = false;

            // Desperation all-in: the base is being razed. Fires above everything.
            if (_maxBuildingsSeen >= Profile.SuicideBuildingCount
                && buildings < Profile.SuicideBuildingCount
                && _s.Tick >= _nextAllInTick
                && CountCombatUnits() >= 1
                && PickWaveTarget(out int sx, out int sy))
            {
                AddCandidate(AiActionKind.AllIn,
                    Util.Score(Profile.WeightDefend + Profile.WeightWave, AiMath.One),
                    default, 0, (ushort)sx, (ushort)sy);
                return;
            }

            if (_tier.ActiveDefense)
                GenDefend();
            if (_tier.FocusFire)
                GenFocusFire();
            if (_tier.Reinforce)
                GenReinforce();

            GenWave();
        }

        void GenWave()
        {
            if (_s.Tick < _sleepUntilTick)
                return;
            int army = CountCombatUnits();

            // Muster-and-launch: enough army and (relative strength permitting) go.
            if (army >= Profile.WaveSize && PickWaveTarget(out int tx, out int ty))
            {
                int readiness = Profile.WaveReadiness.Eval(
                    AiMath.Normalize(army, 0, Profile.WaveSize));
                int rel = Profile.RelativeStrength.Eval(RelativeStrengthInput(army));
                int score = Util.Score(Profile.WeightWave, readiness, rel);
                if (BuildWaveChunk(tx, ty, out var cmd))
                    AddCandidate(AiActionKind.LaunchWave, score, in cmd, 0,
                        (ushort)tx, (ushort)ty);
                return;
            }

            // Dry-map liveness: no gold left means the army can never reach the
            // muster size, so commit what exists rather than stalemating forever.
            if (army >= 1
                && _s.Tick - _lastWaveTick >= Profile.DryWaveTicks
                && AiQueries.NearestGoldMine(_s, _anchorX, _anchorY) < 0
                && PickWaveTarget(out int dx, out int dy))
            {
                AddCandidate(AiActionKind.AllIn,
                    Util.Score(Profile.WeightWave, AiMath.One),
                    default, 0, (ushort)dx, (ushort)dy);
            }
        }

        /// <summary>Own army vs. the strongest enemy's, as a [0,1] share (0.5 = even);
        /// feeds the relativeStrength curve so a cautious profile waits when behind.</summary>
        int RelativeStrengthInput(int army)
        {
            int strongest = 0;
            int myTeam = _s.Players[Slot].Team;
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                if (p == Slot) continue;
                ref PlayerState ps = ref _s.Players[p];
                if (!ps.InGame || ps.Controller == Controller.None
                    || ps.Outcome != PlayerOutcome.Playing || ps.Team == myTeam)
                    continue;
                int c = EnemyCombatCount(p);
                if (c > strongest) strongest = c;
            }
            return AiMath.Normalize(army, 0, army + strongest);
        }

        // ---- Tactical trio ----

        void GenDefend()
        {
            if (!FindBaseThreat(out int tx, out int ty))
                return;
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex
                && _scratchIds.Count < GameCommand.MaxSelection; i++)
                if (IsCombatUnit(ref _s.Units[i]))
                    _scratchIds.Add(AiQueries.PackedId(_s, i));
            if (_scratchIds.Count == 0)
                return;
            AddCandidate(AiActionKind.DefendBase,
                Util.Score(Profile.WeightDefend, AiMath.One),
                AiQueries.Command(CommandOp.AttackMove, Slot, _scratchIds,
                    (ushort)tx, (ushort)ty));
        }

        void GenFocusFire()
        {
            if (!ArmyCentroid(out int cx, out int cy))
                return;
            int weakest = FindWeakestEnemyNear(cx, cy, FocusFireRangeTiles);
            if (weakest < 0)
                return;
            uint targetPacked = AiQueries.PackedId(_s, weakest);
            int r2 = FocusFireRangeTiles * FocusFireRangeTiles * 4;
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex
                && _scratchIds.Count < GameCommand.MaxSelection; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!IsCombatUnit(ref u)) continue;
                int dx = u.TileX - cx, dy = u.TileY - cy;
                if (dx * dx + dy * dy > r2) continue;
                _scratchIds.Add(AiQueries.PackedId(_s, i));
            }
            if (_scratchIds.Count == 0)
                return;
            AddCandidate(AiActionKind.FocusFire,
                Util.Score(Profile.WeightDefend, AiMath.Half),
                AiQueries.Command(CommandOp.Attack, Slot, _scratchIds,
                    targetUnit: targetPacked));
        }

        void GenReinforce()
        {
            if (!_waveActive)
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
            if (_scratchIds.Count < ReinforceMinGroup)
                return; // wait for a group; single stragglers just feed the enemy
            AddCandidate(AiActionKind.Reinforce,
                Util.Score(Profile.WeightWave, AiMath.Half),
                AiQueries.Command(CommandOp.AttackMove, Slot, _scratchIds,
                    (ushort)_waveTargetX, (ushort)_waveTargetY));
        }

        // ---- Execution hooks (called by ExecuteAction) ----

        void OnWaveLaunched(int tx, int ty)
        {
            _waveTargetX = tx;
            _waveTargetY = ty;
            _waveActive = true;
            _sleepUntilTick = _s.Tick + Profile.PostWaveSleepTicks;
            _lastWaveTick = _s.Tick;
        }

        void LaunchAllIn(int tx, int ty)
        {
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex; i++)
                if (IsCombatUnit(ref _s.Units[i]))
                    _scratchIds.Add(AiQueries.PackedId(_s, i));
            for (int start = 0; start < _scratchIds.Count; start += GameCommand.MaxSelection)
            {
                var chunk = new List<uint>();
                for (int i = start;
                    i < _scratchIds.Count && i - start < GameCommand.MaxSelection; i++)
                    chunk.Add(_scratchIds[i]);
                Emit(AiQueries.Command(CommandOp.AttackMove, Slot, chunk,
                    (ushort)tx, (ushort)ty));
            }
            _waveTargetX = tx;
            _waveTargetY = ty;
            _waveActive = true;
            _nextAllInTick = _s.Tick + Profile.PostWaveSleepTicks;
            _sleepUntilTick = _s.Tick + Profile.PostWaveSleepTicks;
            _lastWaveTick = _s.Tick;
        }

        /// <summary>Build one ≤18-unit AttackMove chunk of the standing army.</summary>
        bool BuildWaveChunk(int tx, int ty, out GameCommand cmd)
        {
            _scratchIds.Clear();
            for (int i = 0; i < _s.HighestUnitIndex
                && _scratchIds.Count < GameCommand.MaxSelection; i++)
                if (IsCombatUnit(ref _s.Units[i]))
                    _scratchIds.Add(AiQueries.PackedId(_s, i));
            if (_scratchIds.Count == 0)
            {
                cmd = default;
                return false;
            }
            cmd = AiQueries.Command(CommandOp.AttackMove, Slot, _scratchIds,
                (ushort)tx, (ushort)ty);
            return true;
        }

        // ---- Targeting / threat scans (ported; squared distance, lowest-index ties) ----

        bool PickWaveTarget(out int tx, out int ty)
        {
            int myTeam = _s.Players[Slot].Team;
            int bestSlot = -1, bestCount = -1;
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                if (p == Slot) continue;
                ref PlayerState ps = ref _s.Players[p];
                if (!ps.InGame || ps.Controller == Controller.None
                    || ps.Outcome != PlayerOutcome.Playing || ps.Team == myTeam)
                    continue;
                int count = 0;
                for (int i = 0; i < _s.HighestUnitIndex; i++)
                {
                    ref Unit u = ref _s.Units[i];
                    if (u.IsAlive && u.Player == p) count++;
                }
                if (count > bestCount) { bestCount = count; bestSlot = p; }
            }
            if (bestSlot < 0) { tx = ty = 0; return false; }

            int firstBuilding = -1, firstUnit = -1;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit u = ref _s.Units[i];
                if (!u.IsAlive || u.Player != bestSlot) continue;
                if ((u.Flags & UnitFlags.Building) != 0)
                {
                    var t = (UnitTypeId)u.TypeId;
                    if (TechTree.Satisfies(t, UnitTypeId.TownHall)
                        || TechTree.Satisfies(t, UnitTypeId.GreatHall))
                    {
                        tx = u.TileX; ty = u.TileY; return true;
                    }
                    if (firstBuilding < 0) firstBuilding = i;
                }
                else if (firstUnit < 0) firstUnit = i;
            }
            int target = firstBuilding >= 0 ? firstBuilding : firstUnit;
            if (target < 0) { tx = ty = 0; return false; }
            tx = _s.Units[target].TileX;
            ty = _s.Units[target].TileY;
            return true;
        }

        bool FindBaseThreat(out int tx, out int ty)
        {
            int myTeam = _s.Players[Slot].Team;
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                ref Unit e = ref _s.Units[i];
                if (!e.IsAlive || e.Player >= SimConstants.MaxPlayers || e.Player == Slot)
                    continue;
                if (_s.Players[e.Player].Team == myTeam) continue;
                if ((e.Flags & (UnitFlags.Building | UnitFlags.Hidden)) != 0) continue;
                if (!_s.Rules.Units[e.TypeId].Is(UnitTypeFlags.CanAttack)) continue;
                if (!NearOwnBuilding(e.TileX, e.TileY, DefendRadiusTiles)) continue;
                int dx = e.TileX - _anchorX, dy = e.TileY - _anchorY;
                int d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) { tx = ty = 0; return false; }
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
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        bool ArmyCentroid(out int cx, out int cy)
        {
            int sx = 0, sy = 0, n = 0;
            for (int i = 0; i < _s.HighestUnitIndex; i++)
            {
                if (!IsCombatUnit(ref _s.Units[i])) continue;
                sx += _s.Units[i].TileX;
                sy += _s.Units[i].TileY;
                n++;
            }
            if (n == 0) { cx = cy = 0; return false; }
            cx = sx / n;
            cy = sy / n;
            return true;
        }

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
                if (dx * dx + dy * dy > r2) continue;
                if (e.Hp < bestHp) { bestHp = e.Hp; best = i; }
            }
            return best;
        }
    }
}
