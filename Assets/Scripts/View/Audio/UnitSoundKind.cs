namespace Craftwar.View
{
    /// <summary>
    /// What a unit is saying. Distinct from <see cref="SoundId"/>, which covers
    /// global and UI audio: this axis is per-unit and per-race, so the two do
    /// not belong in one enum — the cross-product of 110 unit types, 8 kinds and
    /// up to 7 takes is data, not enum cases.
    ///
    /// Lives in View rather than Import because Craftwar.View does not reference
    /// Craftwar.Import (and should not — that would invert the layering). Import
    /// references View's Sim-facing types instead.
    /// </summary>
    public enum UnitSoundKind : byte
    {
        /// <summary>"What?" — selection.</summary>
        Selected = 0,
        /// <summary>"Yes, sir" — order acknowledgement.</summary>
        Acknowledge,
        /// <summary>Clicked too many times. PISSED_COUNT in the original is 3.</summary>
        Annoyed,
        /// <summary>Training finished.</summary>
        Ready,
        /// <summary>Worker finished a job.</summary>
        WorkComplete,
        /// <summary>Under attack.</summary>
        Help,
        Death,
        Attack,
    }
}
