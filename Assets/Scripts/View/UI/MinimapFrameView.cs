using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Reserves the sidebar's top slot for the M6 minimap and, more usefully
    /// right now, makes that area pickable so clicks there never reach the
    /// battlefield. <see cref="Content"/> is where the minimap render texture
    /// goes at M6.
    /// </summary>
    public sealed class MinimapFrameView
    {
        public VisualElement Root { get; }
        public VisualElement Content { get; }

        public MinimapFrameView(VisualElement hudRoot)
        {
            Root = hudRoot.Q("minimap-frame");
            Content = hudRoot.Q("minimap-content");
        }
    }
}
