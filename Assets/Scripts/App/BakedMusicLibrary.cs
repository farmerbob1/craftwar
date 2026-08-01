using System;
using System.Collections;
using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Reads a pre-baked <see cref="MusicTable"/> instead of resolving an Ogg
    /// cache or streaming WAVs from a live install every session. Replaces
    /// <c>MusicLibrary</c> — see <c>Craftwar/Setup/Import Warcraft II Assets</c>.
    /// Every clip is already a real imported AudioClip asset, so "loading" a
    /// track is just a dictionary lookup; the coroutine shape is kept only
    /// because <see cref="IMusicProvider"/> requires it.
    /// </summary>
    public sealed class BakedMusicLibrary : IMusicProvider
    {
        // Track stems, without the _r suffix that marks the redbook recordings.
        static readonly string[] HumanInGame =
            { "HUMAN1", "HUMAN2", "HUMAN3", "HUMAN4", "HUMAN5", "HUMAN6" };
        static readonly string[] OrcInGame =
            { "ORC1", "ORC2", "ORC3", "ORC4", "ORC5", "ORC6" };

        readonly Dictionary<string, AudioClip> _clips;

        public static string ResourcePath => "Music/MusicTable";

        public static BakedMusicLibrary Load()
        {
            var table = Resources.Load<MusicTable>(ResourcePath);
            return table == null ? null : new BakedMusicLibrary(table);
        }

        BakedMusicLibrary(MusicTable table)
        {
            _clips = new Dictionary<string, AudioClip>(table.entries.Length);
            foreach (var e in table.entries)
                if (e.clip != null)
                    _clips[e.stem] = e.clip;
        }

        public IReadOnlyList<string> TracksFor(MusicCue cue, Race race)
        {
            bool orc = race == Race.Orc;
            switch (cue)
            {
                case MusicCue.Menu:
                    return Resolve(orc ? "OWARROOM" : "HWARROOM");
                case MusicCue.Victory:
                    return Resolve(orc ? "OVICTORY" : "HVICTORY");
                case MusicCue.Defeat:
                    return Resolve(orc ? "ODEFEAT" : "HDEFEAT");
                case MusicCue.InGame:
                    return Resolve(orc ? OrcInGame : HumanInGame);
                default:
                    return Array.Empty<string>();
            }
        }

        List<string> Resolve(params string[] stems)
        {
            var found = new List<string>();
            foreach (string stem in stems)
            {
                string key = stem + "_r";
                if (_clips.ContainsKey(key))
                    found.Add(key);
            }
            return found;
        }

        public IEnumerator Load(string track, Action<AudioClip> onLoaded)
        {
            _clips.TryGetValue(track ?? string.Empty, out var clip);
            onLoaded?.Invoke(clip);
            yield break;
        }
    }
}
