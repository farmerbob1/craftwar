using System;
using System.Collections.Generic;
using System.IO;
using Craftwar.App;
using Craftwar.Import;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Bakes every shipped .pud (StreamingAssets dev maps + whatever the
    /// player's own install's Maps folder holds) into a raw-byte TextAsset
    /// plus a pre-rendered thumbnail Sprite, both under Resources so
    /// <c>Craftwar.App.MapList</c> can find and load them with no install and
    /// no live IAssetSource. <c>PudFile.Parse</c>, <c>MatchSetup.FromPud</c>
    /// and everything downstream are untouched — only where the bytes come
    /// from moves.
    ///
    /// Unlike the other bakers this doesn't take an <see cref="IAssetSource"/>:
    /// maps aren't part of the Data folder's asset classes, they come from a
    /// separate Maps directory that only <see cref="LocalAssetPaths.mapsDir"/>
    /// (or the project's own StreamingAssets copies) knows about.
    /// </summary>
    public static class MapBaker
    {
        const string BytesDir = "Assets/GameData/Extracted/Resources/Maps";
        const string ThumbDir = "Assets/GameData/Extracted/Resources/MapThumbnails";

        public static void Bake(LocalAssetPaths paths)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddFrom(GameLoopRunner.StreamingMapsDir, files);
            if (paths != null)
                AddFrom(paths.mapsDir, files);

            if (files.Count == 0)
            {
                Debug.LogWarning("[Craftwar] No .pud maps found to bake.");
                return;
            }

            BakeUtil.EnsureFolder(BytesDir);
            BakeUtil.EnsureFolder(ThumbDir);

            int baked = 0, thumbs = 0;
            foreach (var kv in files)
            {
                string name = kv.Key;
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(kv.Value);
                }
                catch (IOException e)
                {
                    Debug.LogWarning($"[Craftwar] Could not read {kv.Value}: {e.Message}");
                    continue;
                }

                string bytesPath = $"{BytesDir}/{name}.bytes";
                File.WriteAllBytes(bytesPath, bytes);
                AssetDatabase.ImportAsset(bytesPath, ImportAssetOptions.ForceUpdate);
                baked++;

                PudFile pud;
                try
                {
                    pud = PudFile.Parse(bytes);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Craftwar] {name}: could not parse for thumbnail ({e.Message}).");
                    continue;
                }

                var palette = LoadMinimapPalette(pud.Era);
                if (palette == null)
                {
                    Debug.LogWarning($"[Craftwar] {name}: no baked terrain for era {pud.Era} yet " +
                                      "(bake tilesets first). Thumbnail skipped.");
                    continue;
                }

                var thumb = MapThumbnail.Bake(pud, palette, maxDimension: 200);
                if (thumb == null)
                    continue;

                BakeUtil.WriteTextureAsset($"{ThumbDir}/{name}.png", thumb);
                UnityEngine.Object.DestroyImmediate(thumb);
                thumbs++;
            }

            Debug.Log($"[Craftwar] Baked {baked} maps, {thumbs} thumbnails -> {BytesDir}");
        }

        static void AddFrom(string dir, Dictionary<string, string> files)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            string[] found;
            try
            {
                found = Directory.GetFiles(dir, "*.pud", SearchOption.TopDirectoryOnly);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Craftwar] Map scan failed for {dir}: {e.Message}");
                return;
            }

            foreach (var f in found)
            {
                string name = Path.GetFileName(f);
                if (!files.ContainsKey(name)) // StreamingAssets (added first) shadows the install's copy
                    files[name] = f;
            }
        }

        /// <summary>Reads the already-baked TerrainTileTable directly off disk
        /// rather than through BakedTileCatalog.Load/Resources.Load — this
        /// runs at Editor time, in the same pass that may have just written
        /// that asset, and AssetDatabase is the authoritative read here.</summary>
        static IMinimapPalette LoadMinimapPalette(PudEra era)
        {
            string path = $"Assets/GameData/Extracted/Resources/Terrain/{era}.asset";
            var table = AssetDatabase.LoadAssetAtPath<TerrainTileTable>(path);
            if (table == null)
                return null;

            var colors = new Dictionary<ushort, Color32>(table.entries.Length);
            foreach (var e in table.entries)
                colors[e.tileId] = e.minimapColor;
            return new StaticPalette(colors);
        }

        sealed class StaticPalette : IMinimapPalette
        {
            readonly Dictionary<ushort, Color32> _colors;
            public StaticPalette(Dictionary<ushort, Color32> colors) => _colors = colors;
            public Color32 ColorFor(ushort id) =>
                _colors.TryGetValue(id, out var c) ? c : new Color32(255, 0, 255, 255);
        }
    }
}
