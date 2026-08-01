using System;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Every unit/building sprite bank the importer decoded, baked once
    /// instead of decoded from GRP bytes every session. Each bank is baked as
    /// one neutral "master" colour atlas plus a team-colour mask atlas sharing
    /// the same UVs (see <c>Craftwar/UnitTeamColor</c> shader) — recolouring
    /// per player happens at draw time instead of pre-baking 8 tinted copies.
    ///
    /// Keyed by (file, era) rather than just file: the original decodes every
    /// bank with the *match's* era palette regardless of which file it came
    /// from, so the same file can legitimately need up to four different
    /// bakes. Foundation/corpse art has no per-player colour in the original
    /// either (always decoded with player 0), so those are baked flat with no
    /// mask. Read at runtime by <see cref="BakedUnitSpriteBank"/> — replaces
    /// <c>UnitSpriteBank</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Craftwar/Baked/Unit Sprite Table")]
    public sealed class UnitSpriteTable : ScriptableObject
    {
        [Serializable]
        public struct FileEntry
        {
            /// <summary>"{file}#{era}", e.g. "human/grunt.grp#Forest".</summary>
            public string fileKey;
            public Sprite[] color;
            public Sprite[] mask;
            public Texture2D maskAtlas;
        }

        [Serializable]
        public struct TypeEntry
        {
            public UnitTypeId type;
            public PudEra era;
            public string file;
        }

        [Serializable]
        public struct CarryEntry
        {
            public UnitTypeId type;
            public byte carry;
            public PudEra era;
            public string file;
        }

        [Serializable]
        public struct SharedBank
        {
            public PudEra era;
            public Sprite[] frames;
        }

        public FileEntry[] files = Array.Empty<FileEntry>();
        public TypeEntry[] types = Array.Empty<TypeEntry>();
        public CarryEntry[] carries = Array.Empty<CarryEntry>();
        public SharedBank[] foundations = Array.Empty<SharedBank>();
        public SharedBank[] corpses = Array.Empty<SharedBank>();
    }
}
