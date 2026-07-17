using System;

namespace Craftwar.Sim
{
    /// <summary>
    /// Generational handle into GameState.Units. Index alone would go stale
    /// when a slot is recycled; the generation makes stale handles detectable.
    /// </summary>
    public readonly struct UnitId : IEquatable<UnitId>
    {
        public readonly ushort Index;
        public readonly ushort Gen;

        public UnitId(ushort index, ushort gen)
        {
            Index = index;
            Gen = gen;
        }

        public static readonly UnitId None = default;

        public bool IsNone => Gen == 0;

        public uint Packed => ((uint)Gen << 16) | Index;

        public static UnitId FromPacked(uint packed) => new UnitId((ushort)packed, (ushort)(packed >> 16));

        public bool Equals(UnitId other) => Index == other.Index && Gen == other.Gen;
        public override bool Equals(object obj) => obj is UnitId other && Equals(other);
        public override int GetHashCode() => (int)Packed;
        public static bool operator ==(UnitId a, UnitId b) => a.Equals(b);
        public static bool operator !=(UnitId a, UnitId b) => !a.Equals(b);
        public override string ToString() => IsNone ? "UnitId.None" : $"UnitId({Index}:{Gen})";
    }
}
