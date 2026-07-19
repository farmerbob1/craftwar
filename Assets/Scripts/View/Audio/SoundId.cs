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
    }
}
