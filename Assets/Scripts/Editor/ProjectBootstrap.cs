using Craftwar.App;
using Craftwar.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

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
            EnsureUiAssets();
            EnsureGameScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Craftwar] ProjectBootstrap complete.");
        }

        const string PanelSettingsPath = "Assets/UI/PanelSettings/CraftwarPanelSettings.asset";
        const string ThemePath = "Assets/UI/Themes/ThemeDark.tss";
        const string CatalogPath = "Assets/UI/Resources/UIAssetCatalog.asset";

        /// <summary>
        /// The UI Toolkit assets are generated, not committed, so a fresh clone
        /// (or a wiped Library) would otherwise start with a HUD-less scene and
        /// a Resources.Load failure at runtime. Generate them on reload when
        /// the catalog is missing; the menu item stays for a forced refresh.
        ///
        /// Checks every field, not just the catalog's existence. A catalog
        /// serialized before a template landed keeps a null reference forever —
        /// an absence-only check never repairs it, the screen silently falls
        /// back to its code-built version, and the UXML becomes dead code that
        /// looks wired. That is exactly what happened to pauseMenuScreen,
        /// commandButton and unitTile between the UI-framework commit and M8.
        /// </summary>
        [InitializeOnLoadMethod]
        static void EnsureUiAssetsOnLoad()
        {
            if (NeedsUiAssets())
                EditorApplication.delayCall += () => EnsureUiAssets();
        }

        static bool NeedsUiAssets()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIAssetCatalog>(CatalogPath);
            if (catalog == null)
                return true;
            return catalog.panelSettings == null
                || catalog.hudScreen == null
                || (catalog.pauseMenuScreen == null && HasUxml("PauseMenuScreen"))
                || (catalog.commandButton == null && HasUxml("CommandButton"))
                || (catalog.unitTile == null && HasUxml("UnitTile"));
        }

        /// <summary>A null field is only a defect once the template exists on disk;
        /// before that it is the honest state of an unbuilt screen.</summary>
        static bool HasUxml(string name) =>
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"Assets/UI/UXML/{name}.uxml") != null;

        /// <summary>
        /// Creates the PanelSettings + UIAssetCatalog that back the UI Toolkit
        /// layer and (re)binds the catalog to the UXML on disk. Idempotent:
        /// existing assets are refreshed in place, never recreated.
        /// </summary>
        [MenuItem("Craftwar/Setup/Ensure UI Assets")]
        public static void EnsureUiAssets()
        {
            foreach (var dir in new[] { "Assets/UI", "Assets/UI/PanelSettings", "Assets/UI/Resources" })
                if (!AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.CreateFolder(
                        System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'),
                        System.IO.Path.GetFileName(dir));

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
                Debug.LogWarning($"[Craftwar] Theme not found: {ThemePath}");

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
                Debug.Log($"[Craftwar] Created {PanelSettingsPath}");
            }
            // Scale by height: the HUD keeps a constant fraction of screen
            // height, so ultrawide gains battlefield instead of stretched chrome.
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 1f;
            if (theme != null)
                panel.themeStyleSheet = theme;
            EditorUtility.SetDirty(panel);

            var catalog = AssetDatabase.LoadAssetAtPath<UIAssetCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<UIAssetCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                Debug.Log($"[Craftwar] Created {CatalogPath}");
            }
            catalog.panelSettings = panel;
            catalog.hudScreen = LoadUxml("Assets/UI/UXML/HudScreen.uxml", required: true);
            catalog.pauseMenuScreen = LoadUxml("Assets/UI/UXML/PauseMenuScreen.uxml", required: false);
            catalog.commandButton = LoadUxml("Assets/UI/UXML/CommandButton.uxml", required: false);
            catalog.unitTile = LoadUxml("Assets/UI/UXML/UnitTile.uxml", required: false);
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
        }

        /// <summary>Optional templates are absent until the phase that adds them.</summary>
        static VisualTreeAsset LoadUxml(string path, bool required)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (asset == null && required)
                Debug.LogWarning($"[Craftwar] UXML not found: {path}");
            return asset;
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
