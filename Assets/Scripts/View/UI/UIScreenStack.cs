using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Ordered screen stack. The HUD is pushed first and never popped; menus
    /// stack above it. Screens are attached to whichever layer root they were
    /// pushed with, so the HUD can live in #layer-hud while menus go to
    /// #layer-screens and still share one stack for ticking and Escape routing.
    /// </summary>
    public sealed class UIScreenStack
    {
        readonly List<UIScreen> _screens = new List<UIScreen>();
        readonly UIAssetCatalog _assets;

        public UIScreenStack(UIAssetCatalog assets) => _assets = assets;

        public int Count => _screens.Count;
        public UIScreen Top => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;

        /// <summary>True while any screen on the stack is modal.</summary>
        public bool AnyModal { get; private set; }

        public void Push(UIScreen screen, VisualElement layerRoot)
        {
            Top?.OnBlur();
            screen.Attach(layerRoot, _assets);
            _screens.Add(screen);
            screen.OnPush();
            screen.OnFocus();
            RecomputeModal();
        }

        /// <summary>
        /// Swap the bottom screen for another, discarding anything above it.
        /// Pop() deliberately refuses to remove the bottom entry, so this is the
        /// only way to change it — needed when the menu scene replaces the
        /// import wizard with the main menu once data has been located.
        /// </summary>
        public void ReplaceRoot(UIScreen screen, VisualElement layerRoot)
        {
            for (int i = _screens.Count - 1; i >= 0; i--)
            {
                _screens[i].OnBlur();
                _screens[i].OnPop();
                _screens[i].Detach();
            }
            _screens.Clear();
            Push(screen, layerRoot);
        }

        /// <summary>Pops the top screen. The bottom entry (the HUD) is never popped.</summary>
        public UIScreen Pop()
        {
            if (_screens.Count <= 1)
                return null;
            var screen = _screens[_screens.Count - 1];
            _screens.RemoveAt(_screens.Count - 1);
            screen.OnBlur();
            screen.OnPop();
            screen.Detach();
            RecomputeModal();
            Top?.OnFocus();
            return screen;
        }

        /// <summary>True if the stack already contains a screen of this type.</summary>
        public bool Contains<T>() where T : UIScreen
        {
            for (int i = 0; i < _screens.Count; i++)
                if (_screens[i] is T)
                    return true;
            return false;
        }

        public void Tick()
        {
            // Iterate by index: a screen may pop itself during Tick.
            for (int i = _screens.Count - 1; i >= 0; i--)
                if (i < _screens.Count)
                    _screens[i].Tick();
        }

        /// <summary>Offers Escape to screens top-down; true if one consumed it.</summary>
        public bool RouteEscape()
        {
            for (int i = _screens.Count - 1; i >= 0; i--)
                if (_screens[i].HandleEscape())
                    return true;
            return false;
        }

        void RecomputeModal()
        {
            AnyModal = false;
            for (int i = 0; i < _screens.Count; i++)
                if (_screens[i].IsModal)
                {
                    AnyModal = true;
                    return;
                }
        }
    }
}
