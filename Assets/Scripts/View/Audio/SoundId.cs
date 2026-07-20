namespace Craftwar.View
{
    /// <summary>
    /// Logical sound slots. These are catalog keys, not files: the provider
    /// decides what actually plays, so swapping placeholder tones for decoded
    /// WC2 audio at M8 needs no changes at any call site.
    /// </summary>
    public enum SoundId : byte
    {
        None = 0,

        // Acknowledgements (local client only — never driven by the sim).
        OrderMove,
        OrderAttack,
        OrderSelect,

        // Sim events.
        WorkComplete,      // building finished
        TrainComplete,
        ResearchComplete,
        UnderAttack,
        MineCollapsed,
        Denied,            // not enough gold/lumber/food, tech unavailable
        PlacementBlocked,
    }

    /// <summary>
    /// Resolves a logical sound to something playable. Mirrors
    /// IUnitSpriteProvider so the asset layer owns decoding, not the view.
    /// </summary>
    public interface IAudioProvider
    {
        /// <summary>Null when the sound has no asset yet; callers must cope.</summary>
        UnityEngine.AudioClip Get(SoundId id);

        /// <summary>
        /// How many recorded variants a unit has for one kind of line, or 0.
        ///
        /// The original picks at random from a set — SND_FIRST_WHAT..LAST_WHAT
        /// and SND_FIRST_YESSIR..LAST_YESSIR — and the installation bears that
        /// out, with anywhere from one to seven takes per unit. Exposing the
        /// count rather than a fixed number is what lets the caller draw fairly
        /// without the provider owning a random source.
        /// </summary>
        int UnitSoundVariants(Craftwar.Sim.UnitTypeId type, Craftwar.Sim.Race race,
                              UnitSoundKind kind);

        /// <summary>One specific take. Callers pick the index; see the note above.</summary>
        UnityEngine.AudioClip GetUnitSound(Craftwar.Sim.UnitTypeId type, Craftwar.Sim.Race race,
                                           UnitSoundKind kind, int variant);
    }
}
