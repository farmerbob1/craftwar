namespace Craftwar.Sim
{
    /// <summary>
    /// PCG-XSH-RR 32-bit deterministic PRNG (www.pcg-random.org, Apache-2.0/MIT).
    /// The single source of gameplay randomness. Every draw mutates sim state,
    /// so all clients in lockstep consume the stream identically.
    /// </summary>
    public struct Pcg32
    {
        public ulong State;
        public ulong Inc;

        const ulong Multiplier = 6364136223846793005ul;

        public Pcg32(ulong initState, ulong initSeq)
        {
            State = 0;
            Inc = (initSeq << 1) | 1ul;
            NextUInt();
            State += initState;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong oldState = State;
            State = oldState * Multiplier + Inc;
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            return (xorShifted >> rot) | (xorShifted << (-rot & 31));
        }

        /// <summary>Uniform value in [0, bound). Debiased via threshold rejection.</summary>
        public uint NextUInt(uint bound)
        {
            if (bound <= 1)
                return 0;
            uint threshold = (uint)(-bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                    return r % bound;
            }
        }

        /// <summary>Uniform value in [0, bound). bound must be positive.</summary>
        public int Next(int bound) => (int)NextUInt((uint)bound);

        /// <summary>Uniform value in [min, max] inclusive.</summary>
        public int Range(int min, int max) => min + Next(max - min + 1);
    }
}
