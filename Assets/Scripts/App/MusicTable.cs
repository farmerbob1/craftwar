using System;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Every music track the importer resolved (preferring an already-converted
    /// Ogg over the source WAV — see <c>Tools/convert_music.py</c>), keyed by
    /// its original stem with the "_r" (redbook) suffix, e.g. "HUMAN1_r".
    /// Read at runtime by <see cref="BakedMusicLibrary"/> — replaces
    /// <c>MusicLibrary</c>'s dual-path Ogg-cache-or-stream-WAV logic.
    /// </summary>
    [CreateAssetMenu(menuName = "Craftwar/Baked/Music Table")]
    public sealed class MusicTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string stem;
            public AudioClip clip;
        }

        public Entry[] entries = Array.Empty<Entry>();
    }
}
