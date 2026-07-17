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
    public sealed class GameSim
    {
        public readonly GameState State;
        Pathfinder _pathfinder;

        // Scratch path buffer reused by every search this tick.
        ushort[] _pathScratch;

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
        {
            State.Rules = rules;
            State.Terrain = TerrainMap.FromPud(pud);
            State.OccupancySurface = new uint[pud.Width * pud.Height];
            State.OccupancyAir = new uint[pud.Width * pud.Height];
            _pathfinder = new Pathfinder(State.Terrain, State);
            _pathScratch = new ushort[pud.Width * pud.Height];

            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                byte owner = pud.Owner[p];
                bool inGame = owner == (byte)PudOwner.Human || owner == (byte)PudOwner.Computer
                    || owner == (byte)PudOwner.PassiveComputer || owner == (byte)PudOwner.RescuePassive
                    || owner == (byte)PudOwner.RescueActive;
                State.Players[p] = new PlayerState
                {
                    InGame = inGame,
                    Race = pud.Side[p] <= 2 ? (Race)pud.Side[p] : Race.Neutral,
                    Gold = pud.StartGold[p],
                    Lumber = pud.StartLumber[p],
                    Oil = pud.StartOil[p],
                };
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
                    u.Facing = (byte)(State.Rng.Next(8)); // original: random idle facing
                }
            }
        }

        /// <summary>
        /// Advance one tick. commands may be empty; when present they were
        /// scheduled for this tick by the lockstep driver and are applied
        /// first, in list order (driver sorts them canonically).
        /// </summary>
        public void Advance(IReadOnlyList<GameCommand> commands)
        {
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
            TickConstruction();
            TickFog();
            TickVictory();

            State.Tick++;
        }

        unsafe void ApplyCommand(in GameCommand cmd)
        {
            switch (cmd.Op)
            {
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

                case CommandOp.Stop:
                    for (int i = 0; i < cmd.SelectionCount; i++)
                    {
                        var id = UnitId.FromPacked(cmd.Selection.Ids[i]);
                        if (!State.TryGetUnitIndex(id, out int idx))
                            continue;
                        ref Unit u = ref State.Units[idx];
                        if (u.Player != cmd.Player)
                            continue;
                        u.Order = OrderType.None;
                        u.PathLength = 0;
                    }
                    break;
            }
        }

        void TickProduction() { }

        void TickMovement()
        {
            int w = State.Terrain?.Width ?? 0;
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive)
                    continue;

                // Finish an in-flight tile step regardless of order changes.
                if (u.StepRemaining > 0)
                {
                    StepPixels(ref u, i);
                    continue;
                }

                if (u.Order != OrderType.Move)
                    continue;

                if (u.TileX == u.OrderX && u.TileY == u.OrderY)
                {
                    u.Order = OrderType.None;
                    u.PathLength = 0;
                    continue;
                }

                if (u.PathCursor >= u.PathLength)
                {
                    if (!Repath(ref u, i))
                    {
                        u.Order = OrderType.None; // nowhere closer to go
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
                    // this is as close as it gets (gathering behavior).
                    if (u.PathCursor >= u.PathLength - 1)
                    {
                        u.Order = OrderType.None;
                        u.PathLength = 0;
                        continue;
                    }
                    // Escalating recovery: wait briefly (blocker may move),
                    // then replan around idle blockers, then replan treating
                    // every stationary unit as a wall (livelock escape), and
                    // finally give up. WaitTicks resets only on a real step.
                    u.WaitTicks++;
                    if (u.WaitTicks == 10 || u.WaitTicks == 20)
                    {
                        Repath(ref u, i, strict: false);
                    }
                    else if (u.WaitTicks == 30)
                    {
                        if (!Repath(ref u, i, strict: true))
                            u.Order = OrderType.None;
                    }
                    else if (u.WaitTicks >= 45)
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
            var rules = State.Rules;
            MoveDomain domain = rules.Units[u.TypeId].MoveDomain switch
            {
                1 => MoveDomain.Air,
                2 => MoveDomain.Sea,
                _ => MoveDomain.Land,
            };
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

        void TickCombat() { }
        void TickHarvest() { }
        void TickConstruction() { }
        void TickFog() { }
        void TickVictory() { }
    }
}
