using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Bakes a static terrain preview straight from parsed PUD data — no
    /// running GameSim required, unlike MinimapView (which this mirrors: same
    /// one-pixel-per-tile bake, same row-flip convention, same
    /// IMinimapPalette contract BakedTileCatalog already implements). Used
    /// for the host-side map picker and the lobby's map preview.
    /// </summary>
    public static class MapThumbnail
    {
        /// <summary>Bake a downsampled terrain texture. Skip-samples rather
        /// than filtering when the map exceeds maxDimension — plenty for a
        /// thumbnail, and it keeps this a single pass with no scratch buffer.
        /// Returns null if the map has no tile data or no palette is given.</summary>
        public static Texture2D Bake(PudFile pud, IMinimapPalette palette, int maxDimension = 96)
        {
            int width = pud?.Width ?? 0;
            int height = pud?.Height ?? 0;
            if (width <= 0 || height <= 0 || palette == null || pud.Tiles.Length < width * height)
                return null;

            int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(width, height) / (float)maxDimension));
            int outWidth = (width + step - 1) / step;
            int outHeight = (height + step - 1) / step;

            var texture = new Texture2D(outWidth, outHeight, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[outWidth * outHeight];

            for (int oy = 0; oy < outHeight; oy++)
            {
                int simY = Mathf.Min(oy * step, height - 1);
                // Row-flipped to match MinimapView's convention (row 0 = bottom).
                int texY = outHeight - 1 - oy;
                for (int ox = 0; ox < outWidth; ox++)
                {
                    int simX = Mathf.Min(ox * step, width - 1);
                    pixels[texY * outWidth + ox] = palette.ColorFor(pud.Tiles[simY * width + simX]);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);
            return texture;
        }
    }
}
