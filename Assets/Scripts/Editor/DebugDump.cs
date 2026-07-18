using System.IO;
using System.Text;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim.Pud;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>Dev-only: dump archive contents as PNGs for visual identification.</summary>
    public static class DebugDump
    {
        static string OutDir => Path.Combine(Path.GetTempPath(), "craftwar-dump");

        [MenuItem("Craftwar/Debug/Dump Sprite Candidates + Forest Tileset")]
        public static void Run()
        {
            var paths = LocalAssetPaths.Load();
            var archive = new War2Archive(File.ReadAllBytes(paths.maindatWar));
            var palette = War2Palette.Decode(archive.ExtractEntry(War2Palette.EntryForEra(PudEra.Forest)));
            Directory.CreateDirectory(OutDir);

            // Sweep: standing facings of every plausible unit bank.
            for (int entry = 120; entry <= 500; entry++)
            {
                SpriteBank bank;
                try
                {
                    var data = archive.ExtractEntry(entry);
                    if (data == null || data.Length < 16)
                        continue;
                    bank = War2Sprites.Decode(data);
                    if (bank.FrameCount < 15 || bank.MaxWidth < 16 || bank.MaxWidth > 96)
                        continue;
                }
                catch { continue; }
                // Contact sheet: first 5 frames (the 5 standing facings).
                int n = Mathf.Min(5, bank.FrameCount);
                var tex = new Texture2D(bank.MaxWidth * n, bank.MaxHeight, TextureFormat.RGBA32, false);
                var clear = new Color32[tex.width * tex.height];
                tex.SetPixels32(clear);
                for (int f = 0; f < n; f++)
                {
                    ref var frame = ref bank.Frames[f];
                    byte[] rgba = War2Sprites.ToRgba(frame, palette, 0);
                    for (int y = 0; y < frame.Height; y++)
                        for (int x = 0; x < frame.Width; x++)
                        {
                            int s = ((frame.Height - 1 - y) * frame.Width + x) * 4;
                            tex.SetPixel(f * bank.MaxWidth + frame.OffsetX + x,
                                y + (bank.MaxHeight - frame.OffsetY - frame.Height),
                                new Color32(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]));
                        }
                }
                tex.Apply();
                File.WriteAllBytes(Path.Combine(OutDir, $"entry_{entry}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }

            // Forest tileset contact sheet, 16 tiles per row, with a manifest of ids.
            var tiles = War2Tileset.Load(archive, PudEra.Forest).DecodeAll();
            int cols = 16;
            int rows = (tiles.Count + cols - 1) / cols;
            var atlas = new Texture2D(cols * 32, rows * 32, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
            var manifest = new StringBuilder();
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                int cx = (i % cols) * 32;
                int cy = (rows - 1 - i / cols) * 32; // row 0 at top of image
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        int s = (y * 32 + x) * 4;
                        atlas.SetPixel(cx + x, cy + 31 - y,
                            new Color32(t.Pixels[s], t.Pixels[s + 1], t.Pixels[s + 2], t.Pixels[s + 3]));
                    }
                manifest.AppendLine($"row {i / cols} col {i % cols}: 0x{t.TileId:x4}");
            }
            atlas.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, "forest_tiles.png"), atlas.EncodeToPNG());
            File.WriteAllText(Path.Combine(OutDir, "forest_tiles.txt"), manifest.ToString());
            Object.DestroyImmediate(atlas);

            Debug.Log($"[Craftwar] Dumped to {OutDir}");
        }
    }
}
