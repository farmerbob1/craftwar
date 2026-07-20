using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using Craftwar.Sim;
using Craftwar.View;
using UnityEngine;
using UnityEngine.Networking;

namespace Craftwar.App
{
    /// <summary>
    /// Finds and streams the game's music.
    ///
    /// Two sources, preferring the smaller one. If Ogg files have been produced
    /// by Tools/convert_music.py they are used (77 MB, and Unity decodes Vorbis
    /// natively); otherwise the source WAVs are streamed straight out of the
    /// player's installation (580 MB, no conversion step). Both are streamed
    /// rather than loaded, so a 4-minute track costs no memory spike and starts
    /// playing before it has finished decoding.
    ///
    /// That fallback is what makes music work on a machine that has never run
    /// the converter — which is every machine but the one it was run on.
    /// </summary>
    public sealed class MusicLibrary : IMusicProvider
    {
        /// <summary>Ogg cache, relative to the project. Gitignored; see .gitignore.</summary>
        public const string OggDir = "Assets/GameData/Extracted/Music";

        readonly string _oggRoot;      // null when no converted cache exists
        readonly string _wavRoot;      // null when the install is absent
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        MusicLibrary(string oggRoot, string wavRoot)
        {
            _oggRoot = oggRoot;
            _wavRoot = wavRoot;
        }

        public static MusicLibrary Create(LocalAssetPaths paths, string dataRoot)
        {
            string ogg = Path.GetFullPath(OggDir);
            if (!Directory.Exists(ogg))
                ogg = null;

            string wav = null;
            if (!string.IsNullOrEmpty(dataRoot))
            {
                string candidate = Path.Combine(dataRoot, "Music");
                if (Directory.Exists(candidate))
                    wav = candidate;
            }

            if (ogg == null && wav == null)
                return null;
            return new MusicLibrary(ogg, wav);
        }

        // Track stems, without the _r suffix that marks the redbook recordings.
        static readonly string[] HumanInGame =
            { "HUMAN1", "HUMAN2", "HUMAN3", "HUMAN4", "HUMAN5", "HUMAN6" };
        static readonly string[] OrcInGame =
            { "ORC1", "ORC2", "ORC3", "ORC4", "ORC5", "ORC6" };

        // DISCOWC is deliberately absent: it is the joke track, not part of the
        // in-game rotation.

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
                    return System.Array.Empty<string>();
            }
        }

        List<string> Resolve(params string[] stems)
        {
            var found = new List<string>();
            foreach (string stem in stems)
            {
                string path = PathFor(stem);
                if (path != null)
                    found.Add(path);
            }
            return found;
        }

        /// <summary>
        /// Locate one track. Prefers Ogg, then the source WAV. The "_r" redbook
        /// recordings are the real music; "_opl" is the OPL-synth alternative
        /// and covers only 14 of the 19 tracks, so it is not a usable fallback
        /// and is only reachable by explicit request.
        /// </summary>
        string PathFor(string stem)
        {
            if (_oggRoot != null)
            {
                string ogg = Path.Combine(_oggRoot, stem + "_r.ogg");
                if (File.Exists(ogg))
                    return ogg;
            }
            if (_wavRoot != null)
            {
                string wav = Path.Combine(_wavRoot, stem + "_r.wav");
                if (File.Exists(wav))
                    return wav;
            }
            return null;
        }

        public System.Collections.IEnumerator Load(string track, System.Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrEmpty(track))
            {
                onLoaded?.Invoke(null);
                yield break;
            }
            if (_clips.TryGetValue(track, out var cached))
            {
                onLoaded?.Invoke(cached);
                yield break;
            }

            var type = track.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase)
                ? AudioType.OGGVORBIS
                : AudioType.WAV;

            AudioClip clip = null;
            using (var request = UnityWebRequestMultimedia.GetAudioClip("file://" + track, type))
            {
                // streamAudio: decode as it plays rather than up front. Without
                // it a 30 MB track would materialise entirely in memory first.
                ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = true;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    clip = DownloadHandlerAudioClip.GetContent(request);
                else
                    Debug.LogWarning($"[Craftwar] Music load failed: {track} ({request.error})");
            }

            if (clip != null)
                clip.name = Path.GetFileNameWithoutExtension(track);
            _clips[track] = clip;
            onLoaded?.Invoke(clip);
        }
    }
}
