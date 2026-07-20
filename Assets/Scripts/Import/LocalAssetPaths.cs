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
        /// <summary>Bumped when a field's meaning changes, so a stale file written
        /// by an older build is discarded rather than half-honoured.</summary>
        public const int CurrentSchema = 1;

        /// <summary>
        /// The installation's Data folder — the primary source. Everything the
        /// game needs is loose in here; see LooseFileAssetSource.
        /// </summary>
        public string dataRoot = "";

        /// <summary>
        /// Optional legacy fallback. There is no maindat.war in a Remastered
        /// install; this only ever points at a separately-obtained copy, and
        /// nothing requires it once dataRoot is set.
        /// </summary>
        public string maindatWar = "";

        public string mapsDir = "";
        public string defaultMap = "";

        /// <summary>Which Strings/&lt;locale&gt;.json to read. Ten ship with the game.</summary>
        public string locale = "enUS";

        public int schema = CurrentSchema;

        /// <summary>True once there is a usable asset source of either kind.</summary>
        public bool HasData =>
            (!string.IsNullOrEmpty(dataRoot) && Directory.Exists(dataRoot))
            || (!string.IsNullOrEmpty(maindatWar) && File.Exists(maindatWar));

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
            schema = CurrentSchema;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(this, prettyPrint: true));
        }

        /// <summary>
        /// Build an asset source from whatever this points at, preferring the
        /// loose install. Null when nothing usable is configured — callers must
        /// cope, because that is exactly the first-run state.
        /// </summary>
        public IAssetSource CreateAssetSource()
        {
            if (!string.IsNullOrEmpty(dataRoot) && Directory.Exists(dataRoot))
                return new LooseFileAssetSource(dataRoot);
            return null;
        }
    }
}
