using System.Collections.Generic;

namespace Craftwar.Sim.Ai.Utility
{
    public enum AiActionKind : byte
    {
        None = 0,
        BuildFarm,
        Build,
        TrainWorker,
        TrainUnit,
        UpgradeBuilding,
        Research,
        HarvestBalance,
        Expand,
        Scout,
        LaunchWave,
        AllIn,
        DefendBase,
        FocusFire,
        Reinforce,
    }

    /// <summary>
    /// One scored candidate the arbiter may execute. Kind + payload describe what
    /// to do; Score (Q16.16, weight×considerations) decides whether it wins. The
    /// command is prebuilt and ready to emit; PendingType/site let the executor
    /// register a build in the cost-reservation ledger. Seq is the deterministic
    /// tie-break so ranking never depends on sort stability.
    /// </summary>
    public struct UtilityAction
    {
        public AiActionKind Kind;
        public int Score;
        public GameCommand Command;
        public ushort PendingType; // building type to add to the pending ledger, 0 = none
        public ushort SiteX, SiteY;
        public int Seq;

        public static UtilityAction Make(AiActionKind kind, int score, in GameCommand cmd)
        {
            return new UtilityAction
            {
                Kind = kind,
                Score = score,
                Command = cmd,
            };
        }
    }

    /// <summary>
    /// Utility scoring math (Dave-Mark IAUS style) and deterministic ranking.
    /// Named <c>Util</c> (not <c>Utility</c>) so it does not collide with the
    /// enclosing <c>Craftwar.Sim.Ai.Utility</c> namespace at call sites.
    /// </summary>
    public static class Util
    {
        /// <summary>
        /// Multiply considerations with the compensation factor that stops many
        /// low-but-nonzero considerations from unfairly crushing a score:
        /// final = product + (1−product)·(1 − 1/n)·product. Inputs and result are
        /// Q16.16 in [0,1].
        /// </summary>
        public static int Compensate(int product, int n)
        {
            if (n <= 1)
                return product;
            int modFactor = AiMath.One - AiMath.Div(AiMath.One, AiMath.FromInt(n));
            int makeUp = AiMath.Mul(AiMath.One - product, modFactor);
            return product + AiMath.Mul(makeUp, product);
        }

        /// <summary>weight × compensated(considerations). weight is a Q16.16 priority
        /// multiplier (may exceed 1) that sets a domain's base standing.</summary>
        public static int Score(int weight, int c0) => AiMath.Mul(weight, c0);

        public static int Score(int weight, int c0, int c1) =>
            AiMath.Mul(weight, Compensate(AiMath.Mul(c0, c1), 2));

        public static int Score(int weight, int c0, int c1, int c2) =>
            AiMath.Mul(weight, Compensate(AiMath.Mul(AiMath.Mul(c0, c1), c2), 3));

        public static int Score(int weight, int c0, int c1, int c2, int c3) =>
            AiMath.Mul(weight,
                Compensate(AiMath.Mul(AiMath.Mul(AiMath.Mul(c0, c1), c2), c3), 4));

        /// <summary>Total order for ranking: higher score first, then lower Kind,
        /// then lower Seq. Total (never returns 0 for distinct actions) so
        /// List.Sort is deterministic regardless of stability.</summary>
        public static int Rank(UtilityAction a, UtilityAction b)
        {
            if (a.Score != b.Score) return b.Score.CompareTo(a.Score);
            if (a.Kind != b.Kind) return ((byte)a.Kind).CompareTo((byte)b.Kind);
            return a.Seq.CompareTo(b.Seq);
        }

        static readonly System.Comparison<UtilityAction> RankCmp = Rank;

        /// <summary>Sort candidates best-first, deterministically.</summary>
        public static void SortByScore(List<UtilityAction> actions) =>
            actions.Sort(RankCmp);
    }
}
