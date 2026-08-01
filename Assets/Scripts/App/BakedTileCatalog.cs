using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Craftwar.App
{
    /// <summary>
    /// Terrain tile catalog for one era, loaded from a pre-baked
    /// <see cref="TerrainTileTable"/> resource rather than decoded from a live
    /// install. Replaces <c>RuntimeTileCatalog</c> — see
    /// <c>Craftwar/Setup/Import Warcraft II Assets</c> for how the table is
    /// produced.
    /// </summary>
    public sealed class BakedTileCatalog : ITileResolver, IMinimapPalette
    {
        static readonly Color32 UnknownTileColor = new Color32(255, 0, 255, 255);

        readonly Dictionary<ushort, Tile> _tiles;
        readonly Dictionary<ushort, Color32> _minimapColors;
        readonly Tile _placeholder;

        public int TileCount => _tiles.Count;

        BakedTileCatalog(TerrainTileTable table)
        {
            _tiles = new Dictionary<ushort, Tile>(table.entries.Length);
            _minimapColors = new Dictionary<ushort, Color32>(table.entries.Length);
            foreach (var e in table.entries)
            {
                if (e.tile != null)
                    _tiles[e.tileId] = e.tile;
                _minimapColors[e.tileId] = e.minimapColor;
            }
            _placeholder = CreatePlaceholder();
        }

        /// <summary>Where the importer writes each era's table, under a Resources folder.</summary>
        public static string ResourcePath(PudEra era) => $"Terrain/{era}";

        /// <summary>Null (with an error) when the era has no baked table — run the importer.</summary>
        public static BakedTileCatalog Load(PudEra era)
        {
            var table = Resources.Load<TerrainTileTable>(ResourcePath(era));
            if (table == null)
            {
                Debug.LogError($"[Craftwar] No baked terrain table for era {era}. " +
                                "Run Craftwar/Setup/Import Warcraft II Assets.");
                return null;
            }
            return new BakedTileCatalog(table);
        }

        static Tile CreatePlaceholder()
        {
            var tex = new Texture2D(SimConstants.TilePixels, SimConstants.TilePixels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
            };
            var magenta = new Color32[SimConstants.TilePixels * SimConstants.TilePixels];
            for (int i = 0; i < magenta.Length; i++)
                magenta[i] = new Color32(255, 0, 255, 255);
            tex.SetPixels32(magenta);
            tex.Apply();

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = Sprite.Create(tex, new Rect(0, 0, SimConstants.TilePixels, SimConstants.TilePixels),
                new Vector2(0.5f, 0.5f), SimConstants.TilePixels, 0, SpriteMeshType.FullRect);
            tile.sprite.name = "placeholder";
            return tile;
        }

        public TileBase Resolve(ushort pudTileId) =>
            _tiles.TryGetValue(pudTileId, out var tile) ? tile : _placeholder;

        public Color32 ColorFor(ushort pudTileId) =>
            _minimapColors.TryGetValue(pudTileId, out var c) ? c : UnknownTileColor;
    }
}
