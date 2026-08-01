using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Craftwar.App
{
    /// <summary>
    /// One era's terrain baked ahead of time by the Warcraft II importer
    /// (see <c>Craftwar/Setup/Import Warcraft II Assets</c>): every decodable
    /// MTXM tile id, its <see cref="Tile"/> asset (sliced from the era's atlas),
    /// and its averaged minimap colour. Read at runtime by
    /// <see cref="BakedTileCatalog"/> — replaces the live GRP/tileset decode
    /// <c>RuntimeTileCatalog</c> used to do every session.
    /// </summary>
    [CreateAssetMenu(menuName = "Craftwar/Baked/Terrain Tile Table")]
    public sealed class TerrainTileTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ushort tileId;
            public Tile tile;
            public Color32 minimapColor;
        }

        public Entry[] entries = Array.Empty<Entry>();
    }
}
