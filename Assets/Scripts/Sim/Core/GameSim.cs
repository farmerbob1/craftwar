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
            State.Tiles = (ushort[])pud.Tiles.Clone();
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
                    if (!Repath(ref u, i))
                    {
                        // Nowhere closer to go. Harvest/Build/Repair keep their
                        // order — their stage logic decides based on adjacency.
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
                        Repath(ref u, i, strict: false);
                    }
                    else if (u.WaitTicks == 20)
                    {
                        if (!Repath(ref u, i, strict: true))
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

        bool Repath(ref Unit u, int index, bool strict = false)
        {
            MoveDomain domain = State.DomainOf(u.TypeId);
            int size = State.Footprint(u.TypeId);
            int steps = _pathfinder.FindPath(domain, size, u.TileX, u.TileY, u.OrderX, u.OrderY,
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
