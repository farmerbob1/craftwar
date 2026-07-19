using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// The one UI MonoBehaviour. Owns the UIDocument/panel, builds the four
    /// layer roots, drives the screen stack and republishes pointer-over-UI
    /// into <see cref="UIState"/> each frame. Everything else in the UI is
    /// plain C# hanging off a VisualElement subtree.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIManager : MonoBehaviour
    {
        ISimHost _host;
        UIState _ui;
        UIAssetCatalog _assets;
        UIDocument _document;
        UIScreenStack _stack;

        VisualElement _layerHud, _layerScreens, _layerOverlay, _layerNotify;

        public HudScreen Hud { get; private set; }

        /// <summary>Layer for non-interactive world-space decorations (drag rect).</summary>
        public VisualElement OverlayLayer => _layerOverlay;

        public void Init(ISimHost host, UIState ui)
        {
            _host = host;
            _ui = ui;

            _assets = UIAssetCatalog.Load();
            if (_assets == null)
            {
                Debug.LogError("[Craftwar] UIAssetCatalog not found in Resources. " +
                               "Run Craftwar/Setup/Ensure UI Assets.");
                enabled = false;
                return;
            }

            _document = GetComponent<UIDocument>();
            _document.panelSettings = _assets.panelSettings;

            var root = _document.rootVisualElement;
            root.name = "root";
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1f;

            _layerHud = AddLayer(root, "layer-hud", PickingMode.Ignore);
            _layerScreens = AddLayer(root, "layer-screens", PickingMode.Ignore);
            _layerOverlay = AddLayer(root, "layer-overlay", PickingMode.Ignore);
            _layerNotify = AddLayer(root, "layer-notify", PickingMode.Ignore);

            _stack = new UIScreenStack(_assets);
            Hud = new HudScreen(_host, _ui, _layerNotify);
            _stack.Push(Hud, _layerHud);
        }

        /// <summary>
        /// Full-bleed transparent container. Layers themselves never pick —
        /// a single pickable full-screen element would swallow the whole
        /// battlefield, so only real panels inside them are pickable.
        /// </summary>
        static VisualElement AddLayer(VisualElement root, string name, PickingMode picking)
        {
            var layer = new VisualElement { name = name, pickingMode = picking };
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.top = 0;
            layer.style.right = 0;
            layer.style.bottom = 0;
            root.Add(layer);
            return layer;
        }

        void Update()
        {
            if (_stack == null)
                return;

            var mouse = Mouse.current;
            _ui.PointerOverUI = mouse != null && IsPointerOverUI(mouse.position.ReadValue());
            _ui.ModalOpen = _stack.AnyModal;
            _stack.Tick();
        }

        /// <summary>
        /// True if a pickable UI element sits under this screen-space point.
        /// <paramref name="screenPos"/> is Unity's bottom-left origin; panel
        /// space is top-left, and RuntimePanelUtils does not flip for us.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPos)
        {
            var panel = _document != null ? _document.rootVisualElement?.panel : null;
            if (panel == null)
                return false; // no layout yet
            var flipped = new Vector2(screenPos.x, Screen.height - screenPos.y);
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, flipped);
            return panel.Pick(panelPos) != null;
        }

        /// <summary>Fan a frame's worth of sim events out to the HUD.</summary>
        public void HandleSimEvents(System.Collections.Generic.List<Sim.SimEvent> events)
        {
            if (events.Count > 0)
                Hud?.HandleSimEvents(events);
        }

        public void Push(UIScreen screen) => _stack.Push(screen, _layerScreens);
        public void Pop() => _stack.Pop();
        public bool RouteEscape() => _stack.RouteEscape();
        public bool HasScreen<T>() where T : UIScreen => _stack.Contains<T>();

        /// <summary>Opens the pause menu if it isn't already up.</summary>
        public void OpenPauseMenu()
        {
            if (!HasScreen<PauseMenuScreen>())
                Push(new PauseMenuScreen(this, _host));
        }
    }
}
