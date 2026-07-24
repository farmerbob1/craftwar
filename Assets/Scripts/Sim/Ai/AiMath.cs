namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Deterministic fixed-point math for the utility AI. Q16.16 (a 32-bit int
    /// holding value * 65536) is the score/weight/curve type; utility scores live
    /// in [0, One]. Everything is integer and bit-reproducible — no float, no
    /// System.Math transcendentals — so it passes SimPurityTests and stays
    /// lockstep-safe. There is no shared FixedPoint type in the Sim yet; this is
    /// the AI's own, deliberately small.
    /// </summary>
    public static class AiMath
    {
        public const int FracBits = 16;
        /// <summary>1.0 in Q16.16.</summary>
        public const int One = 1 << FracBits;
        /// <summary>0.5 in Q16.16.</summary>
        public const int Half = One >> 1;

        public static int FromInt(int n) => n << FracBits;

        /// <summary>Floor of a Q16.16 value to a whole int.</summary>
        public static int ToInt(int f) => f >> FracBits;

        /// <summary>Round-to-nearest of a Q16.16 value to a whole int.</summary>
        public static int RoundToInt(int f) => (f + Half) >> FracBits;

        /// <summary>a * b in Q16.16, via a 64-bit intermediate so it never overflows.</summary>
        public static int Mul(int a, int b) => (int)(((long)a * b) >> FracBits);

        /// <summary>a / b in Q16.16. Returns 0 when b == 0 (callers guard ranges).</summary>
        public static int Div(int a, int b) =>
            b == 0 ? 0 : (int)(((long)a << FracBits) / b);

        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Clamp a Q16.16 value into [0, One].</summary>
        public static int Clamp01(int f) => Clamp(f, 0, One);

        /// <summary>Linear interpolate a→b by t (Q16.16 in [0,One]).</summary>
        public static int Lerp(int a, int b, int t) => a + Mul(b - a, t);

        /// <summary>
        /// Map a whole-number fact onto [0, One] as a Q16.16 fraction of the
        /// [min,max] range, clamped. min==max yields 0 (or One when value>=max).
        /// This is how raw sim facts (gold, unit counts, distances) become curve
        /// inputs.
        /// </summary>
        public static int Normalize(int value, int min, int max)
        {
            if (max <= min)
                return value >= max ? One : 0;
            if (value <= min) return 0;
            if (value >= max) return One;
            // (value-min)/(max-min) in Q16.16, exact via 64-bit shift.
            return (int)((((long)(value - min)) << FracBits) / (max - min));
        }

        /// <summary>
        /// Floor of the square root of a non-negative whole number, by the classic
        /// bitwise digit-by-digit method (no float, no Math.Sqrt). Negative input
        /// returns 0. Used for turning squared tile distances into tile distances.
        /// </summary>
        public static int Isqrt(int n)
        {
            if (n <= 0) return 0;
            uint x = (uint)n;
            uint res = 0;
            // Highest power of four <= x.
            uint bit = 1u << 30;
            while (bit > x) bit >>= 2;
            while (bit != 0)
            {
                if (x >= res + bit)
                {
                    x -= res + bit;
                    res = (res >> 1) + bit;
                }
                else
                {
                    res >>= 1;
                }
                bit >>= 2;
            }
            return (int)res;
        }

        /// <summary>64-bit input variant of <see cref="Isqrt(int)"/>.</summary>
        public static long Isqrt(long n)
        {
            if (n <= 0) return 0;
            ulong x = (ulong)n;
            ulong res = 0;
            ulong bit = 1UL << 62;
            while (bit > x) bit >>= 2;
            while (bit != 0)
            {
                if (x >= res + bit)
                {
                    x -= res + bit;
                    res = (res >> 1) + bit;
                }
                else
                {
                    res >>= 1;
                }
                bit >>= 2;
            }
            return (long)res;
        }

        /// <summary>Square root of a Q16.16 value, returned in Q16.16.
        /// sqrt(f/One) * One == Isqrt(f * One).</summary>
        public static int SqrtFixed(int f)
        {
            if (f <= 0) return 0;
            return (int)Isqrt(((long)f) << FracBits);
        }

        /// <summary>Tile distance between two tiles (floor of Euclidean), integer.</summary>
        public static int TileDistance(int ax, int ay, int bx, int by)
        {
            int dx = ax - bx, dy = ay - by;
            return Isqrt(dx * dx + dy * dy);
        }
    }
}
