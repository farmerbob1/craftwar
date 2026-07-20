using System;
using Craftwar.Import;
using Craftwar.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// The menu scene's root screen. Lives in Craftwar.App rather than beside
    /// the other UIScreens in Craftwar.View because it deals in MatchConfig, and
    /// View does not (and should not) reference App.
    /// </summary>
    public sealed class MainMenuScreen : UIScreen
    {
        readonly UIManager _manager;
        readonly LocalAssetPaths _paths;
        readonly Action<MatchConfig> _onStart;

        public MainMenuScreen(UIManager manager, LocalAssetPaths paths, Action<MatchConfig> onStart)
        {
            _manager = manager;
            _paths = paths;
            _onStart = onStart;
        }

        public override void Attach(VisualElement layerRoot, UIAssetCatalog assets)
        {
            var scrim = new VisualElement { name = "scrim" };
            scrim.AddToClassList("screen-scrim");
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0;
            scrim.style.top = 0;
            scrim.style.right = 0;
            scrim.style.bottom = 0;

            var menu = new VisualElement { name = "menu" };
            menu.AddToClassList("menu");
            scrim.Add(menu);

            var title = new Label { text = "Craftwar" };
            title.AddToClassList("menu__title");
            title.pickingMode = PickingMode.Ignore;
            menu.Add(title);

            // HasData, not maindatWar: dataRoot (the loose install) is the primary
            // source now, and a Remastered install has no maindat.war at all.
            bool haveData = _paths != null && _paths.HasData;

            var skirmish = MenuButton("skirmish", "Single Player",
                () => _manager.Push(new MatchSetupScreen(_manager, _paths, _onStart)));
            skirmish.SetEnabled(haveData);
            menu.Add(skirmish);

            // Placeholders, visible so the shape of the menu is honest.
            var options = MenuButton("options", "Options", null);
            options.SetEnabled(false);
            menu.Add(options);

            menu.Add(MenuButton("quit", "Quit", Quit));

            if (!haveData)
            {
                // Phase 8 replaces this with the import wizard; until then say
                // plainly what is wrong rather than presenting a dead button.
                var warn = new Label
                {
                    text = "No Warcraft II data found.\n" +
                           "Set up LocalAssetPaths.json to point at your installation.",
                };
                warn.AddToClassList("text");
                warn.AddToClassList("text--warn");
                warn.pickingMode = PickingMode.Ignore;
                menu.Add(warn);
            }

            layerRoot.Add(scrim);
            Root = scrim;
        }

        internal static Button MenuButton(string name, string text, Action onClick)
        {
            var b = new Button(() => onClick?.Invoke()) { name = name, text = text };
            b.AddToClassList("menu__button");
            return b;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
