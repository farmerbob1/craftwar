using Craftwar.Sim;
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
        ///
        /// Two different animated shapes share this bank, told apart by
        /// <see cref="SimConstants.EffectSparkle"/> and up — the synthetic
        /// spell-effect ids MissileSpriteBaker deliberately keeps above the
        /// real UDTA missile-weapon range (see its Mapping table):
        ///  * A real missile (arrow, rock, mage/death-knight bolt, griffon
        ///    hammer, ...) is directional: several facing-blocks worth of
        ///    frames, cycled as a fixed animation over the flight the same way
        ///    <c>UnitViewPool.PickAnimBlock</c> cycles a unit's gait. Without
        ///    that, a 30-frame bolt bank would only ever sample its first 5
        ///    frames (block 0) and look frozen for the length of the flight.
        ///  * A cosmetic spell effect (heal sparkle, rune flicker, ...) has no
        ///    facing at all in its baked art — its frames are a flat,
        ///    non-directional loop instead. Blizzard's shard art (see
        ///    GameSim.Spells.cs's SpawnAreaBlast/TickProjectiles) is baked the
        ///    same non-directional way even though the projectile itself does
        ///    physically fly in from the northwest each hit — the source
        ///    GRP has no facing blocks, just a few frames of the shard
        ///    twinkling, so it uses this same flat loop despite moving.
        /// </summary>
        public Sprite Get(byte missileType, byte facing, int frameStep, out bool flipX)
        {
            flipX = false;
            var frames = _byType[missileType];
            if (frames == null || frames.Length == 0)
                return null;

            if (missileType >= SimConstants.EffectSparkle)
            {
                int i = ((frameStep % frames.Length) + frames.Length) % frames.Length;
                return frames[i];
            }

            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;

            int cycles = frames.Length / 5;
            if (cycles <= 1)
                return frames[spriteDir < frames.Length ? spriteDir : 0];

            int cycle = ((frameStep % cycles) + cycles) % cycles;
            int index = cycle * 5 + spriteDir;
            return frames[index < frames.Length ? index : spriteDir];
        }
    }
}
