using Craftwar.Sim.Pud;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Craftwar.View
{
    /// <summary>Resolves a PUD 16-bit tile ID to a renderable Unity tile.</summary>
    public interface ITileResolver
    {
        TileBase Resolve(ushort pudTileId);
    }

    /// <summary>
    /// One representative colour per terrain tile id, for the minimap. Kept
    /// separate from ITileResolver so the View never has to reach into the
    /// asset layer — the catalog in Craftwar.App implements both.
    /// </summary>
    public interface IMinimapPalette
    {
        Color32 ColorFor(ushort pudTileId);
    }

    /// <summary>
    /// Renders the terrain layer (MTXM) of a map onto a Unity Tilemap.
    /// Pure projection: bulk-set on load, patched per TileChanged event later
    /// (tree chopped, wall destroyed). Unity Y axis points up, PUD rows go
    /// down, so rows are flipped so the map reads like the original.
    /// </summary>
    [RequireComponent(typeof(Tilemap))]
    public sealed class TilemapView : MonoBehaviour
    {
        Tilemap _tilemap;
        int _height;

        public int MapWidth { get; private set; }
        public int MapHeight { get; private set; }

        void Awake()
        {
            _tilemap = GetComponent<Tilemap>();
        }

        public void LoadMap(PudFile pud, ITileResolver resolver)
        {
            if (_tilemap == null)
                _tilemap = GetComponent<Tilemap>();
            MapWidth = pud.Width;
            MapHeight = pud.Height;
            _height = pud.Height;

            _tilemap.ClearAllTiles();

            var positions = new Vector3Int[pud.Width * pud.Height];
            var tiles = new TileBase[pud.Width * pud.Height];
            int n = 0;
            for (int y = 0; y < pud.Height; y++)
            {
                for (int x = 0; x < pud.Width; x++)
                {
                    positions[n] = new Vector3Int(x, _height - 1 - y, 0);
                    tiles[n] = resolver.Resolve(pud.Tiles[y * pud.Width + x]);
                    n++;
                }
            }
            _tilemap.SetTiles(positions, tiles);
        }

        public void SetTile(int x, int y, ushort pudTileId, ITileResolver resolver)
        {
            _tilemap.SetTile(new Vector3Int(x, _height - 1 - y, 0), resolver.Resolve(pudTileId));
        }
    }
}
