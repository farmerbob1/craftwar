using System.Collections.Generic;
using Craftwar.Import;
using Craftwar.Sim;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// The real audio provider: PCM WAVs straight out of the player's own
    /// installation.
    ///
    /// This replaced what the plan called "the biggest unknown in M8". There was
    /// no decoder to write — a sweep of all 528 maindat.war entries found 5 WAVs
    /// and 51 XMI tracks, while the install ships 456 named uncompressed sound
    /// effects. So this is a lookup table over <see cref="RiffWav"/>.
    ///
    /// Clips are decoded lazily and cached forever. The whole Gamesfx tree is
    /// 24 MB of 22 kHz mono source, and only the subset a match actually touches
    /// is ever loaded, so there is no reason to evict.
    ///
    /// Falls back to <see cref="PlaceholderAudioBank"/> for anything missing, so
    /// a partial install degrades to tones rather than silence.
    /// </summary>
    public sealed class LooseAudioBank : IAudioProvider
    {
        readonly IAssetSource _source;
        readonly PlaceholderAudioBank _fallback = new PlaceholderAudioBank();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<int, List<string>> _variants = new Dictionary<int, List<string>>();

        public LooseAudioBank(IAssetSource source) => _source = source;

        /// <summary>
        /// Global and UI sounds. Mapped to the original's own choices where they
        /// are known from the PSX SFX.HX table, which records each sound's source
        /// path in a trailing comment.
        /// </summary>
        static string PathFor(SoundId id) => id switch
        {
            SoundId.OrderSelect => null,       // per-unit voice; see GetUnitSound
            SoundId.OrderMove => null,
            SoundId.OrderAttack => null,
            SoundId.WorkComplete => null,      // per-race "work complete"
            SoundId.TrainComplete => null,     // per-unit "ready"
            SoundId.ResearchComplete => Wc2SoundCatalog.MiscConstruct,
            SoundId.UnderAttack => null,       // per-race "help"
            SoundId.MineCollapsed => Wc2SoundCatalog.BldgMineCollapse,
            SoundId.Denied => Wc2SoundCatalog.SfxError,
            SoundId.PlacementBlocked => Wc2SoundCatalog.SfxError,
            _ => null,
        };

        public AudioClip Get(SoundId id)
        {
            if (id == SoundId.None)
                return null;
            string path = PathFor(id);
            if (path == null)
                return _fallback.Get(id);
            return Load(path) ?? _fallback.Get(id);
        }

        public int UnitSoundVariants(UnitTypeId type, Race race, UnitSoundKind kind)
            => VariantPaths(type, race, kind).Count;

        public AudioClip GetUnitSound(UnitTypeId type, Race race, UnitSoundKind kind, int variant)
        {
            var paths = VariantPaths(type, race, kind);
            if (paths.Count == 0)
                return null;
            // Wrap rather than clamp: a caller drawing from a stale count still
            // gets a valid line instead of always the last one.
            int index = ((variant % paths.Count) + paths.Count) % paths.Count;
            return Load(paths[index]);
        }

        List<string> VariantPaths(UnitTypeId type, Race race, UnitSoundKind kind)
        {
            // The scan walks the whole logical index, so cache per (type, race,
            // kind) rather than repeating it on every bark.
            int key = ((int)type << 8) | ((int)race << 4) | (int)kind;
            if (_variants.TryGetValue(key, out var cached))
                return cached;

            var paths = Wc2SoundCatalog.Find(_source, type, race, kind);
            _variants[key] = paths;
            return paths;
        }

        AudioClip Load(string logicalPath)
        {
            if (_clips.TryGetValue(logicalPath, out var cached))
                return cached;

            AudioClip clip = null;
            if (_source != null && _source.TryRead(logicalPath, out var bytes))
            {
                try
                {
                    var wav = RiffWav.Decode(bytes);
                    clip = AudioClip.Create(logicalPath, wav.FrameCount, wav.Channels,
                                            wav.SampleRate, stream: false);
                    clip.SetData(wav.Samples, 0);
                }
                catch (WavFormatException e)
                {
                    Debug.LogWarning($"[Craftwar] Bad WAV {logicalPath}: {e.Message}");
                }
            }

            // Cache misses too: a missing file should be looked up once, not
            // once per shot fired.
            _clips[logicalPath] = clip;
            return clip;
        }
    }
}
