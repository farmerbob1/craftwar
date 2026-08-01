using Craftwar.Import;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// One-time (rerunnable) bake of every Warcraft II asset class into real
    /// Unity assets under Assets/GameData/Extracted, so Play mode and Player
    /// builds never need a live install again. Locates the install exactly like
    /// DataCodegen does (LocalAssetPaths.dataRoot), then runs each asset
    /// class's baker in turn. Batch: -executeMethod Craftwar.EditorTools.Wc2AssetImporter.Run
    /// </summary>
    public static class Wc2AssetImporter
    {
        [MenuItem("Craftwar/Setup/Import Warcraft II Assets")]
        public static void Run()
        {
            var paths = LocalAssetPaths.Load();
            var source = paths?.CreateAssetSource();
            if (source == null)
            {
                Debug.LogError("[Craftwar] No Warcraft II install configured. " +
                                "Set \"dataRoot\" in LocalAssetPaths.json to the install's Data folder.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[Craftwar] Importing from {source.Describe()}");

            TilesetBaker.Bake(source);
            SoundBaker.Bake(source);
            MusicBaker.Bake(source);
            IconBaker.Bake(source);
            StringBaker.Bake(source);
            SpriteBaker.Bake(source);
            TeamColorMaterialSetup.EnsureMaterial();
            MapBaker.Bake(paths);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Craftwar] Import complete.");
        }
    }
}
