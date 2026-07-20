using System.Collections.Generic;
using Craftwar.Import;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// The HUD icon atlas, sliced out of the installation's PNG.
    ///
    /// 196 icons at 46x38, four era variants each (forest/ice/swamp/xswamp) plus
    /// a parallel team-colour mask atlas. Point-filtered and rendered at integer
    /// multiples, so the pixel art stays crisp.
    ///
    /// Note the era prefixes here follow the *tileset* naming, not the sprite
    /// naming: "ice" is Winter and "swamp" is the Wasteland era.
    /// </summary>
    public sealed class IconAtlas : IIconProvider
    {
        readonly Texture2D _face;
        readonly Dictionary<string, AtlasFrame> _frames;
        readonly Dictionary<int, Sprite> _cache = new Dictionary<int, Sprite>();
        readonly string _prefix;

        IconAtlas(Texture2D face, Dictionary<string, AtlasFrame> frames, string prefix)
        {
            _face = face;
            _frames = frames;
            _prefix = prefix;
        }

        /// <summary>Null when the install has no icon art; callers fall back to text.</summary>
        public static IconAtlas Load(IAssetSource source, PudEra era)
        {
            if (source == null)
                return null;
            if (!source.TryRead("art/classic/hud/portrait-face.png", out var png)
                || !source.TryRead("art/classic/hud/portrait-face.json", out var json))
                return null;

            Dictionary<string, AtlasFrame> frames;
            try
            {
                frames = TexturePackerAtlas.Parse(System.Text.Encoding.UTF8.GetString(json));
            }
            catch (JsonException e)
            {
                Debug.LogWarning($"[Craftwar] Icon atlas JSON: {e.Message}");
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = "IconAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (!tex.LoadImage(png))
            {
                Object.Destroy(tex);
                return null;
            }

            return new IconAtlas(tex, frames, PrefixFor(era));
        }

        static string PrefixFor(PudEra era) => era switch
        {
            PudEra.Winter => "ice",
            PudEra.Wasteland => "swamp",
            PudEra.Swamp => "xswamp",
            _ => "forest",
        };

        public Sprite Get(int index)
        {
            if (index < 0)
                return null;
            if (_cache.TryGetValue(index, out var cached))
                return cached;

            Sprite sprite = null;
            if (_frames.TryGetValue($"{_prefix}_{index}", out var f)
                // Not every era defines every icon; forest is the complete set.
                || _frames.TryGetValue($"forest_{index}", out f))
            {
                // Texture2D is bottom-up, the atlas JSON top-down.
                var rect = new Rect(f.X, _face.height - f.Y - f.Height, f.Width, f.Height);
                sprite = Sprite.Create(_face, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
                sprite.name = $"icon_{index}";
            }

            _cache[index] = sprite;
            return sprite;
        }
    }
}
