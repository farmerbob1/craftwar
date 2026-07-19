using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// The drag-selection rectangle, drawn in the overlay layer. Replaces the
    /// old SelectionController.OnGUI box. Takes screen-space points (bottom-left
    /// origin) and converts them to panel space itself, so callers keep working
    /// in the same coordinates the camera uses.
    /// </summary>
    public sealed class DragSelectOverlayView
    {
        readonly VisualElement _rect;
        readonly IPanel _panel;
        bool _visible;

        public DragSelectOverlayView(VisualElement overlayLayer)
        {
            _rect = new VisualElement { name = "drag-select", pickingMode = PickingMode.Ignore };
            _rect.AddToClassList("drag-select");
            _rect.style.position = Position.Absolute;
            _rect.style.display = DisplayStyle.None;
            overlayLayer.Add(_rect);
            _panel = overlayLayer.panel;
        }

        public void Show(Vector2 screenA, Vector2 screenB)
        {
            var panel = _panel ?? _rect.panel;
            if (panel == null)
                return;

            // Screen space is bottom-left origin, panel space is top-left, and
            // ScreenToPanel does not flip for us.
            var a = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(screenA.x, Screen.height - screenA.y));
            var b = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(screenB.x, Screen.height - screenB.y));

            float left = Mathf.Min(a.x, b.x);
            float top = Mathf.Min(a.y, b.y);
            _rect.style.left = left;
            _rect.style.top = top;
            _rect.style.width = Mathf.Abs(a.x - b.x);
            _rect.style.height = Mathf.Abs(a.y - b.y);

            if (_visible)
                return;
            _visible = true;
            _rect.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            if (!_visible)
                return;
            _visible = false;
            _rect.style.display = DisplayStyle.None;
        }
    }
}
