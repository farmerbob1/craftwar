using System;
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
    /// Bakes every unit/building sprite bank into a master colour atlas plus a
    /// team-colour mask atlas (see <see cref="UnitSpriteTable"/> for why the
    /// two are baked per (file, era) rather than just per file), and one flat
    /// player-0 bank each for the shared foundation/corpse art. Recolouring
    /// per player happens at draw time via the <c>Craftwar/UnitTeamColor</c>
    /// shader — see <see cref="BakedUnitSpriteBank.MaskFor"/> — instead of
    /// baking 8 pre-tinted copies of every frame.
    ///
    /// The largest bake phase: every (typeId, era) pair that resolves to a
    /// file is a candidate, deduplicated by (file, era) since many types
    /// share a bank (Paladin reuses the Knight's art, etc).
    /// </summary>
    public static class SpriteBaker
    {
        const string AtlasDir = "Assets/GameData/Extracted/Sprites";
        const string TableDir = "Assets/GameData/Extracted/Resources/Sprites";

        public static void Bake(IAssetSource source)
        {
            var palettes = new Dictionary<PudEra, Rgba[]>();
            Rgba[] PaletteFor(PudEra era)
            {
                if (palettes.TryGetValue(era, out var cached))
                    return cached;
                string path = $"art/bgs/{War2Palette.FolderForEra(era).ToLowerInvariant()}/{War2Palette.StemForEra(era)}.ppl";
                var p = source.TryRead(path, out var ppl) ? War2Palette.Decode(ppl) : null;
                palettes[era] = p;
                return p;
            }

            var typeEntries = new List<UnitSpriteTable.TypeEntry>();
            var carryEntries = new List<UnitSpriteTable.CarryEntry>();
            var neededFileEras = new HashSet<(string file, PudEra era)>();

            foreach (UnitTypeId type in Enum.GetValues(typeof(UnitTypeId)))
            {
                foreach (PudEra era in Enum.GetValues(typeof(PudEra)))
                {
                    string file = War2Sprites.FileForUnit((ushort)type, era);
                    if (file == null)
                        continue;
                    file = file.ToLowerInvariant();
                    typeEntries.Add(new UnitSpriteTable.TypeEntry { type = type, era = era, file = file });
                    neededFileEras.Add((file, era));

                    foreach (byte carry in new byte[] { 1, 2, 3 })
                    {
                        string carryFile = CarryFileOverride(type, carry);
                        if (carryFile == null)
                            continue;
                        carryFile = carryFile.ToLowerInvariant();
                        carryEntries.Add(new UnitSpriteTable.CarryEntry { type = type, carry = carry, era = era, file = carryFile });
                        neededFileEras.Add((carryFile, era));
                    }
                }
            }

            var fileEntries = new List<UnitSpriteTable.FileEntry>();
            int done = 0;
            Debug.Log($"[Craftwar] Baking {neededFileEras.Count} sprite banks (file x era combinations)...");
            foreach (var (file, era) in neededFileEras)
            {
                var palette = PaletteFor(era);
                if (palette == null)
                {
                    Debug.LogWarning($"[Craftwar] No palette for era {era}. Skipping {file}.");
                    continue;
                }
                if (!source.TryRead("art/unit/" + file, out var data))
                {
                    Debug.LogWarning($"[Craftwar] Sprite not found: {file}");
                    continue;
                }

                SpriteBank bank;
                try
                {
                    bank = War2Sprites.Decode(data);
                }
                catch (War2FormatException e)
                {
                    Debug.LogWarning($"[Craftwar] Sprite decode failed for {file}: {e.Message}");
                    continue;
                }

                fileEntries.Add(BakeBank(bank, file, era, palette));
                done++;
                if (done % 25 == 0)
                    Debug.Log($"[Craftwar] ...{done}/{neededFileEras.Count} sprite banks baked");
            }

            var foundations = new List<UnitSpriteTable.SharedBank>();
            var corpses = new List<UnitSpriteTable.SharedBank>();
            foreach (PudEra era in Enum.GetValues(typeof(PudEra)))
            {
                var palette = PaletteFor(era);
                if (palette == null)
                    continue;

                string foundationFile = era == PudEra.Winter ? "other/s_build1.grp" : "other/build_1.grp";
                var foundationFrames = BakeFlatBank(source, foundationFile, era, palette, "foundation");
                if (foundationFrames != null)
                    foundations.Add(new UnitSpriteTable.SharedBank { era = era, frames = foundationFrames });

                var corpseFrames = BakeFlatBank(source, "other/death.grp", era, palette, "corpse");
                if (corpseFrames != null)
                    corpses.Add(new UnitSpriteTable.SharedBank { era = era, frames = corpseFrames });
            }

            string tablePath = $"{TableDir}/UnitSpriteTable.asset";
            if (AssetDatabase.LoadAssetAtPath<UnitSpriteTable>(tablePath) != null)
                AssetDatabase.DeleteAsset(tablePath);
            var table = BakeUtil.CreateOrLoadAsset<UnitSpriteTable>(tablePath);
            table.files = fileEntries.ToArray();
            table.types = typeEntries.ToArray();
            table.carries = carryEntries.ToArray();
            table.foundations = foundations.ToArray();
            table.corpses = corpses.ToArray();
            EditorUtility.SetDirty(table);

            Debug.Log($"[Craftwar] Baked {fileEntries.Count} sprite banks " +
                      $"({typeEntries.Count} type mappings, {carryEntries.Count} carry overrides) -> {tablePath}");
        }

        /// <summary>Verbatim from the retired UnitSpriteBank.CarryFileOverride —
        /// only the baker needs this switch now; the baked table carries the
        /// resolved (type, carry, era) -> file mapping at runtime.</summary>
        static string CarryFileOverride(UnitTypeId typeId, byte carry)
        {
            if (typeId == UnitTypeId.HumanTanker)
                return carry == 3 ? "Human/tankero.grp" : null;
            if (typeId == UnitTypeId.OrcTanker)
                return carry == 3 ? "Orc/tankero.grp" : null;

            bool human = typeId is UnitTypeId.Peasant or UnitTypeId.AttackPeasant;
            bool orc = typeId is UnitTypeId.Peon or UnitTypeId.AttackPeon;
            if (!human && !orc)
                return null;

            string folder = human ? "Human/" : "Orc/";
            return carry switch
            {
                1 => folder + "peong.grp",
                2 => folder + "peonl.grp",
                _ => null,
            };
        }

        static string SafeName(string s) => s.Replace('/', '_').Replace('.', '_').Replace('#', '_').Replace(' ', '_');

        /// <summary>Bakes one (file, era) bank into a master colour atlas + a
        /// team-colour mask atlas sharing the same layout/UVs.</summary>
        static UnitSpriteTable.FileEntry BakeBank(SpriteBank bank, string file, PudEra era, Rgba[] palette)
        {
            var layout = new GridAtlasLayout(bank.FrameCount, bank.MaxWidth, bank.MaxHeight);
            var colorTex = new Texture2D(layout.AtlasWidth, layout.AtlasHeight, TextureFormat.RGBA32, false);
            var maskTex = new Texture2D(layout.AtlasWidth, layout.AtlasHeight, TextureFormat.RGBA32, false);
            var clear = new Color32[layout.AtlasWidth * layout.AtlasHeight];
            colorTex.SetPixels32(clear);
            maskTex.SetPixels32(clear);

            var colorSlices = new List<BakeUtil.SpriteSlice>(bank.FrameCount);
            var maskSlices = new List<BakeUtil.SpriteSlice>(bank.FrameCount);

            for (int f = 0; f < bank.FrameCount; f++)
            {
                ref var frame = ref bank.Frames[f];
                var cell = layout.CellRect(f);

                var colorPixels = new Color32[frame.Width * frame.Height];
                var maskPixels = new Color32[frame.Width * frame.Height];
                for (int y = 0; y < frame.Height; y++)
                {
                    int srcRow = (frame.Height - 1 - y) * frame.Width;
                    for (int x = 0; x < frame.Width; x++)
                    {
                        byte idx = frame.Indices[srcRow + x];
                        if (idx == War2Sprites.Transparent)
                            continue;

                        Rgba c = palette[idx];
                        colorPixels[y * frame.Width + x] = new Color32(c.R, c.G, c.B, c.A);

                        byte shade = 0;
                        if (idx >= War2Palette.TeamColorFirstIndex &&
                            idx < War2Palette.TeamColorFirstIndex + War2Palette.TeamColorRampSize)
                            shade = (byte)(idx - War2Palette.TeamColorFirstIndex + 1);
                        maskPixels[y * frame.Width + x] = new Color32(shade, 0, 0, 255);
                    }
                }

                int px = cell.x + frame.OffsetX;
                int py = cell.y + (layout.CellHeight - frame.OffsetY - frame.Height);
                if (frame.Width > 0 && frame.Height > 0)
                {
                    colorTex.SetPixels32(px, py, frame.Width, frame.Height, colorPixels);
                    maskTex.SetPixels32(px, py, frame.Width, frame.Height, maskPixels);
                }

                string name = $"f{f}";
                colorSlices.Add(new BakeUtil.SpriteSlice(name, cell, new Vector2(0.5f, 0.5f)));
                maskSlices.Add(new BakeUtil.SpriteSlice(name, cell, new Vector2(0.5f, 0.5f)));
            }
            colorTex.Apply(false, false);
            maskTex.Apply(false, false);

            string baseName = SafeName($"{file}#{era}");
            string colorPath = $"{AtlasDir}/{baseName}_color.png";
            string maskPath = $"{AtlasDir}/{baseName}_mask.png";

            BakeUtil.WriteTextureAsset(colorPath, colorTex);
            UnityEngine.Object.DestroyImmediate(colorTex); // written to disk; the in-memory copy would otherwise
                                                            // sit in native memory for the rest of this bake pass
            var colorSprites = BakeUtil.SliceSpritesheet(colorPath, colorSlices, pixelsPerUnit: SimConstants.TilePixels);

            BakeUtil.WriteTextureAsset(maskPath, maskTex, linear: true);
            UnityEngine.Object.DestroyImmediate(maskTex);
            var maskSprites = BakeUtil.SliceSpritesheet(maskPath, maskSlices, pixelsPerUnit: SimConstants.TilePixels, linear: true);

            var color = new Sprite[bank.FrameCount];
            var mask = new Sprite[bank.FrameCount];
            for (int f = 0; f < bank.FrameCount; f++)
            {
                color[f] = colorSprites[$"f{f}"];
                mask[f] = maskSprites[$"f{f}"];
            }

            return new UnitSpriteTable.FileEntry
            {
                fileKey = $"{file}#{era}",
                color = color,
                mask = mask,
                maskAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath),
            };
        }

        /// <summary>
        /// Foundation/corpse art: the original always decodes these with
        /// player colour 0 (hardcoded), regardless of who owns the building or
        /// died — so no mask/recolour is needed, just a flat baked bank.
        /// </summary>
        static Sprite[] BakeFlatBank(IAssetSource source, string file, PudEra era, Rgba[] palette, string label)
        {
            if (!source.TryRead("art/unit/" + file, out var data))
            {
                Debug.LogWarning($"[Craftwar] {label} art not found: {file}");
                return null;
            }

            SpriteBank bank;
            try
            {
                bank = War2Sprites.Decode(data);
            }
            catch (War2FormatException e)
            {
                Debug.LogWarning($"[Craftwar] {label} decode failed: {e.Message}");
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

            string path = $"{AtlasDir}/shared_{SafeName(file)}_{era}.png";
            BakeUtil.WriteTextureAsset(path, tex);
            UnityEngine.Object.DestroyImmediate(tex);
            var sprites = BakeUtil.SliceSpritesheet(path, slices, pixelsPerUnit: SimConstants.TilePixels);

            var frames = new Sprite[bank.FrameCount];
            for (int f = 0; f < bank.FrameCount; f++)
                frames[f] = sprites[$"f{f}"];
            return frames;
        }
    }
}
