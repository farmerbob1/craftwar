using System.Collections.Generic;
using Craftwar.App;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Bakes in-flight projectile art: UDTA missile-weapon id -&gt; a flat
    /// (unrecoloured, player-0-style) sprite bank decoded from the shared
    /// "other" effects GRPs, the same files BULLET.C's per-bullet-type art
    /// table points at. Player colour never applies to a flying missile, so
    /// there is no team-colour mask here, unlike <see cref="SpriteBaker"/>.
    ///
    /// The id -&gt; file mapping was cross-referenced against war2tools'
    /// pud_format.txt missile-weapon list and the actual GRP filenames
    /// shipped under Data/Art/unit/Other; a handful (marked below) are a
    /// best-effort guess where no exact filename match exists. Missile art
    /// doesn't vary by era, so a single palette (Forest) bakes every entry.
    /// Unmapped ids are skipped — the view keeps its placeholder dot for
    /// those rather than showing something wrong.
    /// </summary>
    public static class MissileSpriteBaker
    {
        const string AtlasDir = "Assets/GameData/Extracted/Sprites";
        const string TableDir = "Assets/GameData/Extracted/Resources/Sprites";

        /// <summary>UDTA missile-weapon id (Appendix, pud_format.txt) -&gt; GRP
        /// file under Data/Art/unit/Other. Only ids this project's unit data
        /// actually uses are listed; add more here as new units need them.</summary>
        static readonly (byte id, string file)[] Mapping =
        {
            (0x00, "lightng.grp"),  // lightning (hero paladins)
            (0x01, "hammer.grp"),   // griffon hammer
            (0x02, "fireball.grp"), // dragon breath / gryphon fireball
            (0x07, "cannon.grp"),   // big cannon (battleship/juggernaught)
            (0x0a, "dkatak.grp"),   // touch of death (hero death knights)
            (0x0d, "rock.grp"),     // catapult rock
            (0x0e, "bolt.grp"),     // ballista bolt
            (0x0f, "arrow.grp"),    // arrow
            (0x10, "axe.grp"),      // throwing axe
            (0x11, "torpedo.grp"),  // submarine missile
            (0x12, "torpedo.grp"),  // turtle missile (shared art)
            (0x18, "cannon.grp"),   // small cannon (guard/cannon tower) — best guess, shares big cannon's art
            (0x1b, "hvfire.grp"),   // demon fire (Eye of Kilrogg) — best guess, no exact filename match

            // Synthetic spell cast/impact effect ids (SimConstants.Effect*),
            // above the real UDTA missile-weapon range — GameSim.Spells.cs's
            // SpawnEffect/SpawnAreaBlast, matching BULLET.C's
            // bullet_create_on(pTarget, BT_SPARKLE/BT_HEAL/...) family.
            (SimConstants.EffectSparkle, "sparkle.grp"),
            (SimConstants.EffectHeal, "heal.grp"),
            (SimConstants.EffectExorcism, "exorcism.grp"),
            (SimConstants.EffectRune, "rune.grp"),
            (SimConstants.EffectBlizzard, "blizzard.grp"),
            (SimConstants.EffectWhirlwind, "phoon.grp"),
            (SimConstants.EffectDecay, "rot.grp"),
            (SimConstants.EffectBoom, "boom.grp"),
        };

        public static void Bake(IAssetSource source)
        {
            string palettePath = $"art/bgs/{War2Palette.FolderForEra(PudEra.Forest).ToLowerInvariant()}" +
                $"/{War2Palette.StemForEra(PudEra.Forest)}.ppl";
            if (!source.TryRead(palettePath, out var pplData))
            {
                Debug.LogWarning("[Craftwar] MissileSpriteBaker: no Forest palette, skipping missile art.");
                return;
            }
            var palette = War2Palette.Decode(pplData);

            var entries = new List<MissileSpriteTable.Entry>();
            var byFile = new Dictionary<string, Sprite[]>();

            foreach (var (id, file) in Mapping)
            {
                if (!byFile.TryGetValue(file, out var frames))
                {
                    frames = BakeFlatBank(source, file, palette);
                    byFile[file] = frames; // cache even a null miss — don't retry
                }
                if (frames != null)
                    entries.Add(new MissileSpriteTable.Entry { missileType = id, frames = frames });
            }

            string tablePath = $"{TableDir}/MissileSpriteTable.asset";
            if (AssetDatabase.LoadAssetAtPath<MissileSpriteTable>(tablePath) != null)
                AssetDatabase.DeleteAsset(tablePath);
            var table = BakeUtil.CreateOrLoadAsset<MissileSpriteTable>(tablePath);
            table.entries = entries.ToArray();
            EditorUtility.SetDirty(table);

            Debug.Log($"[Craftwar] Baked {entries.Count}/{Mapping.Length} missile sprites -> {tablePath}");
        }

        /// <summary>Same shape as SpriteBaker.BakeFlatBank: no team-colour
        /// mask, always decoded at player colour 0 (irrelevant for effects).</summary>
        static Sprite[] BakeFlatBank(IAssetSource source, string file, Rgba[] palette)
        {
            if (!source.TryRead("art/unit/other/" + file, out var data))
            {
                Debug.LogWarning($"[Craftwar] Missile art not found: other/{file}");
                return null;
            }

            SpriteBank bank;
            try
            {
                bank = War2Sprites.Decode(data);
            }
            catch (War2FormatException e)
            {
                Debug.LogWarning($"[Craftwar] Missile art decode failed for {file}: {e.Message}");
                return null;
            }

            var layout = new GridAtlasLayout(bank.FrameCount, bank.MaxWidth, bank.MaxHeight);
            var tex = new Texture2D(layout.AtlasWidth, layout.AtlasHeight, TextureFormat.RGBA32, false);
            tex.SetPixels32(new Color32[layout.AtlasWidth * layout.AtlasHeight]);

            var slices = new List<BakeUtil.SpriteSlice>(bank.FrameCount);
            for (int f = 0; f < bank.FrameCount; f++)
            {
                ref var frame = ref bank.Frames[f];
                byte[] rgba = War2Sprites.ToRgba(frame, palette, playerColor: 0);
                var cell = layout.CellRect(f);

                var pixels = new Color32[frame.Width * frame.Height];
                for (int y = 0; y < frame.Height; y++)
                {
                    int srcRow = (frame.Height - 1 - y) * frame.Width;
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int s = (srcRow + x) * 4;
                        pixels[y * frame.Width + x] = new Color32(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
                    }
                }

                int px = cell.x + frame.OffsetX;
                int py = cell.y + (layout.CellHeight - frame.OffsetY - frame.Height);
                if (frame.Width > 0 && frame.Height > 0)
                    tex.SetPixels32(px, py, frame.Width, frame.Height, pixels);

                slices.Add(new BakeUtil.SpriteSlice($"f{f}", cell, new Vector2(0.5f, 0.5f)));
            }
            tex.Apply(false, false);

            string path = $"{AtlasDir}/missile_{file.Replace('.', '_')}.png";
            BakeUtil.WriteTextureAsset(path, tex);
            UnityEngine.Object.DestroyImmediate(tex);
            var sprites = BakeUtil.SliceSpritesheet(path, slices, pixelsPerUnit: SimConstants.TilePixels);

            var result = new Sprite[bank.FrameCount];
            for (int f = 0; f < bank.FrameCount; f++)
                result[f] = sprites[$"f{f}"];
            return result;
        }
    }
}
