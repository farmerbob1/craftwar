using System;
using System.Collections.Generic;
using Craftwar.App;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Bakes one <see cref="TerrainTileTable"/> per era: decodes the tileset
    /// exactly as <c>RuntimeTileCatalog.Build</c> used to at runtime, packs it
    /// into an atlas PNG once, and writes a Resources asset the game reads at
    /// startup instead of decoding anything. First bake phase because terrain
    /// has no team-colour complexity — proves the shared bake plumbing before
    /// the harder asset classes.
    /// </summary>
    public static class TilesetBaker
    {
        const string AtlasDir = "Assets/GameData/Extracted/Tilesets";
        const string TableDir = "Assets/GameData/Extracted/Resources/Terrain";

        public static void Bake(IAssetSource source)
        {
            foreach (PudEra era in Enum.GetValues(typeof(PudEra)))
                BakeEra(source, era);
        }

        static void BakeEra(IAssetSource source, PudEra era)
        {
            var tileset = War2Tileset.Load(source, era);
            if (tileset == null)
            {
                Debug.LogWarning($"[Craftwar] Tileset not found for era {era} " +
                                 $"(looked under art/bgs/{War2Palette.FolderForEra(era)}). Skipped.");
                return;
            }

            var decoded = tileset.DecodeAll();
            foreach (var (id, megatile) in new (ushort, int)[]
            {
                (SimConstants.OneTreeTopTileId, 121),
                (SimConstants.OneTreeMidTileId, 122),
                (SimConstants.OneTreeBotTileId, 123),
                (SimConstants.ChoppedTileId, 126),
            })
            {
                byte[] px = tileset.DecodeMegatile(megatile);
                if (px != null)
                    decoded.Add(new DecodedTile { TileId = id, Pixels = px });
            }

            if (decoded.Count == 0)
            {
                Debug.LogWarning($"[Craftwar] Era {era} decoded zero tiles. Skipped.");
                return;
            }

            const int size = War2Tileset.TileSize;
            var layout = new GridAtlasLayout(decoded.Count, size, size);
            var atlas = new Texture2D(layout.AtlasWidth, layout.AtlasHeight, TextureFormat.RGBA32, false);
            var clear = new Color32[layout.AtlasWidth * layout.AtlasHeight];
            atlas.SetPixels32(clear);

            var minimapColors = new Dictionary<ushort, Color32>(decoded.Count);
            var slices = new List<BakeUtil.SpriteSlice>(decoded.Count);

            for (int i = 0; i < decoded.Count; i++)
            {
                var cell = layout.CellRect(i);
                byte[] src = decoded[i].Pixels;

                // Source rows are top-down; SetPixels32 wants bottom-up.
                var pixels = new Color32[size * size];
                uint r = 0, g = 0, b = 0;
                for (int y = 0; y < size; y++)
                {
                    int srcRow = (size - 1 - y) * size * 4;
                    for (int x = 0; x < size; x++)
                    {
                        int o = srcRow + x * 4;
                        var c = new Color32(src[o], src[o + 1], src[o + 2], src[o + 3]);
                        pixels[y * size + x] = c;
                        r += c.r; g += c.g; b += c.b;
                    }
                }
                atlas.SetPixels32(cell.x, cell.y, size, size, pixels);

                ushort tileId = decoded[i].TileId;
                int count = pixels.Length;
                minimapColors[tileId] = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
                slices.Add(new BakeUtil.SpriteSlice($"tile_{tileId:x4}", cell, new Vector2(0.5f, 0.5f)));
            }
            atlas.Apply(false, false);

            string pngPath = $"{AtlasDir}/{era}.png";
            BakeUtil.WriteTextureAsset(pngPath, atlas);
            UnityEngine.Object.DestroyImmediate(atlas);
            var sprites = BakeUtil.SliceSpritesheet(pngPath, slices, pixelsPerUnit: size);

            string tablePath = $"{TableDir}/{era}.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainTileTable>(tablePath) != null)
                AssetDatabase.DeleteAsset(tablePath);
            var table = BakeUtil.CreateOrLoadAsset<TerrainTileTable>(tablePath);

            var entries = new TerrainTileTable.Entry[decoded.Count];
            for (int i = 0; i < decoded.Count; i++)
            {
                ushort tileId = decoded[i].TileId;
                string name = $"tile_{tileId:x4}";
                if (!sprites.TryGetValue(name, out var sprite))
                {
                    Debug.LogWarning($"[Craftwar] Missing sliced sprite for {name} ({era}).");
                    continue;
                }

                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                BakeUtil.AddSubAsset(table, tile, name);

                entries[i] = new TerrainTileTable.Entry
                {
                    tileId = tileId,
                    tile = tile,
                    minimapColor = minimapColors[tileId],
                };
            }
            table.entries = entries;
            EditorUtility.SetDirty(table);

            Debug.Log($"[Craftwar] Baked {decoded.Count} tiles for era {era} -> {tablePath}");
        }
    }
}
