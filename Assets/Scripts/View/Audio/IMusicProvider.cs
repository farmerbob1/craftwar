namespace Craftwar.View
{
    /// <summary>Which piece of music the moment calls for.</summary>
    public enum MusicCue : byte
    {
        None = 0,
        Menu,
        InGame,
        Victory,
        Defeat,
    }

    /// <summary>
    /// Supplies music tracks. Kept separate from <see cref="IAudioProvider"/>
    /// rather than folded into it: music is streamed, looping, long-lived and
    /// asynchronous, where sound effects are short, fire-and-forget clips. One
    /// interface covering both would force every SFX call site to reason about
    /// loading state.
    /// </summary>
    public interface IMusicProvider
    {
        /// <summary>
        /// Ordered track paths for a cue and race, longest-lived first. Empty
        /// when the cue has no music, which callers must treat as "stay silent"
        /// rather than an error.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<string> TracksFor(MusicCue cue, Craftwar.Sim.Race race);

        /// <summary>
        /// Load a track, yielding until it is playable and then invoking
        /// <paramref name="onLoaded"/> (with null on failure).
        ///
        /// A coroutine rather than a blocking call: tracks are tens of megabytes,
        /// so decoding one synchronously would freeze the frame it starts on.
        /// </summary>
        System.Collections.IEnumerator Load(string track, System.Action<UnityEngine.AudioClip> onLoaded);
    }
}
