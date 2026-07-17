namespace Craftwar.Sim
{
    /// <summary>
    /// Incremental FNV-1a 32-bit hasher over a canonical walk of sim state.
    /// Used for desync detection between lockstep peers and for golden-replay
    /// tests. Not cryptographic; just needs to diverge fast on divergent state.
    /// </summary>
    public struct StateHash
    {
        public const uint OffsetBasis = 2166136261;
        const uint Prime = 16777619;

        public uint Value;

        public static StateHash Begin() => new StateHash { Value = OffsetBasis };

        public void Add(byte b)
        {
            Value = (Value ^ b) * Prime;
        }

        public void Add(ushort v)
        {
            Add((byte)v);
            Add((byte)(v >> 8));
        }

        public void Add(uint v)
        {
            Add((byte)v);
            Add((byte)(v >> 8));
            Add((byte)(v >> 16));
            Add((byte)(v >> 24));
        }

        public void Add(int v) => Add((uint)v);

        public void Add(ulong v)
        {
            Add((uint)v);
            Add((uint)(v >> 32));
        }
    }
}
