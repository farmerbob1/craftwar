using System.Collections.Generic;
using System.IO;
using Craftwar.App;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Replaces the free-text map path with a dropdown of the .pud files sitting
    /// in StreamingAssets/Maps. The serialized value stays a plain string — a
    /// bare file name, which GameBootstrap resolves against that folder — so
    /// the scene does not bake in one machine's absolute paths.
    /// </summary>
    [CustomEditor(typeof(GameBootstrap))]
    public sealed class GameBootstrapEditor : Editor
    {
        const string DefaultLabel = "(LocalAssetPaths default)";
        const string MapField = "mapOverridePath";

        // Cached: OnInspectorGUI runs many times a second and must not hit disk.
        string[] _mapFiles = System.Array.Empty<string>();
        GUIContent[] _options = System.Array.Empty<GUIContent>();

        void OnEnable() => RefreshMapList();

        void RefreshMapList()
        {
            var files = new List<string>();
            string dir = GameBootstrap.StreamingMapsDir;
            if (Directory.Exists(dir))
            {
                var found = Directory.GetFiles(dir, "*.pud");
                // GetFiles order is filesystem-dependent; sort so the dropdown
                // is stable across machines.
                System.Array.Sort(found, System.StringComparer.OrdinalIgnoreCase);
                foreach (string f in found)
                    files.Add(Path.GetFileName(f));
            }
            _mapFiles = files.ToArray();
            RebuildOptions(null);
        }

        /// <summary>Options are [default] + maps, plus the current value if it is
        /// something else (an old absolute path) so opening the inspector never
        /// silently rewrites it.</summary>
        void RebuildOptions(string stray)
        {
            var opts = new List<GUIContent> { new GUIContent(DefaultLabel) };
            foreach (string m in _mapFiles)
                opts.Add(new GUIContent(Path.GetFileNameWithoutExtension(m)));
            if (!string.IsNullOrEmpty(stray))
                opts.Add(new GUIContent($"{stray}  (external)"));
            _options = opts.ToArray();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var mapProp = serializedObject.FindProperty(MapField);
            if (mapProp == null)
            {
                // Field renamed or removed — fall back rather than draw nothing.
                DrawDefaultInspector();
                return;
            }

            DrawPropertiesExcluding(serializedObject, MapField, "m_Script");
            DrawMapDropdown(mapProp);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawMapDropdown(SerializedProperty mapProp)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Map", EditorStyles.boldLabel);

            string current = mapProp.stringValue ?? "";
            int index = IndexOf(current, out bool isStray);
            RebuildOptions(isStray ? current : null);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                int picked = EditorGUILayout.Popup(
                    new GUIContent("Map", "Loaded on Play. Files come from StreamingAssets/Maps."),
                    index, _options);
                if (EditorGUI.EndChangeCheck())
                {
                    // 0 = default; 1..n = maps; anything past that is the stray,
                    // which we leave untouched.
                    if (picked == 0)
                        mapProp.stringValue = "";
                    else if (picked - 1 < _mapFiles.Length)
                        mapProp.stringValue = _mapFiles[picked - 1];
                }

                if (GUILayout.Button("Refresh", GUILayout.Width(60)))
                    RefreshMapList();
            }

            if (_mapFiles.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No .pud files in StreamingAssets/Maps. Copy maps there to " +
                    "populate this list — they are gitignored, so they stay local.",
                    MessageType.Info);
                if (GUILayout.Button("Reveal folder"))
                {
                    Directory.CreateDirectory(GameBootstrap.StreamingMapsDir);
                    EditorUtility.RevealInFinder(GameBootstrap.StreamingMapsDir);
                }
            }
            else if (isStray)
            {
                EditorGUILayout.HelpBox(
                    $"Current override is an external path:\n{current}\n" +
                    "Pick a map above to switch to a portable StreamingAssets entry.",
                    MessageType.Warning);
            }
            else if (index == 0)
            {
                EditorGUILayout.HelpBox(
                    "Using LocalAssetPaths.defaultMap from the per-machine " +
                    "LocalAssetPaths.json.", MessageType.None);
            }
        }

        int IndexOf(string value, out bool isStray)
        {
            isStray = false;
            if (string.IsNullOrEmpty(value))
                return 0;
            for (int i = 0; i < _mapFiles.Length; i++)
                if (string.Equals(_mapFiles[i], value, System.StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            isStray = true;
            return _mapFiles.Length + 1; // the appended "(external)" entry
        }
    }
}
