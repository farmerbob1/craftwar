using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// In-game options, pushed from the pause menu. Two tabs: Gameplay (fog of
    /// war, game speed) and Sound (master / music / effects). Everything writes
    /// <see cref="GameplaySettings"/>, which the views, the audio directors and
    /// the game loop read live, so changes apply the moment they are made (fog
    /// even sooner — the views keep drawing while paused).
    /// </summary>
    public sealed class OptionsScreen : UIScreen
    {
        enum Tab { Gameplay = 0, Sound }

        readonly UIManager _manager;

        Button _gameplayTab, _soundTab;
        VisualElement _gameplayPage, _soundPage;
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

            var tabs = new VisualElement();
            tabs.AddToClassList("options-tabs");
            _gameplayTab = TabButton(tabs, "Gameplay", Tab.Gameplay);
            _soundTab = TabButton(tabs, "Sound", Tab.Sound);
            menu.Add(tabs);

            // --- gameplay page ---
            _gameplayPage = new VisualElement();
            _fogButton = new Button(ToggleFog);
            _fogButton.AddToClassList("menu__button");
            _gameplayPage.Add(_fogButton);

            _speedButton = new Button(() => CycleSpeed(1));
            _speedButton.AddToClassList("menu__button");
            _gameplayPage.Add(_speedButton);
            menu.Add(_gameplayPage);

            // --- sound page ---
            _soundPage = new VisualElement();
            SoundOptionsPanel.Build(_soundPage);
            menu.Add(_soundPage);

            var back = new Button(() => _manager.Pop()) { text = "Back" };
            back.AddToClassList("menu__button");
            menu.Add(back);

            RefreshLabels();
            ShowTab(Tab.Gameplay);
            layerRoot.Add(scrim);
            Root = scrim;
        }

        Button TabButton(VisualElement parent, string text, Tab tab)
        {
            var b = new Button(() => ShowTab(tab)) { text = text };
            b.AddToClassList("menu__button");
            b.AddToClassList("options-tab");
            parent.Add(b);
            return b;
        }

        void ShowTab(Tab tab)
        {
            _gameplayPage.style.display = tab == Tab.Gameplay ? DisplayStyle.Flex : DisplayStyle.None;
            _soundPage.style.display = tab == Tab.Sound ? DisplayStyle.Flex : DisplayStyle.None;
            // The active tab is the one you cannot press.
            _gameplayTab.SetEnabled(tab != Tab.Gameplay);
            _soundTab.SetEnabled(tab != Tab.Sound);
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
