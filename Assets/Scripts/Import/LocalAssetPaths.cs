using System;
using System.IO;
using UnityEngine;

namespace Craftwar.Import
{
    /// <summary>
    /// Per-machine pointers to the user's own Warcraft 2 data (never committed;
    /// gitignored). Dev machines keep LocalAssetPaths.json in the project root;
    /// players get one written to persistentDataPath by the first-run import
    /// flow (M8).
    /// </summary>
    [Serializable]
    public sealed class LocalAssetPaths
    {
        public string maindatWar = "";
        public string mapsDir = "";
        public string defaultMap = "";

        public static string ProjectRootPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LocalAssetPaths.json"));

        public static string PersistentPath =>
            Path.Combine(Application.persistentDataPath, "LocalAssetPaths.json");

        public static LocalAssetPaths Load()
        {
            foreach (string path in new[] { ProjectRootPath, PersistentPath })
            {
                try
                {
                    if (File.Exists(path))
                        return JsonUtility.FromJson<LocalAssetPaths>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Craftwar] Failed reading {path}: {e.Message}");
                }
            }
            return null;
        }

        public void Save(string path)
        {
            File.WriteAllText(path, JsonUtility.ToJson(this, prettyPrint: true));
        }
    }
}
