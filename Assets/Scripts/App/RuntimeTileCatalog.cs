using System.Collections.Generic;
using Craftwar.Import.War2;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Craftwar.App
{
    /// <summary>
    /// Runtime-built terrain tile catalog for one era: decodes the tileset out
    /// of maindat.war, packs all 32x32 tiles into a single atlas texture, and
    /// serves UnityEngine.Tilemaps.Tile instances keyed by raw PUD tile id.
    /// Unknown ids resolve to a magenta placeholder (the fallback chain's
    /// last rung). Later a CustomCatalog can shadow ids with replacement art.
    /// </summary>
    public sealed class RuntimeTileCatalog : ITileResolver, IMinimapPalette
    {
        const int TileSize = War2Tileset.TileSize;
        const int AtlasTilesPerRow = 32; // 32*32 tiles = 1024px square atlas

        readonly Dictionary<ushort, Tile> _tiles = new Dictionary<ushort, Tile>();
        readonly Dictionary<ushort, Color32> _minimapColors = new Dictionary<ushort, Color32>();
        static readonly Color32 UnknownTileColor = new Color32(255, 0, 255, 255);
        Tile _placeholder;

        public Texture2D Atlas { get; private set; }
        public int TileCount => _tiles.Count;

        public static RuntimeTileCatalog Build(War2Archive archive, PudEra era)
        {
            var catalog = new RuntimeTileCatalog();
            var tileset = War2Tileset.Load(archive, era);
            var decoded = tileset.DecodeAll();

            // "Special" megatiles with no MTXM id — chopping art the sim
            // references through synthetic ids (see SimConstants). Megatile
            // numbers are identical in every era.
            foreach (var (id, megatile) in new (ushort, int)[]
            {
                (Craftwar.Sim.SimConstants.OneTreeTopTileId, 121),
                (Craftwar.Sim.SimConstants.OneTreeMidTileId, 122),
                (Craftwar.Sim.SimConstants.OneTreeBotTileId, 123),
                (Craftwar.Sim.SimConstants.ChoppedTileId, 126),
            })
            {
                byte[] px = tileset.DecodeMegatile(megatile);
                if (px != null)
                    decoded.Add(new DecodedTile { TileId = id, Pixels = px });
            }

            int rows = (decoded.Count + AtlasTilesPerRow - 1) / AtlasTilesPerRow;
            var atlas = new Texture2D(
                AtlasTilesPerRow * TileSize, Mathf.Max(1, rows) * TileSize,
                TextureFormat.RGBA32, mipChain: false)
            {
                name = $"Tileset_{era}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            for (int i = 0; i < decoded.Count; i++)
            {
                int cellX = (i % AtlasTilesPerRow) * TileSize;
                // Flip the source rows: decoder emits top-down, SetPixels32
                // expects bottom-up.
                var pixels = new Color32[TileSize * TileSize];
                byte[] src = decoded[i].Pixels;
                for (int y = 0; y < TileSize; y++)
                {
                    int srcRow = (TileSize - 1 - y) * TileSize * 4;
                    for (int x = 0; x < TileSize; x++)
                    {
                        int o = srcRow + x * 4;
                        pixels[y * TileSize + x] = new Color32(src[o], src[o + 1], src[o + 2], src[o + 3]);
                    }
                }
                atlas.SetPixels32(cellX, (i / AtlasTilesPerRow) * TileSize, TileSize, TileSize, pixels);

                // Average the tile down to one colour now, while its pixels are
                // already decoded: that is the minimap's whole terrain layer.
                uint r = 0, g = 0, b = 0;
                for (int p = 0; p < pixels.Length; p++)
                {
                    r += pixels[p].r;
                    g += pixels[p].g;
                    b += pixels[p].b;
                }
                int count = pixels.Length;
                catalog._minimapColors[decoded[i].TileId] =
                    new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
            }
            atlas.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            catalog.Atlas = atlas;

            for (int i = 0; i < decoded.Count; i++)
            {
                var rect = new Rect(
                    (i % AtlasTilesPerRow) * TileSize,
                    (i / AtlasTilesPerRow) * TileSize,
                    TileSize, TileSize);
                var sprite = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: TileSize, extrude: 0, SpriteMeshType.FullRect);
                sprite.name = $"tile_{decoded[i].TileId:x4}";

                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                catalog._tiles[decoded[i].TileId] = tile;
            }

            catalog._placeholder = CreatePlaceholder();
            return catalog;
        }

        static Tile CreatePlaceholder()
        {
            var tex = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
            };
            var magenta = new Color32[TileSize * TileSize];
            for (int i = 0; i < magenta.Length; i++)
                magenta[i] = new Color32(255, 0, 255, 255);
            tex.SetPixels32(magenta);
            tex.Apply();

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = Sprite.Create(tex, new Rect(0, 0, TileSize, TileSize),
                new Vector2(0.5f, 0.5f), TileSize, 0, SpriteMeshType.FullRect);
            tile.sprite.name = "placeholder";
            return tile;
        }

        public TileBase Resolve(ushort pudTileId) =>
            _tiles.TryGetValue(pudTileId, out var tile) ? tile : _placeholder;

        public Color32 ColorFor(ushort pudTileId) =>
            _minimapColors.TryGetValue(pudTileId, out var c) ? c : UnknownTileColor;
    }
}
