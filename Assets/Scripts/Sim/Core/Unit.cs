namespace Craftwar.Sim
{
    public enum UnitFlags : ushort
    {
        None = 0,
        Alive = 1 << 0,
        Building = 1 << 1,
        Hidden = 1 << 2,            // inside a mine/depot/construction site
        UnderConstruction = 1 << 3,
    }

    public enum HarvestStage : byte
    {
        None = 0,
        ToMine,
        InMine,
        ToDepot,
        InDepot,
        ToWood,
        Chopping,
    }

    public enum CarryType : byte
    {
        None = 0,
        Gold = 1,
        Wood = 2,
    }

    public enum OrderType : byte
    {
        None = 0,
        Move = 1,
        Attack = 2,      // explicit target (AttackTarget)
        AttackMove = 3,  // move to OrderX/Y, engaging anything on the way
        Harvest = 4,     // gold/wood cycle (Unit.Harvest holds the stage)
        Build = 5,       // walk to site, erect Unit.BuildType
        Repair = 6,      // walk to building (ResourceTarget), hammer HP back
    }

    /// <summary>
    /// One entity slot (units AND buildings, per the original model).
    /// Array-of-structs in GameState.Units; refer to slots via UnitId handles.
    /// Coordinates follow the original CELL.H model: a tile ("matrix") coord
    /// plus an absolute integer pixel coord (32 px per tile) used for
    /// intra-tile movement. Integer-only, always.
    /// </summary>
    public struct Unit
    {
        public ushort Gen;          // generation of this slot; 0 = never used
        public UnitFlags Flags;
        public ushort TypeId;       // index into the unit-type table (PUD unit type ids)
        public byte Player;         // 0-7, 15 = neutral
        public byte Facing;         // 0-7, N/NE/E/SE/S/SW/W/NW like the original

        public ushort TileX;
        public ushort TileY;
        public int PixX;            // absolute pixel coords (tile * 32 + offset)
        public int PixY;

        public int Hp;

        // Active order + movement execution state
        public OrderType Order;
        public ushort OrderX;
        public ushort OrderY;
        public ushort PathCursor;    // next index into GameState.UnitPaths[slot]
        public ushort PathLength;
        public int MoveAccum;        // integer speed accumulator
        public byte StepRemaining;   // pixels left in the current tile step
        public sbyte StepDX;         // -1/0/1 per axis while stepping
        public sbyte StepDY;
        public byte WaitTicks;       // blocked-tile backoff

        // Combat
        public uint AttackTarget;    // UnitId.Packed of engaged enemy, 0 = none
        public byte Cooldown;        // ticks until next attack
        public ushort ChaseX;        // target's tile when we last pathed to it
        public ushort ChaseY;
        public ushort GoalX;         // attack-move final destination (resumed after kills)
        public ushort GoalY;

        // Economy
        public HarvestStage Harvest;
        public CarryType Carry;
        public ushort Timer;         // generic stage timer (in-mine, chopping, deposit)
        public uint ResourceTarget;  // mine UnitId.Packed, or (0x8000_0000 | tileIndex) for wood
        public int ResourceAmount;   // mines/patches: remaining resources
        public ushort BuildType;     // peasant: queued building type; building: training type
                                     // (a building type here = self-upgrade in progress)
        public ushort TrainTicks;    // building: ticks left on training/construction/research
        public byte ResearchId;      // building: UpgradeId + 1 being researched, 0 = none
        public ushort RallyX;
        public ushort RallyY;

        public bool IsAlive => (Flags & UnitFlags.Alive) != 0;
        public bool IsMoving => StepRemaining > 0;

        public void HashInto(ref StateHash h)
        {
            h.Add(Gen);
            h.Add((ushort)Flags);
            h.Add(TypeId);
            h.Add(Player);
            h.Add(Facing);
            h.Add(TileX);
            h.Add(TileY);
            h.Add(PixX);
            h.Add(PixY);
            h.Add(Hp);
            h.Add((byte)Order);
            h.Add(OrderX);
            h.Add(OrderY);
            h.Add(PathCursor);
            h.Add(PathLength);
            h.Add(MoveAccum);
            h.Add(StepRemaining);
            h.Add((byte)StepDX);
            h.Add((byte)StepDY);
            h.Add(WaitTicks);
            h.Add(AttackTarget);
            h.Add(Cooldown);
            h.Add(ChaseX);
            h.Add(ChaseY);
            h.Add(GoalX);
            h.Add(GoalY);
            h.Add((byte)Harvest);
            h.Add((byte)Carry);
            h.Add(Timer);
            h.Add(ResourceTarget);
            h.Add(ResourceAmount);
            h.Add(BuildType);
            h.Add(TrainTicks);
            h.Add(ResearchId);
            h.Add(RallyX);
            h.Add(RallyY);
        }
    }
}
