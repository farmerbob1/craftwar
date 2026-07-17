using System.IO;
using Craftwar.App;
using Craftwar.Import.War2;
using Craftwar.Sim.Pud;
using Craftwar.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// End-to-end M1 check without entering play mode: real PUD + real
    /// maindat.war → tile catalog → TilemapView → every cell holds a real
    /// (non-placeholder) tile.
    /// </summary>
    public class MapRenderIntegrationTests
    {
        const string MaindatPath =
            @"C:\Users\mattc\Desktop\Warcraft shit\war2tools-master\data\maindat.war";
        const string MapPath =
            @"C:\Program Files (x86)\Warcraft II Remastered\x86\Maps\Gold Rush BNE.pud";

        GameObject _gridGo;

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null)
                Object.DestroyImmediate(_gridGo);
        }

        [Test]
        public void FullPipeline_PudToTilemap_NoPlaceholderTiles()
        {
            if (!File.Exists(MaindatPath) || !File.Exists(MapPath))
                Assert.Ignore("Local WC2 data not present");

            var pud = PudFile.Parse(File.ReadAllBytes(MapPath));
            var archive = new War2Archive(File.ReadAllBytes(MaindatPath));
            var catalog = RuntimeTileCatalog.Build(archive, pud.Era);
            Assert.Greater(catalog.TileCount, 300);

            _gridGo = new GameObject("Grid", typeof(Grid));
            var terrainGo = new GameObject("Terrain",
                typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapView));
            terrainGo.transform.SetParent(_gridGo.transform, false);

            var view = terrainGo.GetComponent<TilemapView>();
            view.LoadMap(pud, catalog);

            var tilemap = terrainGo.GetComponent<Tilemap>();
            int missing = 0;
            var missingIds = new System.Collections.Generic.HashSet<ushort>();
            for (int y = 0; y < pud.Height; y++)
            {
                for (int x = 0; x < pud.Width; x++)
                {
                    var pos = new Vector3Int(x, pud.Height - 1 - y, 0);
                    var tile = tilemap.GetTile(pos);
                    Assert.IsNotNull(tile, $"no tile at {x},{y}");
                    ushort id = pud.Tiles[y * pud.Width + x];
                    if (catalog.Resolve(id) is Tile t && t.sprite != null
                        && t.sprite.name == "placeholder")
                    {
                        missing++;
                        missingIds.Add(id);
                    }
                }
            }
            Assert.AreEqual(0, missing,
                $"{missing} cells fell back to placeholder; ids: {string.Join(", ", missingIds)}");
        }
    }
}
