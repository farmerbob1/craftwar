namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Difficulty tier. Skill parameters (think cadence, competence toggles) and
    /// optional handicaps are attached in Phase B (an AiTierTable); Phase A only
    /// needs the identity so a strategy can declare its default tier and the value
    /// can round-trip through the canonical binary form.
    /// </summary>
    public enum AiTier : byte
    {
        Dumb = 0,
        Normal = 1,
        Smart = 2,
        God = 3,
    }

    public static class AiTiers
    {
        /// <summary>Lower-case name → tier. Ordered switch, deterministic.</summary>
        public static bool TryParse(string s, out AiTier tier)
        {
            switch (s)
            {
                case "dumb": tier = AiTier.Dumb; return true;
                case "normal": tier = AiTier.Normal; return true;
                case "smart": tier = AiTier.Smart; return true;
                case "god": tier = AiTier.God; return true;
                default: tier = AiTier.Normal; return false;
            }
        }
    }
}
