using System.Collections.Generic;

namespace Craftwar.Import
{
    /// <summary>A frame's pixel rect within the atlas texture. Top-left origin, as authored.</summary>
    public struct AtlasFrame
    {
        public int X, Y, Width, Height;
    }

    /// <summary>
    /// Parser for the TexturePacker "hash" JSON that accompanies the
    /// installation's PNG atlases (Art/classic/HUD/*.json).
    ///
    /// Only the frame rects are read. The rotated/trimmed/spriteSourceSize
    /// fields are present in the files but uniformly false/identity for this
    /// data, so honouring them would be untested code.
    ///
    /// UnityEngine-free so it runs in the standalone harness; turning frames
    /// into Sprites is the caller's job.
    /// </summary>
    public static class TexturePackerAtlas
    {
        /// <summary>
        /// Frame name → rect. Names carry the era prefix, e.g. "forest_12",
        /// "ice_12", and in the mask atlas "forest_12_team".
        /// </summary>
        public static Dictionary<string, AtlasFrame> Parse(string json)
        {
            var result = new Dictionary<string, AtlasFrame>(System.StringComparer.Ordinal);
            var frames = JsonValue.Parse(json)["frames"];
            if (frames == null || frames.Type != JsonValue.Kind.Object)
                throw new JsonException("atlas has no 'frames' object");

            foreach (var kv in frames.Object)
            {
                var rect = kv.Value?["frame"];
                if (rect == null)
                    continue;
                result[kv.Key] = new AtlasFrame
                {
                    X = rect["x"].AsInt(),
                    Y = rect["y"].AsInt(),
                    Width = rect["w"].AsInt(),
                    Height = rect["h"].AsInt(),
                };
            }
            return result;
        }
    }
}
