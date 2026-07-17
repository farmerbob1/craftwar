using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// One-time project scaffolding that must run inside the editor because the
    /// assets involved (Renderer2DData) populate their internal resource
    /// references in Awake/Reset. Idempotent: safe to run repeatedly.
    /// Batch-mode entry point: -executeMethod Craftwar.EditorTools.ProjectBootstrap.Run
    /// </summary>
    public static class ProjectBootstrap
    {
        const string Renderer2DPath = "Assets/Settings/Renderer2D.asset";
        static readonly string[] PipelineAssetPaths =
        {
            "Assets/Settings/PC_RPAsset.asset",
            "Assets/Settings/Mobile_RPAsset.asset",
        };

        [MenuItem("Craftwar/Setup/Ensure 2D Renderer")]
        public static void Run()
        {
            var renderer2D = AssetDatabase.LoadAssetAtPath<Renderer2DData>(Renderer2DPath);
            if (renderer2D == null)
            {
                renderer2D = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer2D, Renderer2DPath);
                Debug.Log($"[Craftwar] Created {Renderer2DPath}");
            }

            foreach (var path in PipelineAssetPaths)
            {
                var rpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (rpAsset == null)
                {
                    Debug.LogWarning($"[Craftwar] Pipeline asset not found: {path}");
                    continue;
                }

                var so = new SerializedObject(rpAsset);
                var list = so.FindProperty("m_RendererDataList");
                if (list.arraySize == 0)
                    list.arraySize = 1;
                var slot0 = list.GetArrayElementAtIndex(0);
                if (slot0.objectReferenceValue != renderer2D)
                {
                    slot0.objectReferenceValue = renderer2D;
                    so.FindProperty("m_DefaultRendererIndex").intValue = 0;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rpAsset);
                    Debug.Log($"[Craftwar] Assigned 2D renderer to {path}");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Craftwar] ProjectBootstrap complete.");
        }
    }
}
