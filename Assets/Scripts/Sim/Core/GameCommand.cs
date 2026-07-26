namespace Craftwar.Sim
{
    public enum CommandOp : byte
    {
        None = 0,
        Move,
        Attack,
        AttackMove,
        Stop,
        Patrol,
        Harvest,
        Repair,
        Build,
        Train,
        Research,
        Cancel,
        SetRally,
        Board,
        Unload,
        Cast,
        /// <summary>Concede. Appended, not inserted — these values are the wire
        /// format and are baked into every existing replay.</summary>
        Surrender,
    }

    /// <summary>
    /// A player intent, the only way anything mutates the sim. Tick-stamped by
    /// the lockstep driver and executed at turn boundaries on all peers alike.
    /// Kept compact: this exact byte layout goes over the wire and into replays.
    /// </summary>
    public struct GameCommand
    {
        public const int MaxSelection = 18;

        /// <summary>Largest serialized size: 13 fixed bytes + a full selection.</summary>
        public const int MaxWireBytes = 13 + MaxSelection * 4;

        public CommandOp Op;
        public byte Player;
        public ushort TargetX;        // tile coords for positional orders
        public ushort TargetY;
        public uint TargetUnit;       // UnitId.Packed, 0 = none
        public ushort Param;          // type id for Build/Train/Research, spell id for Cast
        public byte SelectionCount;
        public unsafe struct SelectionArray
        {
            public fixed uint Ids[MaxSelection];
        }
        public SelectionArray Selection;

        public unsafe void Write(ref ByteWriter w)
        {
            w.WriteByte((byte)Op);
            w.WriteByte(Player);
            w.WriteUShort(TargetX);
            w.WriteUShort(TargetY);
            w.WriteUInt(TargetUnit);
            w.WriteUShort(Param);
            w.WriteByte(SelectionCount);
            for (int i = 0; i < SelectionCount; i++)
                w.WriteUInt(Selection.Ids[i]);
        }

        public static unsafe GameCommand Read(ref ByteReader r)
        {
            var cmd = new GameCommand
            {
                Op = (CommandOp)r.ReadByte(),
                Player = r.ReadByte(),
                TargetX = r.ReadUShort(),
                TargetY = r.ReadUShort(),
                TargetUnit = r.ReadUInt(),
                Param = r.ReadUShort(),
                SelectionCount = r.ReadByte(),
            };
            if (cmd.SelectionCount > MaxSelection)
                throw new System.IO.InvalidDataException($"Corrupt command: selection count {cmd.SelectionCount}");
            for (int i = 0; i < cmd.SelectionCount; i++)
                cmd.Selection.Ids[i] = r.ReadUInt();
            return cmd;
        }
    }
}
