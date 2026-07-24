namespace Craftwar.Sim.Ai.Utility
{
    public enum CurveKind : byte
    {
        /// <summary>Ignores input; always yields <c>B</c>.</summary>
        Constant = 0,
        /// <summary>clamp01(B + A·x). A is slope (may be negative), B intercept.</summary>
        Linear = 1,
        /// <summary>clamp01(B + A·x²). Ease-in (A&gt;0) or ease-out shapes.</summary>
        Quadratic = 2,
        /// <summary>Smoothstep S-curve centred at B with steepness A (a soft,
        /// exp-free logistic — the workhorse for "enough / not enough" inputs).</summary>
        Logistic = 3,
        /// <summary>x ≥ B → 1, else 0. A hard gate.</summary>
        Step = 4,
    }

    /// <summary>
    /// A response curve: maps a normalized Q16.16 input in [0,1] to a Q16.16 score
    /// in [0,1]. The tunable heart of the utility AI — a modder authors these in the
    /// .ai profile (kind + two integer knobs) to reshape any decision without
    /// touching engine code. All integer, no transcendentals, so it is
    /// bit-reproducible and lockstep-safe.
    /// </summary>
    public struct ResponseCurve
    {
        public CurveKind Kind;
        public int A; // Q16.16 slope / steepness
        public int B; // Q16.16 intercept / midpoint / threshold

        public ResponseCurve(CurveKind kind, int a, int b)
        {
            Kind = kind;
            A = a;
            B = b;
        }

        public static ResponseCurve Constant(int value) =>
            new ResponseCurve(CurveKind.Constant, 0, value);

        public static ResponseCurve Linear(int slope, int intercept) =>
            new ResponseCurve(CurveKind.Linear, slope, intercept);

        public static ResponseCurve Logistic(int steepness, int midpoint) =>
            new ResponseCurve(CurveKind.Logistic, steepness, midpoint);

        public static ResponseCurve Step(int threshold) =>
            new ResponseCurve(CurveKind.Step, 0, threshold);

        /// <summary>Default identity: score == input.</summary>
        public static ResponseCurve Identity =>
            new ResponseCurve(CurveKind.Linear, AiMath.One, 0);

        /// <summary>Evaluate at input x (Q16.16, expected in [0,1]); returns Q16.16
        /// in [0,1].</summary>
        public int Eval(int x)
        {
            x = AiMath.Clamp01(x);
            switch (Kind)
            {
                case CurveKind.Constant:
                    return AiMath.Clamp01(B);
                case CurveKind.Linear:
                    return AiMath.Clamp01(B + AiMath.Mul(A, x));
                case CurveKind.Quadratic:
                    return AiMath.Clamp01(B + AiMath.Mul(A, AiMath.Mul(x, x)));
                case CurveKind.Step:
                    return x >= B ? AiMath.One : 0;
                case CurveKind.Logistic:
                {
                    // Soft S-curve: shift+scale around the midpoint B by steepness A,
                    // then smoothstep t²(3−2t). Exp-free but S-shaped.
                    int t = AiMath.Clamp01(AiMath.Half + AiMath.Mul(A, x - B));
                    int t2 = AiMath.Mul(t, t);
                    return AiMath.Mul(t2, 3 * AiMath.One - 2 * t);
                }
                default:
                    return x;
            }
        }
    }
}
