using System;
using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using Craftwar.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// First-run flow: find the player's own Warcraft II installation and record
    /// where it is.
    ///
    /// There is nothing to extract. Every asset class the game needs — sprites,
    /// tilesets, icons, strings, sound, music — is read directly from the
    /// install at runtime, so this writes a JSON pointer and nothing else. That
    /// is also the licensing position: no Blizzard data is ever copied, and the
    /// player must own the game.
    ///
    /// Runs in the menu scene, before any sim exists, which is why MenuBootstrap
    /// is deliberately free of match machinery.
    /// </summary>
    public sealed class ImportWizardScreen : UIScreen
    {
        readonly UIManager _manager;
        readonly Action _onComplete;

        List<InstallCandidate> _candidates;
        int _selected;
        Label _detail;
        VisualElement _list;
        TextField _manualPath;

        public ImportWizardScreen(UIManager manager, Action onComplete)
        {
            _manager = manager;
            _onComplete = onComplete;
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

            var title = new Label { text = "Locate Warcraft II" };
            title.AddToClassList("menu__title");
            title.pickingMode = PickingMode.Ignore;
            menu.Add(title);

            var blurb = new Label
            {
                text = "Craftwar reads art, sound and music from your own copy of " +
                       "Warcraft II. Nothing is copied — the files stay where they are.",
            };
            blurb.AddToClassList("text");
            blurb.AddToClassList("text--dim");
            blurb.pickingMode = PickingMode.Ignore;
            blurb.style.whiteSpace = WhiteSpace.Normal;
            menu.Add(blurb);

            _list = new VisualElement { name = "candidates" };
            menu.Add(_list);

            _detail = new Label { name = "detail" };
            _detail.AddToClassList("text");
            _detail.pickingMode = PickingMode.Ignore;
            _detail.style.whiteSpace = WhiteSpace.Normal;
            menu.Add(_detail);

            // No native folder picker exists in a player build
            // (EditorUtility.OpenFolderPanel is editor-only and no file-browser
            // package is installed), so a validated text field is the honest
            // fallback rather than a dependency.
            _manualPath = new TextField("Or enter a folder");
            menu.Add(_manualPath);
            menu.Add(MainMenuScreen.MenuButton("check", "Check This Folder", CheckManual));

            menu.Add(MainMenuScreen.MenuButton("use", "Use This Copy", Accept));
            menu.Add(MainMenuScreen.MenuButton("rescan", "Search Again", Rescan));

            layerRoot.Add(scrim);
            Root = scrim;

            Rescan();
        }

        void Rescan()
        {
            _candidates = Wc2InstallLocator.Find();
            _selected = 0;
            RebuildList();
        }

        void CheckManual()
        {
            string path = _manualPath?.value?.Trim();
            if (string.IsNullOrEmpty(path))
                return;
            if (!Directory.Exists(path))
            {
                _detail.text = "That folder does not exist.";
                return;
            }

            var c = Wc2InstallLocator.Inspect(path, "chosen by you");
            _candidates.Insert(0, c);
            _selected = 0;
            RebuildList();
        }

        void RebuildList()
        {
            _list.Clear();
            if (_candidates.Count == 0)
            {
                _detail.text = "No installation found. Enter the folder containing " +
                               "your Warcraft II data (the one holding Art and Gamesfx).";
                return;
            }

            for (int i = 0; i < _candidates.Count && i < 4; i++)
            {
                int index = i; // capture
                var c = _candidates[i];
                var b = MainMenuScreen.MenuButton($"cand{i}", c.Origin, () =>
                {
                    _selected = index;
                    ShowDetail();
                });
                _list.Add(b);
            }
            ShowDetail();
        }

        void ShowDetail()
        {
            if (_candidates.Count == 0)
                return;
            var c = _candidates[_selected];

            // Name the parts rather than reporting a bare pass/fail: a partial
            // copy is playable, and the player should see what they will miss.
            var parts = new List<string>();
            parts.Add(Mark(c.HasTilesets) + " terrain");
            parts.Add(Mark(c.HasSprites) + " units");
            parts.Add(Mark(c.HasSounds) + " sound");
            parts.Add(Mark(c.HasIcons) + " icons");
            parts.Add(Mark(c.HasStrings) + " names");

            _detail.text = c.DataRoot + "\n" + string.Join("   ", parts)
                + (c.IsUsable ? string.Empty : "\n\nTerrain and units are required.");
        }

        static string Mark(bool ok) => ok ? "+" : "-";

        void Accept()
        {
            if (_candidates.Count == 0)
                return;
            var c = _candidates[_selected];
            if (!c.IsUsable)
            {
                _detail.text = "That folder is missing terrain or unit art.\n" + c.DataRoot;
                return;
            }

            var paths = LocalAssetPaths.Load() ?? new LocalAssetPaths();
            paths.dataRoot = c.DataRoot;

            var mapFolders = Wc2InstallLocator.MapFolders(c.DataRoot);
            if (mapFolders.Count > 0)
                paths.mapsDir = mapFolders[0];

            try
            {
                // persistentDataPath, not the project root: players have no
                // project. Load() checks the project root first, so a developer's
                // hand-written file still wins.
                paths.Save(LocalAssetPaths.PersistentPath);
            }
            catch (IOException e)
            {
                _detail.text = "Could not save settings: " + e.Message;
                return;
            }

            Debug.Log($"[Craftwar] Import complete: {c.DataRoot}");
            _onComplete?.Invoke();
        }
    }
}
