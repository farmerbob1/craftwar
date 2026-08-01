using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Reads a pre-baked <see cref="SoundTable"/> instead of scanning
    /// Gamesfx/Sfx every session. Replaces <c>LooseAudioBank</c> — see
    /// <c>Craftwar/Setup/Import Warcraft II Assets</c> for how the table is
    /// produced. Falls back to <see cref="PlaceholderAudioBank"/> for anything
    /// not baked (mirrors the old install-partial fallback).
    /// </summary>
    public sealed class BakedAudioBank : IAudioProvider
    {
        readonly Dictionary<SoundId, AudioClip> _globals;
        readonly Dictionary<int, AudioClip[]> _variants;
        readonly PlaceholderAudioBank _fallback = new PlaceholderAudioBank();

        public static string ResourcePath => "Audio/SoundTable";

        public static BakedAudioBank Load()
        {
            var table = Resources.Load<SoundTable>(ResourcePath);
            if (table == null)
            {
                Debug.LogError("[Craftwar] No baked sound table. Run Craftwar/Setup/Import Warcraft II Assets.");
                return null;
            }
            return new BakedAudioBank(table);
        }

        BakedAudioBank(SoundTable table)
        {
            _globals = new Dictionary<SoundId, AudioClip>(table.globals.Length);
            foreach (var e in table.globals)
                if (e.clip != null)
                    _globals[e.id] = e.clip;

            _variants = new Dictionary<int, AudioClip[]>(table.variants.Length);
            foreach (var e in table.variants)
                _variants[Key(e.type, e.race, e.kind)] = e.clips;
        }

        static int Key(UnitTypeId type, Race race, UnitSoundKind kind) =>
            ((int)type << 8) | ((int)race << 4) | (int)kind;

        public AudioClip Get(SoundId id)
        {
            if (id == SoundId.None)
                return null;
            return _globals.TryGetValue(id, out var clip) ? clip : _fallback.Get(id);
        }

        public int UnitSoundVariants(UnitTypeId type, Race race, UnitSoundKind kind) =>
            _variants.TryGetValue(Key(type, race, kind), out var arr) ? arr.Length : 0;

        public AudioClip GetUnitSound(UnitTypeId type, Race race, UnitSoundKind kind, int variant)
        {
            if (!_variants.TryGetValue(Key(type, race, kind), out var arr) || arr.Length == 0)
                return null;
            int index = ((variant % arr.Length) + arr.Length) % arr.Length;
            return arr[index];
        }
    }
}
