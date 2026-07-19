using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Single serialized entry point for every UI Toolkit asset. Lives at
    /// Assets/UI/Resources/UIAssetCatalog.asset so UIManager can pull it with
    /// one Resources.Load — no per-scene inspector wiring. Created idempotently
    /// by Craftwar/Setup/Ensure UI Assets.
    /// </summary>
    public sealed class UIAssetCatalog : ScriptableObject
    {
        public const string ResourceName = "UIAssetCatalog";

        [Header("Panel")]
        public PanelSettings panelSettings;

        [Header("Screens")]
        public VisualTreeAsset hudScreen;
        public VisualTreeAsset pauseMenuScreen;

        [Header("Templates")]
        public VisualTreeAsset commandButton;
        public VisualTreeAsset unitTile;

        public static UIAssetCatalog Load() => Resources.Load<UIAssetCatalog>(ResourceName);
    }
}
