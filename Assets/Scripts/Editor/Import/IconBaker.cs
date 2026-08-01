using System.Collections.Generic;
using System.IO;
using Craftwar.App;
using Craftwar.Import;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Bakes the HUD icon atlas (Portrait-face.png + its TexturePacker JSON)
    /// into sliced Sprite assets plus one <see cref="IconTable"/>. The face PNG
    /// is already a valid texture, so this is a copy-and-slice step, not a
    /// decode — <c>TexturePackerAtlas.Parse</c> (already UnityEngine-free, so
    /// it runs fine at Editor time) supplies the frame rects.
    ///
    /// HUD chrome (BG_Human/Orc.png) and the portrait team-colour mask are not
    /// baked here: nothing in the current runtime reads either, so there is no
    /// consumer to wire up yet.
    /// </summary>
    public static class IconBaker
    {
        const string FacePngPath = "art/classic/hud/portrait-face.png";
        const string FaceJsonPath = "art/classic/hud/portrait-face.json";
        const string AtlasPath = "Assets/GameData/Extracted/Icons/PortraitFace.png";
        const string TablePath = "Assets/GameData/Extracted/Resources/Icons/IconTable.asset";

        public static void Bake(IAssetSource source)
        {
            if (!source.TryRead(FacePngPath, out var png) || !source.TryRead(FaceJsonPath, out var json))
            {
                Debug.LogWarning("[Craftwar] HUD icon atlas not found. Skipped.");
                return;
            }

            Dictionary<string, AtlasFrame> frames;
            try
            {
                frames = TexturePackerAtlas.Parse(System.Text.Encoding.UTF8.GetString(json));
            }
            catch (JsonException e)
            {
                Debug.LogWarning($"[Craftwar] Icon atlas JSON: {e.Message}. Skipped.");
                return;
            }

            BakeUtil.EnsureFolder("Assets/GameData/Extracted/Icons");
            File.WriteAllBytes(AtlasPath, png);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            // Need the imported height before we can flip TexturePacker's
            // top-left-origin rects into Unity's bottom-up sprite rects.
            var probe = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            int texHeight = probe.height;

            var slices = new List<BakeUtil.SpriteSlice>(frames.Count);
            foreach (var kv in frames)
            {
                var f = kv.Value;
                var rect = new RectInt(f.X, texHeight - f.Y - f.Height, f.Width, f.Height);
                slices.Add(new BakeUtil.SpriteSlice(kv.Key, rect, new Vector2(0.5f, 0.5f)));
            }

            var sprites = BakeUtil.SliceSpritesheet(AtlasPath, slices, pixelsPerUnit: 1f);

            if (AssetDatabase.LoadAssetAtPath<IconTable>(TablePath) != null)
                AssetDatabase.DeleteAsset(TablePath);
            var table = BakeUtil.CreateOrLoadAsset<IconTable>(TablePath);
            var entries = new IconTable.Entry[sprites.Count];
            int i = 0;
            foreach (var kv in sprites)
                entries[i++] = new IconTable.Entry { name = kv.Key, sprite = kv.Value };
            table.entries = entries;
            EditorUtility.SetDirty(table);

            Debug.Log($"[Craftwar] Baked {entries.Length} icons -> {TablePath}");
        }
    }
}
