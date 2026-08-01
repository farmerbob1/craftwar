using System;
using Craftwar.Sim;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Every sound effect the Warcraft II importer resolved, baked once instead
    /// of scanned every session: global/UI sounds by <see cref="SoundId"/>, and
    /// per-unit voice lines by (type, race, kind) with however many variants
    /// <c>Wc2SoundCatalog</c> found. Read at runtime by
    /// <see cref="BakedAudioBank"/> — replaces <c>LooseAudioBank</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Craftwar/Baked/Sound Table")]
    public sealed class SoundTable : ScriptableObject
    {
        [Serializable]
        public struct GlobalEntry
        {
            public SoundId id;
            public AudioClip clip;
        }

        [Serializable]
        public struct VariantEntry
        {
            public UnitTypeId type;
            public Race race;
            public UnitSoundKind kind;
            public AudioClip[] clips;
        }

        public GlobalEntry[] globals = Array.Empty<GlobalEntry>();
        public VariantEntry[] variants = Array.Empty<VariantEntry>();
    }
}
