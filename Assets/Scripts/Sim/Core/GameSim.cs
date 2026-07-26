using System.Collections.Generic;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim
{
    /// <summary>
    /// The deterministic fixed-tick simulation. Advance() is the ONLY way time
    /// passes; commands are the ONLY inputs. Systems run in a fixed order every
    /// tick — never reorder without a determinism review, and never read
    /// anything outside GameState.
    /// </summary>
    public sealed partial class GameSim
    {
        public readonly GameState State;
        Pathfinder _pathfinder;

        // Scratch path buffer reused by every search this tick.
        ushort[] _pathScratch;

        // Per-tick pathfinding budget. A* dominates the tick (profiled at ~99% of
        // Advance late-game), and congestion makes many blocked units want to
        // repath at once. Capping searches per tick spreads them across ticks in
        // deterministic unit-index order: a unit denied a search this tick keeps
        // its order and retries next tick. The original got the same effect by
        // caching a short per-unit traverse (traverse.c). Transient per-tick
        // state — reset in TickMovement, never hashed.
        const int MaxRepathsPerTick = 12;
        int _repathsThisTick;

        // Victory. The evaluator is swappable so the campaign track (M13) can
        // supply scenario objectives; the scratch array lives here rather than on
        // GameState because it is fully rewritten each call and so cannot carry
        // state between ticks — same reasoning as _pathScratch.
        IVictoryEvaluator _victory = new MeleeVictoryEvaluator();
        readonly PlayerOutcome[] _outcomeScratch = new PlayerOutcome[SimConstants.MaxPlayers];

        /// <summary>Replace the melee rules with scenario objectives. Must be set
        /// before the first Advance so every peer evaluates identically.</summary>
        public void SetVictoryEvaluator(IVictoryEvaluator evaluator)
        {
            if (evaluator != null)
                _victory = evaluator;
        }

        public GameSim(ulong seed)
        {
            State = new GameState(seed);
        }

        /// <summary>
        /// Populate the world from a map. Neutral gold mines / oil patches
        /// spawn like any unit; start-location markers become nothing (they
        /// seed camera/AI placement at the app layer).
        /// </summary>
        /// <summary>
        /// Finish standing up a sim whose state came from a snapshot rather than
        /// from a map. Everything here is derived data that <see cref="Setup"/>
        /// would otherwise have built: the pathfinder and its scratch buffer, and
        /// the sight grids, which TickFog rebuilds from scratch every tick and so
        /// are never stored.
        /// </summary>
        internal void AdoptLoadedState(int width, int height)
        {
            _pathfinder = new Pathfinder(State.Terrain, State);
            _pathScratch = new ushort[width * height];
            TickFog();
        }

        public void Setup(PudFile pud, RuleSet rules)
            => Setup(pud, rules, MatchSetup.FromPud(pud));

        /// <summary>
        /// As <see cref="Setup(PudFile, RuleSet)"/>, but with lobby overrides for
        /// controller/race/team. The two-argument form is the map's own defaults.
        /// </summary>
        public void Setup(PudFile pud, RuleSet rules, MatchSetup setup)
        {
            State.Rules = rules;
            State.Terrain = TerrainMap.FromPud(pud);
            State.InstallTiles((ushort[])pud.Tiles.Clone());
            State.OccupancySurface = new uint[pud.Width * pud.Height];
            State.OccupancyAir = new uint[pud.Width * pud.Height];
            State.Visible = new byte[SimConstants.MaxPlayers][];
            State.Explored = new byte[SimConstants.MaxPlayers][];
            State.Detected = new byte[SimConstants.MaxPlayers][];
            _pathfinder = new Pathfinder(State.Terrain, State);
            _pathScratch = new ushort[pud.Width * pud.Height];

            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                byte owner = pud.Owner[p];
                bool inGame = MatchSetup.IsInGame(owner);
                State.Players[p] = new PlayerState
                {
                    InGame = inGame,
                    Race = setup.Slots[p].Race,
                    // A slot the map does not spawn cannot be a participant, even
                    // if a lobby tried to seat someone there.
                    Controller = inGame ? setup.Slots[p].Controller : Controller.None,
                    Team = setup.Slots[p].Team,
                    Outcome = PlayerOutcome.Playing,
                    // Start-resource handicap is a one-time bump at Setup (the
                    // running Gold/Lumber are hashed, so no separate field needed).
                    Gold = pud.StartGold[p] + setup.Slots[p].StartGoldBonus,
                    Lumber = pud.StartLumber[p] + setup.Slots[p].StartLumberBonus,
                    Oil = pud.StartOil[p],
                    AllowedUnits = pud.AllowUnits?[p] ?? ~0u,
                    AllowedUpgrades = pud.AllowUpgrades?[p] ?? ~0u,
                    AllowedSpells = pud.AllowSpellResearch?[p] ?? ~0u,
                    HarvestBonusTenths = setup.Slots[p].HarvestBonusTenths,
                    SightBonus = setup.Slots[p].SightBonus,
                };

                // Fog grids only exist for slots that are actually playing;
                // ComputeHash and TickFog both skip the rest.
                if (inGame)
                {
                    State.Visible[p] = new byte[pud.Width * pud.Height];
                    State.Explored[p] = new byte[pud.Width * pud.Height];
                    State.Detected[p] = new byte[pud.Width * pud.Height];
                }

                // ALOW start-spells arrive pre-researched.
                if (pud.AllowSpellStart != null)
                {
                    uint start = pud.AllowSpellStart[p];
                    for (var u = UpgradeId.HolyVision; u <= UpgradeId.DeathAndDecay; u++)
                    {
                        int bit = TechTree.AlowSpellBit(u);
                        if (bit >= 0 && (start & (1u << bit)) != 0)
                            State.Players[p].Researched |= 1ul << (int)u;
                    }
                }
            }

            foreach (var entry in pud.Units)
            {
                if (entry.Type == (byte)UnitTypeId.HumanStart || entry.Type == (byte)UnitTypeId.OrcStart)
                    continue;
                // Skip units of slots that are not playing (except neutral 15).
                if (entry.Owner < SimConstants.MaxPlayers && !State.Players[entry.Owner].InGame)
                    continue;

                var id = State.SpawnUnit(entry.Type, entry.Owner, entry.X, entry.Y);
                if (State.TryGetUnitIndex(id, out int idx))
                {
                    ref Unit u = ref State.Units[idx];
                    u.Hp = rules.Units[entry.Type].Hp;
                    if (rules.Units[entry.Type].Is(UnitTypeFlags.Building))
                        u.Flags |= UnitFlags.Building;
                    if (rules.Units[entry.Type].Is(UnitTypeFlags.GoldMine | UnitTypeFlags.OilPatch))
                        u.ResourceAmount = entry.Alter * 2500;
                    u.Facing = (byte)(State.Rng.Next(8)); // original: random idle facing
                }
            }

            RecountFood(); // food gates must be valid before the first command
            TickFog();     // starting bases are visible before the first tick
        }

        /// <summary>
        /// Advance one tick. commands may be empty; when present they were
        /// scheduled for this tick by the lockstep driver and are applied
        /// first, in list order (driver sorts them canonically).
        /// </summary>
        public void Advance(IReadOnlyList<GameCommand> commands)
        {
            State.TileChanges.Clear();
            State.Events.Clear();

            if (commands != null)
            {
                for (int i = 0; i < commands.Count; i++)
                    ApplyCommand(commands[i]);
            }

            // Fixed system order — the spine of determinism.
            TickProduction();
            TickCritters();   // before movement, so a new wander starts this tick
            TickMovement();
            TickCombat();
            TickHarvest();
            TickTransport();
            TickConstruction();
            TickFog();
            TickVictory();

            State.Tick++;
        }

        /// <summary>Queue a presentation event. Write-only channel — see SimEvent.</summary>
        void Emit(SimEventKind kind, byte player, ushort a, ushort b, uint unit = 0)
        {
            State.Events.Add(new SimEvent
            {
                Kind = kind,
                Player = player,
                A = a,
                B = b,
                UnitPacked = unit,
            });
        }

        unsafe void ApplyCommand(in GameCommand cmd)
        {
            switch (cmd.Op)
            {
                // Concede. A command rather than a UI action so it travels the
                // lockstep path like everything else and lands on the same tick
                // for every peer. Emitting here rather than leaving it to
                // TickVictory keeps the announcement immediate; the latch there
                // then skips this slot, so it is still announced exactly once.
                case CommandOp.Surrender:
                    if (cmd.Player < SimConstants.MaxPlayers
                        && State.Players[cmd.Player].Outcome == PlayerOutcome.Playing)
                    {
                        State.Players[cmd.Player].Outcome = PlayerOutcome.Defeated;
                        Emit(SimEventKind.PlayerDefeated, cmd.Player, 0, 0);
                    }
                    break;

                case CommandOp.Move:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        var id = UnitId.FromPacked(cmd.Selection.Ids[i]);
                        if (!State.TryGetUnitIndex(id, out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        // Only the owner's units obey (neutral/buildings don't move).
                        if (u.Player != cmd.Player || UnitSpeeds.Get(u.TypeId) == 0)
                            continue;
                        u.Order = OrderType.Move;
                        u.OrderX = cmd.TargetX;
                        u.OrderY = cmd.TargetY;
                        u.PathLength = 0;   // force repath; current step finishes first
                        u.PathCursor = 0;
                        u.WaitTicks = 0;
                    }
                    break;

                case CommandOp.Attack:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        var id = UnitId.FromPacked(cmd.Selection.Ids[i]);
                        if (!State.TryGetUnitIndex(id, out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player ||
                            !State.Rules.Units[u.TypeId].Is(UnitTypeFlags.CanAttack))
                            continue;
                        u.Order = OrderType.Attack;
                        u.AttackTarget = cmd.TargetUnit;
                        u.ChaseX = 0xFFFF; // force chase-path refresh
                        u.PathLength = 0;
                        u.PathCursor = 0;
                        u.WaitTicks = 0;
                    }
                    break;

                case CommandOp.AttackMove:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        var id = UnitId.FromPacked(cmd.Selection.Ids[i]);
                        if (!State.TryGetUnitIndex(id, out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player || UnitSpeeds.Get(u.TypeId) == 0)
                            continue;
                        u.Order = OrderType.AttackMove;
                        u.OrderX = cmd.TargetX;
                        u.OrderY = cmd.TargetY;
                        u.GoalX = cmd.TargetX;
                        u.GoalY = cmd.TargetY;
                        u.AttackTarget = 0;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                        u.WaitTicks = 0;
                    }
                    break;

                case CommandOp.Patrol:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        var id = UnitId.FromPacked(cmd.Selection.Ids[i]);
                        if (!State.TryGetUnitIndex(id, out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player || UnitSpeeds.Get(u.TypeId) == 0)
                            continue;
                        // The beat runs between where the unit stands now and
                        // the clicked tile; arrival swaps the two ends.
                        u.Order = OrderType.Patrol;
                        u.OrderX = cmd.TargetX;
                        u.OrderY = cmd.TargetY;
                        u.GoalX = u.TileX;
                        u.GoalY = u.TileY;
                        u.AttackTarget = 0;
                        u.Harvest = HarvestStage.None;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                        u.WaitTicks = 0;
                    }
                    break;

                case CommandOp.Stop:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        var id = UnitId.FromPacked(cmd.Selection.Ids[i]);
                        if (!State.TryGetUnitIndex(id, out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player || (u.Flags & UnitFlags.Hidden) != 0)
                            continue;
                        u.Order = OrderType.None;
                        u.AttackTarget = 0;
                        u.Harvest = HarvestStage.None;
                        u.PathLength = 0;
                    }
                    break;

                case CommandOp.Harvest:
                case CommandOp.Build:
                case CommandOp.Train:
                case CommandOp.SetRally:
                    ApplyEconomyCommand(cmd);
                    break;

                case CommandOp.Research:
                    ApplyResearchCommand(cmd);
                    break;

                case CommandOp.Cancel:
                    ApplyCancelCommand(cmd);
                    break;

                case CommandOp.Repair:
                    ApplyRepairCommand(cmd);
                    break;

                case CommandOp.Board:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]),
                                out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player || UnitSpeeds.Get(u.TypeId) == 0)
                            continue;
                        if (cmd.TargetUnit == 0
                            || !State.TryGetUnitIndex(UnitId.FromPacked(cmd.TargetUnit), out int ti))
                            continue;
                        if (!CanBoard(ref u, ref State.Units[ti]))
                            continue;
                        u.Order = OrderType.Board;
                        u.ResourceTarget = cmd.TargetUnit;
                        u.AttackTarget = 0;
                        // Park the walk order on our own tile: movement runs
                        // before TickTransport picks the real destination.
                        u.OrderX = (ushort)(u.TileX + u.StepDX);
                        u.OrderY = (ushort)(u.TileY + u.StepDY);
                        u.PathLength = 0;
                        u.PathCursor = 0;
                        u.WaitTicks = 0;
                    }
                    break;

                case CommandOp.Unload:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        if (!State.TryGetUnitIndex(UnitId.FromPacked(cmd.Selection.Ids[i]),
                                out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player
                            || !State.Rules.Units[u.TypeId].Is(UnitTypeFlags.Transport))
                            continue;
                        u.Order = OrderType.Unload;
                        u.OrderX = cmd.TargetX;
                        u.OrderY = cmd.TargetY;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                        u.WaitTicks = 0;
                    }
                    break;
            }
        }

        /// <summary>
        /// Flip a patrolling unit to the other end of its beat. GoalX/Y holds
        /// the far end while OrderX/Y holds the leg in progress, so the march
        /// is a single swap with a forced repath.
        /// </summary>
        static void SwapPatrolLegs(ref Unit u)
        {
            (u.OrderX, u.GoalX) = (u.GoalX, u.OrderX);
            (u.OrderY, u.GoalY) = (u.GoalY, u.OrderY);
            u.PathLength = 0;
            u.PathCursor = 0;
        }

        void TickMovement()
        {
            _repathsThisTick = 0;
            int w = State.Terrain?.Width ?? 0;
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive || (u.Flags & UnitFlags.Hidden) != 0)
                    continue;

                // Finish an in-flight tile step regardless of order changes.
                if (u.StepRemaining > 0)
                {
                    StepPixels(ref u, i);
                    continue;
                }

                if (u.Order == OrderType.None || UnitSpeeds.Get(u.TypeId) == 0)
                    continue;

                // Engaged and already in range: stand and fight. TickCombat
                // clears the path every tick for exactly this case, but it runs
                // AFTER movement, so without the gate here the unit takes one
                // step toward its target every tick and shuffles on the spot.
                if (u.AttackTarget != 0 && EngagedInRange(ref u))
                {
                    u.PathLength = 0;
                    u.PathCursor = 0;
                    u.WaitTicks = 0;
                    continue;
                }

                if (u.TileX == u.OrderX && u.TileY == u.OrderY)
                {
                    // Arrival completes Move/AttackMove only; Attack keeps
                    // chasing, Patrol turns around, and Harvest/Build have
                    // their own stage logic.
                    if (u.Order == OrderType.Move || u.Order == OrderType.AttackMove)
                        u.Order = OrderType.None;
                    else if (u.Order == OrderType.Patrol)
                        SwapPatrolLegs(ref u);
                    u.PathLength = 0;
                    continue;
                }

                if (u.PathCursor >= u.PathLength)
                {
                    if (!BudgetRepath(ref u, i, false, out bool ran))
                    {
                        // Budget spent this tick: keep the order and try again
                        // next tick rather than abandoning the move.
                        if (!ran)
                            continue;
                        // A search ran and found nothing closer. Harvest/Build/
                        // Repair keep their order — their stage logic decides
                        // based on adjacency.
                        if (u.Order != OrderType.Harvest && u.Order != OrderType.Build
                            && u.Order != OrderType.Repair)
                            u.Order = OrderType.None;
                        continue;
                    }
                }

                ushort nextPacked = State.UnitPaths[i][u.PathCursor];
                int nx = nextPacked % w;
                int ny = nextPacked / w;
                var id = new UnitId((ushort)i, u.Gen);

                if (!State.FootprintFree(id, u.TypeId, nx, ny))
                {
                    // Blocked on the final step: the destination is taken —
                    // this is as close as it gets (gathering behavior). For
                    // Attack orders the blocker is usually the target itself;
                    // stay put and let combat take over.
                    if (u.PathCursor >= u.PathLength - 1)
                    {
                        // Move/AttackMove: as close as it gets. Attack/Harvest/
                        // Build: the blocker is usually the destination itself
                        // (target, mine, depot); stay put, stage logic decides.
                        if (u.Order == OrderType.Move || u.Order == OrderType.AttackMove)
                            u.Order = OrderType.None;
                        else if (u.Order == OrderType.Patrol)
                            SwapPatrolLegs(ref u); // blocked end: turn around
                        u.PathLength = 0;
                        continue;
                    }
                    // Escalating recovery: wait briefly (blocker may move),
                    // then replan around idle blockers, then replan treating
                    // every stationary unit as a wall (livelock escape), and
                    // finally give up. WaitTicks resets only on a real step.
                    u.WaitTicks++;
                    if (u.WaitTicks == 4 || u.WaitTicks == 12)
                    {
                        // Best-effort; if the budget is spent, deferring to a
                        // later tick is fine — WaitTicks keeps escalating.
                        BudgetRepath(ref u, i, false, out _);
                    }
                    else if (u.WaitTicks == 20)
                    {
                        // Only give up if a strict search actually ran and failed;
                        // a budget deferral must not abandon the order.
                        if (!BudgetRepath(ref u, i, true, out bool ran20) && ran20)
                            u.Order = OrderType.None;
                    }
                    else if (u.WaitTicks >= 32)
                    {
                        // Full reset: fresh plan next tick. Truly boxed-in
                        // units still terminate via the strict-repath failure.
                        u.WaitTicks = 0;
                        u.PathLength = 0;
                        u.PathCursor = 0;
                    }
                    continue;
                }

                // Reserve destination, start the step (32 px, diagonal included
                // — the original's diagonal-speed quirk, kept deliberately).
                u.WaitTicks = 0;
                u.PathCursor++;
                State.Occupy(id, u.TypeId, nx, ny);
                u.StepDX = (sbyte)(nx > u.TileX ? 1 : nx < u.TileX ? -1 : 0);
                u.StepDY = (sbyte)(ny > u.TileY ? 1 : ny < u.TileY ? -1 : 0);
                u.StepRemaining = (byte)SimConstants.TilePixels;
                u.Facing = FacingFrom(u.StepDX, u.StepDY);
                StepPixels(ref u, i);
            }
        }

        /// <summary>N=0, then clockwise: NE=1, E=2, SE=3, S=4, SW=5, W=6, NW=7.</summary>
        public static byte FacingFrom(int dx, int dy)
        {
            // dy is map-down positive (south).
            if (dx == 0) return dy < 0 ? (byte)0 : (byte)4;
            if (dx > 0) return dy < 0 ? (byte)1 : dy == 0 ? (byte)2 : (byte)3;
            return dy < 0 ? (byte)7 : dy == 0 ? (byte)6 : (byte)5;
        }

        void StepPixels(ref Unit u, int index)
        {
            // Integer speed: Speed/10 tiles per second at 50 ticks/sec.
            // accum += Speed*TilePixels per tick; 1 px per 500 accumulated.
            u.MoveAccum += UnitSpeeds.Get(u.TypeId) * SimConstants.TilePixels;
            int pixels = u.MoveAccum / 500;
            u.MoveAccum -= pixels * 500;
            if (pixels > u.StepRemaining)
                pixels = u.StepRemaining;
            if (pixels <= 0)
                return;

            u.PixX += u.StepDX * pixels;
            u.PixY += u.StepDY * pixels;
            u.StepRemaining -= (byte)pixels;

            if (u.StepRemaining == 0)
            {
                var id = new UnitId((ushort)index, u.Gen);
                int oldX = u.TileX, oldY = u.TileY;
                u.TileX = (ushort)(u.TileX + u.StepDX);
                u.TileY = (ushort)(u.TileY + u.StepDY);
                State.Vacate(id, u.TypeId, oldX, oldY);
                u.StepDX = 0;
                u.StepDY = 0;
                u.MoveAccum = 0;
            }
        }

        /// <summary>
        /// Budget-gated wrapper around <see cref="Repath"/>. <paramref name="ran"/>
        /// reports whether a search actually executed; the return value is the
        /// Repath result and is only meaningful when a search ran. When the
        /// per-tick budget is exhausted no search runs (ran = false, returns
        /// false) and the caller must DEFER — keep the order and retry next tick
        /// — rather than treat it as an unreachable failure.
        /// </summary>
        bool BudgetRepath(ref Unit u, int index, bool strict, out bool ran)
        {
            if (_repathsThisTick >= MaxRepathsPerTick)
            {
                ran = false;
                return false;
            }
            _repathsThisTick++;
            ran = true;
            return Repath(ref u, index, strict);
        }

        bool Repath(ref Unit u, int index, bool strict = false)
        {
            MoveDomain domain = State.DomainOf(u.TypeId);
            int size = State.Footprint(u.TypeId);
            // Clamp an unreachable goal to the nearest tile the unit can actually
            // stand on before spending a search on it. u.OrderX/Y stays the true
            // goal; we only path to the clamped point for this search.
            ClampGoalToRegion(domain, u.TileX, u.TileY, u.OrderX, u.OrderY,
                out int tx, out int ty);
            int steps = _pathfinder.FindPath(domain, size, u.TileX, u.TileY, tx, ty,
                _pathScratch, new UnitId((ushort)index, u.Gen).Packed, strict);
            if (steps == 0)
                return false;

            var path = State.UnitPaths[index];
            if (path == null || path.Length < steps)
                State.UnitPaths[index] = path = new ushort[steps];
            for (int i = 0; i < steps; i++)
                path[i] = _pathScratch[i];
            u.PathLength = (ushort)steps;
            u.PathCursor = 0;
            return true;
        }

        /// <summary>
        /// If (gx,gy) is unreachable for this domain (a different terrain region
        /// than the unit sits in), walk the straight line back toward the unit and
        /// return the first same-region tile — mirroring the original's
        /// path_find_passable_target. Reachable goals pass through unchanged in
        /// O(1) (two region lookups), so the walk only runs in the case that would
        /// otherwise burn a full-budget A* toward somewhere the unit can never
        /// stand. If nothing on the line is reachable (the unit is boxed in) it
        /// returns the unit's own tile and FindPath then stops it cleanly.
        /// </summary>
        void ClampGoalToRegion(MoveDomain domain, int sx, int sy, int gx, int gy,
            out int cx, out int cy)
        {
            var terrain = State.Terrain;
            int startRegion = terrain != null ? terrain.RegionOf(domain, sx, sy) : 0;
            int goalRegion = terrain != null ? terrain.RegionOf(domain, gx, gy) : 0;
            // Only clamp when the goal is a genuinely different, reachable landmass
            // (a different non-zero region). A goal tile that is itself impassable
            // (region 0 — a tree, an occupied mine, a building's tile) is an
            // APPROACH target: leave it to A*'s closest-node partial path so
            // harvest / build / attack orders still stop adjacent to it. Clamping
            // those was pulling peons off their trees and livelocking the economy.
            if (startRegion == 0 || goalRegion == 0 || goalRegion == startRegion)
            {
                cx = gx;
                cy = gy;
                return;
            }

            int dx = gx > sx ? gx - sx : sx - gx;
            int dy = gy > sy ? gy - sy : sy - gy;
            int stepX = sx > gx ? 1 : -1;
            int stepY = sy > gy ? 1 : -1;
            int err = dx - dy;
            int x = gx, y = gy;
            while (true)
            {
                if (terrain.RegionOf(domain, x, y) == startRegion)
                {
                    cx = x;
                    cy = y;
                    return;
                }
                if (x == sx && y == sy)
                    break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += stepX; }
                if (e2 < dx) { err += dx; y += stepY; }
            }
            cx = sx; // nothing reachable along the line: stop in place
            cy = sy;
        }

        void TickConstruction() { }

        /// <summary>
        /// Resolve wins and losses. Runs once a second rather than every tick:
        /// a full unit scan is O(HighestUnitIndex) and, exactly as with fog
        /// (see the M6 note in PROGRESS.md), a scan cannot desync where an
        /// incrementally-maintained counter can — spawn, death, transport
        /// load/unload, hall tier swaps and unit transforms would each have to
        /// adjust it correctly, and one miss is a desync rather than a glitch.
        ///
        /// Outcomes are latched: once a slot is Defeated or Victorious it stays
        /// that way, so a transient "no units" tick during a transform cannot
        /// un-defeat anyone and the events fire exactly once.
        /// </summary>
        void TickVictory()
        {
            if (State.Tick % SimConstants.VictoryCheckTicks != 0)
                return;

            _victory.Evaluate(State, _outcomeScratch);

            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                if (State.Players[p].Outcome != PlayerOutcome.Playing)
                    continue; // already resolved — latch
                PlayerOutcome now = _outcomeScratch[p];
                if (now == PlayerOutcome.Playing)
                    continue;

                State.Players[p].Outcome = now;
                Emit(now == PlayerOutcome.Defeated
                        ? SimEventKind.PlayerDefeated
                        : SimEventKind.PlayerVictorious,
                    (byte)p, 0, 0);
            }
        }
    }
}
