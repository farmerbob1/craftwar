namespace Craftwar.Sim.Ai
{
    /// <summary>What a computer slot does with its units. M9 ships the melee
    /// land-attack script only; sea/air/campaign scripts are later work.</summary>
    public enum AiBehavior : byte
    {
        Passive = 0,
        LandAttack = 1,
    }

    public static class AiBehaviorMap
    {
        /// <summary>
        /// Collapse a PUD AIPL byte (spec Appendix C) onto what M9 implements:
        /// $01 is "passive"; everything else — land attack $00, sea $19,
        /// air $1A, the per-campaign scripts $02-$18 and expansion $20+ —
        /// plays the land-attack script.
        /// </summary>
        public static AiBehavior FromAiplByte(byte aipl) =>
            aipl == 0x01 ? AiBehavior.Passive : AiBehavior.LandAttack;
    }
}
