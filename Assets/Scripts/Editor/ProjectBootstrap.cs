using Craftwar.App;
using Craftwar.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

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

        public static void Run()
        {
            EnsureRenderer2D();
            EnsureGameScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Craftwar] ProjectBootstrap complete.");
        }

        [MenuItem("Craftwar/Setup/Ensure 2D Renderer")]
        public static void EnsureRenderer2D()
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
        }

        const string GameScenePath = "Assets/Scenes/Game.unity";

        [MenuItem("Craftwar/Setup/Ensure Game Scene")]
        public static void EnsureGameScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) != null)
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Grid + terrain tilemap
            var gridGo = new GameObject("Grid", typeof(Grid));
            var terrainGo = new GameObject("Terrain", typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapView));
            terrainGo.transform.SetParent(gridGo.transform, false);
            terrainGo.GetComponent<TilemapRenderer>().sortingOrder = 0;

            // Camera: orthographic, pixel perfect (32 PPU, 640x480 reference)
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(CameraRig));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7.5f; // 480px / 2 / 32ppu
            cam.backgroundColor = Color.black;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(32f, 32f, -10f);
            var ppc = camGo.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = 32;
            ppc.refResolutionX = 640;
            ppc.refResolutionY = 480;

            // Bootstrap object wired to the view components
            var bootGo = new GameObject("GameBootstrap", typeof(GameBootstrap));
            var so = new SerializedObject(bootGo.GetComponent<GameBootstrap>());
            so.FindProperty("tilemapView").objectReferenceValue = terrainGo.GetComponent<TilemapView>();
            so.FindProperty("cameraRig").objectReferenceValue = camGo.GetComponent<CameraRig>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, GameScenePath);
            Debug.Log($"[Craftwar] Created {GameScenePath}");
        }
    }
}
