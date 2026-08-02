using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Baked missile art (Craftwar/Setup/Import Warcraft II Assets), one entry
    /// per UDTA missile-weapon id actually mapped to a source GRP — see
    /// <c>MissileSpriteBaker.FileFor</c>. Unmapped ids simply have no entry;
    /// the view falls back to the placeholder dot for those.
    /// </summary>
    public sealed class MissileSpriteTable : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public byte missileType;
            /// <summary>5 baked facings (N, NE, E, SE, S) — the other three
            /// mirror at draw time, same convention as every unit/corpse bank.</summary>
            public Sprite[] frames;
        }

        public Entry[] entries;
    }
}
