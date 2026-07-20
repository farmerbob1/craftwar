using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// One entry on the screen stack. Screens own a VisualElement subtree
    /// parented under a layer root; they are plain C# (no MonoBehaviour) and
    /// are ticked by UIManager.
    /// </summary>
    public abstract class UIScreen
    {
        /// <summary>Modal screens block world/camera input while on the stack.</summary>
        public virtual bool IsModal => false;

        /// <summary>This screen's root element, created in <see cref="Attach"/>.</summary>
        public VisualElement Root { get; protected set; }

        /// <summary>Build the subtree under <paramref name="layerRoot"/>.</summary>
        public abstract void Attach(VisualElement layerRoot, UIAssetCatalog assets);

        public virtual void OnPush() { }
        public virtual void OnPop() { }
        public virtual void OnFocus() { }
        public virtual void OnBlur() { }

        /// <summary>
        /// Per-frame refresh. Every screen on the stack ticks, not just the top
        /// one — so a screen that is covered must still cope with being ticked.
        /// (This has never mattered: the stack has only ever been [HUD, modal],
        /// where "all" and "top plus HUD" are the same thing. It starts to matter
        /// with the M8 menu stack.)
        /// </summary>
        public virtual void Tick() { }

        /// <summary>Return true if this screen consumed the Escape key.</summary>
        public virtual bool HandleEscape() => false;

        /// <summary>Remove the subtree from the panel.</summary>
        public virtual void Detach()
        {
            Root?.RemoveFromHierarchy();
            Root = null;
        }
    }
}
