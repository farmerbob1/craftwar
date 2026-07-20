using System;
using System.Collections.Generic;
using Craftwar.Import;
using Craftwar.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// Pick a map and start a skirmish.
    ///
    /// Slot configuration is deliberately left to the map: MatchConfig.slots
    /// stays null, so GameSim.Setup derives controllers, races and teams from
    /// the PUD's own OWNR/SIDE exactly as it always has. Overriding those needs
    /// an AI to be worth anything (M9), and inventing a lobby UI before then
    /// would be guessing at requirements.
    /// </summary>
    public sealed class MatchSetupScreen : UIScreen
    {
        public override bool IsModal => true;

        readonly UIManager _manager;
        readonly LocalAssetPaths _paths;
        readonly Action<MatchConfig> _onStart;

        List<MapEntry> _maps;
        int _selected;
        Label _mapLabel;

        public MatchSetupScreen(UIManager manager, LocalAssetPaths paths, Action<MatchConfig> onStart)
        {
            _manager = manager;
            _paths = paths;
            _onStart = onStart;
        }

        public override void Attach(VisualElement layerRoot, UIAssetCatalog assets)
        {
            _maps = MapList.Find(_paths);

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

            var title = new Label { text = "Skirmish" };
            title.AddToClassList("menu__title");
            title.pickingMode = PickingMode.Ignore;
            menu.Add(title);

            if (_maps.Count == 0)
            {
                var warn = new Label
                {
                    text = "No .pud maps found.\n" +
                           "Checked StreamingAssets/Maps and LocalAssetPaths.mapsDir.",
                };
                warn.AddToClassList("text");
                warn.AddToClassList("text--warn");
                warn.pickingMode = PickingMode.Ignore;
                menu.Add(warn);
            }
            else
            {
                // A plain prev/next stepper rather than a dropdown: UI Toolkit's
                // DropdownField needs styling this theme does not have yet, and
                // the WC2 skin at Phase 6 will replace this chrome anyway.
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.Add(MainMenuScreen.MenuButton("prev", "<", () => Step(-1)));
                _mapLabel = new Label { text = _maps[0].Label };
                _mapLabel.AddToClassList("text");
                _mapLabel.pickingMode = PickingMode.Ignore;
                _mapLabel.style.flexGrow = 1f;
                _mapLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                row.Add(_mapLabel);
                row.Add(MainMenuScreen.MenuButton("next", ">", () => Step(1)));
                menu.Add(row);

                menu.Add(MainMenuScreen.MenuButton("start", "Start", Start));
            }

            menu.Add(MainMenuScreen.MenuButton("back", "Back", () => _manager.Pop()));

            layerRoot.Add(scrim);
            Root = scrim;
        }

        void Step(int delta)
        {
            if (_maps.Count == 0)
                return;
            _selected = (_selected + delta + _maps.Count) % _maps.Count;
            _mapLabel.text = _maps[_selected].Label;
        }

        void Start()
        {
            if (_maps.Count == 0)
                return;
            var config = MatchConfig.FromMapDefaults(_maps[_selected].Value);
            _onStart?.Invoke(config);
        }

        public override bool HandleEscape()
        {
            _manager.Pop();
            return true;
        }
    }
}
