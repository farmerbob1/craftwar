using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Shared low-level helpers for every Warcraft II bake phase
    /// (<see cref="Wc2AssetImporter"/> and friends): folder scaffolding, writing
    /// an in-memory atlas out as a real imported Texture2D asset, slicing one
    /// into named Sprite sub-assets, and the create-or-refresh pattern
    /// <c>ProjectBootstrap</c> already uses for authored ScriptableObjects.
    ///
    /// Everything here runs once, at Editor time, against a live install — see
    /// the plan at the root of this feature for why that is fine even though
    /// nothing here may ever ship in a Player build (Craftwar.Import, and this
    /// assembly, are Editor-only).
    /// </summary>
    public static class BakeUtil
    {
        /// <summary>Creates every missing folder in an 'Assets/...' path, parent first.</summary>
        public static void EnsureFolder(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            string parent = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            string leaf = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Writes an in-memory texture to disk as a PNG and imports it with the
        /// point-filtered, uncompressed settings every pixel-art atlas in this
        /// project wants. Returns the freshly (re)imported asset.
        ///
        /// <paramref name="linear"/> disables the sRGB read so a data texture
        /// (e.g. a team-colour mask, where byte values are shade indices, not
        /// colour) round-trips exactly instead of going through the gamma
        /// curve the project's Linear colour space would otherwise apply.
        /// </summary>
        public static Texture2D WriteTextureAsset(string assetPath, Texture2D source, bool linear = false)
        {
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            File.WriteAllBytes(assetPath, source.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = !linear;
            importer.alphaIsTransparency = !linear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        public readonly struct SpriteSlice
        {
            public readonly string Name;
            public readonly RectInt Rect;
            public readonly Vector2 Pivot;

            public SpriteSlice(string name, RectInt rect, Vector2 pivot)
            {
                Name = name;
                Rect = rect;
                Pivot = pivot;
            }
        }

        /// <summary>
        /// Slices an already-imported texture asset into named Sprite
        /// sub-assets. Keying every slice's name stably (frame/tile id) is what
        /// keeps other assets' references to these sprites valid across
        /// re-bakes — Unity derives each sub-asset's identity from its name.
        /// </summary>
        public static Dictionary<string, Sprite> SliceSpritesheet(
            string assetPath, IReadOnlyList<SpriteSlice> slices, float pixelsPerUnit, bool linear = false)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = !linear;
            importer.alphaIsTransparency = !linear;

            var meta = new SpriteMetaData[slices.Count];
            for (int i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                meta[i] = new SpriteMetaData
                {
                    name = s.Name,
                    rect = new Rect(s.Rect.x, s.Rect.y, s.Rect.width, s.Rect.height),
                    pivot = s.Pivot,
                    alignment = (int)SpriteAlignment.Custom,
                };
            }
#pragma warning disable CS0618 // SpriteMetaData/spritesheet: the standard scripted-slicing API.
            importer.spritesheet = meta;
#pragma warning restore CS0618
            importer.SaveAndReimport();

            var result = new Dictionary<string, Sprite>(slices.Count);
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (obj is Sprite sprite)
                    result[sprite.name] = sprite;
            return result;
        }

        /// <summary>Loads an existing ScriptableObject asset or creates a fresh one at that path.</summary>
        public static T CreateOrLoadAsset<T>(string assetPath) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null)
                return existing;

            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }

        /// <summary>
        /// Adds a ScriptableObject as a sub-asset of an already-created container
        /// asset (e.g. one <c>Tile</c> per terrain id, living inside the era's
        /// <c>TerrainTileTable</c> asset) so a whole table stays one file on disk
        /// instead of one file per entry.
        /// </summary>
        public static void AddSubAsset(Object container, Object subAsset, string name)
        {
            subAsset.name = name;
            AssetDatabase.AddObjectToAsset(subAsset, container);
        }
    }
}
