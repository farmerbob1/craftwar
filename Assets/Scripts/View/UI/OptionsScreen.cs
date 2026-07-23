using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// In-game options, pushed from the pause menu. One tab so far — Gameplay
    /// (fog of war, game speed) — with the tab bar in place so Audio/Video can
    /// join later. Everything writes <see cref="GameplaySettings"/>, which the
    /// views and the game loop read live, so changes apply the moment the
    /// pause menu closes (fog even sooner — the views keep drawing while
    /// paused).
    /// </summary>
    public sealed class OptionsScreen : UIScreen
    {
        readonly UIManager _manager;

        Button _fogButton;
        Button _speedButton;

        public override bool IsModal => true;

        public OptionsScreen(UIManager manager) => _manager = manager;

        public override void Attach(VisualElement layerRoot, UIAssetCatalog assets)
        {
            var scrim = new VisualElement { name = "scrim" };
            scrim.AddToClassList("screen-scrim");

            var menu = new VisualElement { name = "menu" };
            menu.AddToClassList("menu");
            scrim.Add(menu);

            var title = new Label("Options") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("menu__title");
            menu.Add(title);

            // Tab bar. A single selected tab today; more tabs slot in here.
            var tabs = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var gameplayTab = new Button { text = "Gameplay" };
            gameplayTab.AddToClassList("menu__button");
            gameplayTab.SetEnabled(false); // the one and only tab is always active
            tabs.Add(gameplayTab);
            menu.Add(tabs);

            _fogButton = new Button(ToggleFog);
            _fogButton.AddToClassList("menu__button");
            menu.Add(_fogButton);

            _speedButton = new Button(() => CycleSpeed(1));
            _speedButton.AddToClassList("menu__button");
            menu.Add(_speedButton);

            var back = new Button(() => _manager.Pop()) { text = "Back" };
            back.AddToClassList("menu__button");
            menu.Add(back);

            RefreshLabels();
            layerRoot.Add(scrim);
            Root = scrim;
        }

        void ToggleFog()
        {
            GameplaySettings.Current.revealMap = !GameplaySettings.Current.revealMap;
            GameplaySettings.Save();
            RefreshLabels();
        }

        void CycleSpeed(int delta)
        {
            GameplaySettings.Current.CycleSpeed(delta);
            GameplaySettings.Save();
            RefreshLabels();
        }

        void RefreshLabels()
        {
            var s = GameplaySettings.Current;
            _fogButton.text = s.revealMap ? "Fog of War: Off" : "Fog of War: On";
            _speedButton.text = $"Game Speed: {s.SpeedLabel}";
        }

        public override bool HandleEscape()
        {
            _manager.Pop();
            return true;
        }
    }
}
