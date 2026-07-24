namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// Difficulty tier. Orthogonal to the strategy: any tier runs any strategy.
    /// A tier bundles two kinds of strength — SKILL (think cadence + competence
    /// toggles that drive the out-of-sim executor) and optional HANDICAP knobs
    /// (resource/vision bonuses applied INSIDE the sim, hashed and deterministic).
    /// The original WC2 AI had neither; higher tiers here exceed it by design.
    /// </summary>
    public enum AiTier : byte
    {
        Dumb = 0,
        Normal = 1,
        Smart = 2,
        God = 3,
    }

    /// <summary>
    /// Resolved parameters for a tier. Skill fields are consumed by the AiPlayer
    /// (cadence now; competence toggles as Phases C-E wire them). Handicap fields
    /// are read by the APP into <see cref="SlotSetup"/> at match start and applied
    /// by sim systems — never by the AiPlayer — so they cannot desync. Every
    /// handicap default is integer identity (0), so a match with no handicap is
    /// byte-for-byte the same as before this existed.
    /// </summary>
    public struct AiTierParams
    {
        // --- Skill (out-of-sim executor) ---
        /// <summary>AI think cadence in ticks; lower = sharper reactions.</summary>
        public int ThinkPeriodTicks;
        public bool PlannedLayout;   // Phase C: clustered placement vs naive spiral
        public bool FocusFire;       // Phase D: target lowest-HP in range
        public bool Reinforce;       // Phase D: feed new units into an ongoing push
        public bool RetargetWaves;   // Phase D: re-evaluate the wave target mid-attack
        public bool ActiveDefense;   // Phase D: recall the army when the base is hit
        public bool Scouting;        // Phase E: send an early scout
        public bool Expansion;       // Phase E: 2nd hall at a fresh mine

        // --- Handicap (sim-side, integer, identity at zero) ---
        public int StartGoldBonus;
        public int StartLumberBonus;
        public int HarvestBonusTenths; // +N/10 of every resource drop-off
        public int SightBonus;         // +N tiles of sight on every unit
    }

    public static class AiTierTable
    {
        /// <summary>
        /// The default gradient. Normal == the M9 baseline exactly (cadence 25,
        /// no competences, no handicaps) so the migration stays byte-identical.
        /// Handicap defaults are the menu's starting point; Phase F lets a player
        /// dial them (a purist can zero God's handicaps for a "smart-max" bot).
        /// </summary>
        public static AiTierParams For(AiTier tier) => tier switch
        {
            AiTier.Dumb => new AiTierParams
            {
                ThinkPeriodTicks = 50,
            },
            AiTier.Smart => new AiTierParams
            {
                // Kept in the healthy cadence band (see Normal): thinking much
                // faster than ~25 ticks currently develops WORSE, so Smart's edge
                // is its competences (active defense, focus fire, reinforcement,
                // scouting), not raw reaction speed.
                ThinkPeriodTicks = 25,
                PlannedLayout = true,
                FocusFire = true,
                Reinforce = true,
                RetargetWaves = true,
                ActiveDefense = true,
                Scouting = true,
            },
            AiTier.God => new AiTierParams
            {
                ThinkPeriodTicks = 22,
                PlannedLayout = true,
                FocusFire = true,
                Reinforce = true,
                RetargetWaves = true,
                ActiveDefense = true,
                Scouting = true,
                Expansion = true,
                StartGoldBonus = 2000,
                StartLumberBonus = 1000,
                HarvestBonusTenths = 3,
                SightBonus = 3,
            },
            _ => new AiTierParams // Normal — the M9 baseline
            {
                ThinkPeriodTicks = 25,
            },
        };
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
