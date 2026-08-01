using System;
using System.Collections.Generic;
using Craftwar.App;
using Craftwar.Import;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Bakes every Strings/&lt;locale&gt;.json into one <see cref="LocalizedStringTable"/>
    /// asset per locale, pre-parsed so runtime never needs a JSON parser (the
    /// one that reads the source file, <c>Craftwar.Import.JsonValue</c>, is
    /// Editor-only once this feature lands).
    /// </summary>
    public static class StringBaker
    {
        const string TableDir = "Assets/GameData/Extracted/Resources/Strings";

        public static void Bake(IAssetSource source)
        {
            int locales = 0;
            foreach (string path in source.List("strings/"))
            {
                if (!path.EndsWith(".json", StringComparison.Ordinal))
                    continue;

                string locale = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!source.TryRead(path, out var bytes))
                    continue;

                Dictionary<string, string> map;
                try
                {
                    string json = System.Text.Encoding.UTF8.GetString(bytes);
                    map = JsonValue.Parse(json).ToStringMap();
                }
                catch (JsonException e)
                {
                    Debug.LogWarning($"[Craftwar] Strings/{locale}.json: {e.Message}. Skipped.");
                    continue;
                }
                if (map.Count == 0)
                    continue;

                string tablePath = $"{TableDir}/{locale}.asset";
                if (AssetDatabase.LoadAssetAtPath<LocalizedStringTable>(tablePath) != null)
                    AssetDatabase.DeleteAsset(tablePath);
                var table = BakeUtil.CreateOrLoadAsset<LocalizedStringTable>(tablePath);
                table.locale = locale;
                var entries = new LocalizedStringTable.Entry[map.Count];
                int i = 0;
                foreach (var kv in map)
                    entries[i++] = new LocalizedStringTable.Entry { key = kv.Key, value = kv.Value };
                table.entries = entries;
                EditorUtility.SetDirty(table);
                locales++;
            }

            Debug.Log($"[Craftwar] Baked {locales} locale string table(s) -> {TableDir}");
        }
    }
}
