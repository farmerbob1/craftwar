using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Reads the pre-baked <see cref="MissileSpriteTable"/> for in-flight
    /// projectile art (Craftwar/Setup/Import Warcraft II Assets). Missile art
    /// doesn't vary by era/tileset, so — unlike <see cref="BakedUnitSpriteBank"/>
    /// — there's only ever one bank, and <see cref="Load"/> can return null
    /// (nothing baked yet) without it being an error: the view falls back to
    /// its placeholder dot.
    /// </summary>
    public sealed class BakedMissileSpriteBank : IMissileSpriteProvider
    {
        readonly Sprite[][] _byType = new Sprite[256][];

        public static string ResourcePath => "Sprites/MissileSpriteTable";

        public static BakedMissileSpriteBank Load()
        {
            var table = Resources.Load<MissileSpriteTable>(ResourcePath);
            return table == null ? null : new BakedMissileSpriteBank(table);
        }

        BakedMissileSpriteBank(MissileSpriteTable table)
        {
            foreach (var e in table.entries)
                _byType[e.missileType] = e.frames;
        }

        /// <summary>
        /// 5 baked facings (N, NE, E, SE, S) mirrored for the other three, same
        /// convention as <see cref="BakedUnitSpriteBank.GetCorpseFrame"/>. Null
        /// when this missile type has no baked art.
        /// </summary>
        public Sprite Get(byte missileType, byte facing, out bool flipX)
        {
            flipX = false;
            var frames = _byType[missileType];
            if (frames == null || frames.Length == 0)
                return null;
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            return frames[spriteDir < frames.Length ? spriteDir : 0];
        }
    }
}
