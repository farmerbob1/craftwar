using System.Collections.Generic;
using NUnit.Framework;
using Craftwar.App;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// MapThumbnail.Bake needs no running GameSim and no real tileset — a fake
    /// IMinimapPalette plus a hand-built PudFile (public fields, no parsing
    /// required) is enough to pin down the row-flip and downsampling math.
    /// </summary>
    public class MapThumbnailTests
    {
        sealed class FakePalette : IMinimapPalette
        {
            readonly Dictionary<ushort, Color32> _colors;
            public FakePalette(Dictionary<ushort, Color32> colors) => _colors = colors;
            public Color32 ColorFor(ushort pudTileId) =>
                _colors.TryGetValue(pudTileId, out var c) ? c : new Color32(0, 0, 0, 255);
        }

        static readonly Color32 Red = new Color32(255, 0, 0, 255);
        static readonly Color32 Green = new Color32(0, 255, 0, 255);
        static readonly Color32 Blue = new Color32(0, 0, 255, 255);
        static readonly Color32 White = new Color32(255, 255, 255, 255);

        [Test]
        public void Bake_NoDownsampling_MatchesMinimapViewsRowFlipConvention()
        {
            // 2x2 map, tile ids 1..4 laid out row-major (y * width + x):
            // (0,0)=1 (1,0)=2
            // (0,1)=3 (1,1)=4
            var pud = new PudFile
            {
                Width = 2,
                Height = 2,
                Tiles = new ushort[] { 1, 2, 3, 4 },
            };
            var palette = new FakePalette(new Dictionary<ushort, Color32>
            {
                [1] = Red, [2] = Green, [3] = Blue, [4] = White,
            });

            var tex = MapThumbnail.Bake(pud, palette, maxDimension: 96);

            Assert.IsNotNull(tex);
            Assert.AreEqual(2, tex.width);
            Assert.AreEqual(2, tex.height);
            // Row-flipped like MinimapView.TexIndex: PUD row y=1 (the bottom
            // row in row-major layout) lands at texture row 0 (Unity's bottom).
            Assert.AreEqual(Blue, (Color32)tex.GetPixel(0, 0));
            Assert.AreEqual(White, (Color32)tex.GetPixel(1, 0));
            Assert.AreEqual(Red, (Color32)tex.GetPixel(0, 1));
            Assert.AreEqual(Green, (Color32)tex.GetPixel(1, 1));
        }

        [Test]
        public void Bake_LargerThanMaxDimension_Downsamples()
        {
            var tiles = new ushort[16];
            for (ushort i = 0; i < 16; i++) tiles[i] = i;
            var pud = new PudFile { Width = 4, Height = 4, Tiles = tiles };
            var palette = new FakePalette(new Dictionary<ushort, Color32>());

            var tex = MapThumbnail.Bake(pud, palette, maxDimension: 2);

            Assert.IsNotNull(tex);
            Assert.AreEqual(2, tex.width);
            Assert.AreEqual(2, tex.height);
        }

        [Test]
        public void Bake_NullPaletteOrEmptyMap_ReturnsNull()
        {
            var pud = new PudFile { Width = 2, Height = 2, Tiles = new ushort[] { 0, 0, 0, 0 } };
            Assert.IsNull(MapThumbnail.Bake(pud, null));
            Assert.IsNull(MapThumbnail.Bake(new PudFile { Width = 0, Height = 0 },
                new FakePalette(new Dictionary<ushort, Color32>())));
        }
    }
}
