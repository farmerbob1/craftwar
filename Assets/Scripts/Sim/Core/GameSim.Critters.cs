namespace Craftwar.Sim
{
    public sealed partial class GameSim
    {
        /// <summary>Tile delta per facing, N=0 clockwise to NW=7. dy is map-down.</summary>
        static readonly sbyte[] FacingDX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        static readonly sbyte[] FacingDY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        /// <summary>
        /// Critters mill about. This is the original's <c>fidget()</c>
        /// (PSX <c>ATTACK.C</c>), which runs on idle units and rolls once:
        ///
        ///     0        -> turn one facing anticlockwise
        ///     1..3     -> turn one facing clockwise
        ///     4..50    -> if it is a critter, step to a random adjacent tile
        ///                 (<c>sheep_try_move</c>, clamped to the map)
        ///     else       do nothing
        ///
        /// Only critters are fidgeted here. The original applies the turn part
        /// to every idle unit, but a soldier that quietly rotates on the spot
        /// would fight the facing that combat and harvesting set deliberately,
        /// so that half is left out rather than guessed at.
        ///
        /// Draws come from <see cref="GameState.Rng"/> and only for units that
        /// qualify on hashed state, so this stays reproducible: the same
        /// (map, seed, command log) still yields the same flock.
        /// </summary>
        void TickCritters()
        {
            if (State.Terrain == null)
                return;

            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive || (u.Flags & UnitFlags.Hidden) != 0)
                    continue;
                if (!IsCritter(u.TypeId))
                    continue;
                // Busy: finishing a step, or already walking somewhere.
                if (u.Order != OrderType.None || u.StepRemaining > 0)
                    continue;
                // Staggered by slot so a flock does not twitch in lockstep, and
                // so the roll costs one pass per critter per period, not per tick.
                if ((State.Tick + i) % SimConstants.CritterFidgetTicks != 0)
                    continue;

                int r = (int)State.Rng.Next(256);
                if (r == 0)
                    u.Facing = (byte)((u.Facing + 7) & 7);
                else if (r <= 3)
                    u.Facing = (byte)((u.Facing + 1) & 7);
                else if (r <= 50)
                    CritterWander(ref u);
            }
        }

        /// <summary>
        /// Whether a type is wildlife.
        ///
        /// UDTA only sets the critter bit on 0x39 <see cref="UnitTypeId.Critter"/>
        /// — the one real maps place, and the one whose art the tileset picks
        /// (sheep in Forest, seal in Winter, boar in Wasteland, hog in Swamp).
        /// The four named species at 0x69-0x6c are map-editor rows: none carries
        /// the critter bit and three are flagged Building, which is plainly a
        /// data artefact of those slots being reused as size templates. They are
        /// accepted here by id so a map that does place one still gets an
        /// animal, and the Building check keeps that leniency from ever
        /// animating something that really is a structure.
        /// </summary>
        bool IsCritter(ushort typeId)
        {
            ref UnitTypeData row = ref State.Rules.Units[typeId];
            if (row.Is(UnitTypeFlags.Building))
                return false;
            if (row.Is(UnitTypeFlags.Critter))
                return true;
            return typeId == (ushort)UnitTypeId.CritterSheep
                || typeId == (ushort)UnitTypeId.CritterPig
                || typeId == (ushort)UnitTypeId.CritterSeal
                || typeId == (ushort)UnitTypeId.CritterRedPig;
        }

        /// <summary>
        /// Pick a random direction and walk one tile that way, clamped to the
        /// map. Ordinary movement does the rest — including refusing an
        /// occupied or impassable tile, so a sheep cannot wander into the sea.
        /// </summary>
        void CritterWander(ref Unit u)
        {
            int facing = (int)State.Rng.Next(8);
            int nx = u.TileX + FacingDX[facing];
            int ny = u.TileY + FacingDY[facing];
            if (nx < 0) nx = 0;
            else if (nx >= State.Terrain.Width) nx = State.Terrain.Width - 1;
            if (ny < 0) ny = 0;
            else if (ny >= State.Terrain.Height) ny = State.Terrain.Height - 1;
            if (nx == u.TileX && ny == u.TileY)
                return;

            u.Order = OrderType.Move;
            u.OrderX = (ushort)nx;
            u.OrderY = (ushort)ny;
            u.PathLength = 0;
            u.PathCursor = 0;
            u.WaitTicks = 0;
        }
    }
}
