namespace Craftwar.Sim
{
    /// <summary>
    /// Transports. Land units board a ship, ride hidden, and disembark on a
    /// shore tile — the original gates unloading on SQ_SHORE (DISPATCH.C
    /// dispatch_unload_all) and caps a hold at MAX_MEN_IN_TRANSPORT.
    /// </summary>
    public partial class GameSim
    {
        public const int TransportCapacity = 6; // MAX_MEN_IN_TRANSPORT

        void TickTransport()
        {
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit u = ref State.Units[i];
                if (!u.IsAlive)
                    continue;

                if (u.Order == OrderType.Board && (u.Flags & UnitFlags.Hidden) == 0)
                    TickBoarding(ref u, i);
                else if (u.Order == OrderType.Unload)
                    TickUnloading(ref u, i);
            }
        }

        void TickBoarding(ref Unit u, int index)
        {
            if (!State.TryGetUnitIndex(UnitId.FromPacked(u.ResourceTarget), out int ti))
            {
                EndBoarding(ref u);
                return;
            }

            ref Unit ship = ref State.Units[ti];
            if (!CanBoard(ref u, ref ship))
            {
                EndBoarding(ref u);
                return;
            }

            if (u.StepRemaining != 0 || FootprintDistance(ref u, ref ship) > 1)
            {
                // A transport still out in open water is unreachable — coast is
                // not land-passable, so the troops would mill on the bank
                // forever. Give up the way the wood cycle does.
                if (u.StepRemaining == 0 && ++u.Timer > SimConstants.BoardStuckTicks)
                    EndBoarding(ref u);
                else
                    WalkToBuilding(ref u, ref ship);
                return;
            }
            u.Timer = 0;

            // Aboard: the passenger goes hidden exactly like a peasant in a mine,
            // and rides at the ship's tile so it unloads from wherever it lands.
            HideUnit(ref u, index);
            State.Vacate(new UnitId((ushort)index, u.Gen), u.TypeId, u.TileX, u.TileY);
            u.Order = OrderType.None;
            u.Transport = u.ResourceTarget;
            u.ResourceTarget = 0;
            ship.CargoCount++;
        }

        bool CanBoard(ref Unit passenger, ref Unit ship)
        {
            if (!ship.IsAlive || ship.Player != passenger.Player)
                return false;
            if (!State.Rules.Units[ship.TypeId].Is(UnitTypeFlags.Transport))
                return false;
            if (ship.CargoCount >= TransportCapacity)
                return false;
            // Only ground units ride; ships and flyers make their own way.
            return State.DomainOf(passenger.TypeId) == MoveDomain.Land;
        }

        static void EndBoarding(ref Unit u)
        {
            u.Order = OrderType.None;
            u.ResourceTarget = 0;
            u.PathLength = 0;
        }

        void TickUnloading(ref Unit ship, int shipIndex)
        {
            // Walk to the drop-off first; only a shore tile will do.
            if (ship.StepRemaining != 0)
                return;
            if (Chebyshev(ship.TileX, ship.TileY, ship.OrderX, ship.OrderY) > 1)
                return;

            if (!IsBeachable(ref ship))
            {
                Emit(SimEventKind.BuildSiteBlocked, ship.Player, 0, ship.TypeId);
                ship.Order = OrderType.None;
                ship.PathLength = 0;
                return;
            }

            var shipId = new UnitId((ushort)shipIndex, ship.Gen);
            // Slot order, so the hold empties identically on every machine.
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit p = ref State.Units[i];
                if (!p.IsAlive || p.Transport != shipId.Packed)
                    continue;
                if (!TryPutAshore(ref p, i, ref ship))
                    break; // beach is full; the rest stay aboard
                if (ship.CargoCount > 0)
                    ship.CargoCount--;
            }

            ship.Order = OrderType.None;
            ship.PathLength = 0;
        }

        /// <summary>Is the transport touching a shore tile it can disgorge onto?</summary>
        bool IsBeachable(ref Unit ship)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = ship.TileX + dx, y = ship.TileY + dy;
                    if (State.Terrain.IsShore(x, y) || State.Terrain.IsPassable(MoveDomain.Land, x, y))
                        return true;
                }
            return false;
        }

        bool TryPutAshore(ref Unit p, int index, ref Unit ship)
        {
            if (!TryFindSpawnTileNear(ref ship, p.TypeId, ship.TileX, ship.TileY,
                    out int sx, out int sy))
                return false;

            p.TileX = (ushort)sx;
            p.TileY = (ushort)sy;
            p.PixX = sx * SimConstants.TilePixels;
            p.PixY = sy * SimConstants.TilePixels;
            p.Flags &= ~UnitFlags.Hidden;
            p.Transport = 0;
            p.Order = OrderType.None;
            p.OrderX = p.TileX;
            p.OrderY = p.TileY;
            p.PathLength = 0;
            p.PathCursor = 0;
            p.StepRemaining = 0;
            p.StepDX = 0;
            p.StepDY = 0;
            State.Occupy(new UnitId((ushort)index, p.Gen), p.TypeId, sx, sy);
            return true;
        }

        /// <summary>A sinking transport takes its passengers down with it.</summary>
        void DrownCargo(UnitId shipId)
        {
            for (int i = 0; i < State.HighestUnitIndex; i++)
            {
                ref Unit p = ref State.Units[i];
                if (p.IsAlive && p.Transport == shipId.Packed)
                    State.DestroyUnit(new UnitId((ushort)i, p.Gen));
            }
        }
    }
}
