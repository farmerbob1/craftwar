using Craftwar.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Single-player pause menu. Modal, so the input router kills world and
    /// camera input while it is up. Options/Save are placeholders until M8.
    /// </summary>
    public sealed class PauseMenuScreen : UIScreen
    {
        readonly UIManager _manager;
        readonly ISimHost _host;

        public override bool IsModal => true;

        public PauseMenuScreen(UIManager manager, ISimHost host)
        {
            _manager = manager;
            _host = host;
        }

        public override void Attach(VisualElement layerRoot, UIAssetCatalog assets)
        {
            VisualElement root;
            if (assets.pauseMenuScreen != null)
            {
                var clone = assets.pauseMenuScreen.Instantiate();
                clone.style.position = Position.Absolute;
                clone.style.left = 0;
                clone.style.top = 0;
                clone.style.right = 0;
                clone.style.bottom = 0;
                clone.pickingMode = PickingMode.Ignore; // the scrim inside does the blocking
                root = clone;
            }
            else
            {
                root = BuildFallback();
            }
            layerRoot.Add(root);
            Root = root;

            Bind("resume", () => _manager.Pop());
            Bind("quit", Quit);
            Bind("surrender", Surrender);
            Bind("options", () => _manager.Push(new OptionsScreen(_manager)));

            // Live at last (M10 SimSerializer). Still disabled in multiplayer: a
            // save is one peer's private copy, and reloading it would drop that
            // peer out of the shared turn schedule.
            if (_host != null && _host.CanPauseLocally)
                Bind("save", SaveGame);
            else
                Disable("save");
        }

        void SaveGame()
        {
            if (_host == null)
                return;
            var button = Root.Q<Button>("save");
            bool saved = _host.SaveGame(out string path);
            if (button != null)
                button.text = saved
                    ? $"Saved  ({System.IO.Path.GetFileNameWithoutExtension(path)})"
                    : "Save failed";
            if (button != null)
                button.SetEnabled(!saved);
        }

        void Bind(string name, System.Action action)
        {
            var button = Root.Q<Button>(name);
            if (button != null)
                button.clicked += action;
        }

        void Disable(string name)
        {
            var button = Root.Q<Button>(name);
            if (button != null)
                button.SetEnabled(false);
        }

        static VisualElement BuildFallback()
        {
            var scrim = new VisualElement { name = "scrim" };
            scrim.AddToClassList("screen-scrim");
            var menu = new VisualElement { name = "menu" };
            menu.AddToClassList("menu");
            var title = new Label("Paused") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("menu__title");
            menu.Add(title);
            foreach (var (n, text) in new[]
                     {
                         ("resume", "Resume"), ("options", "Options"),
                         ("save", "Save Game"), ("surrender", "Surrender"), ("quit", "Quit"),
                     })
            {
                var b = new Button { name = n, text = text };
                b.AddToClassList("menu__button");
                menu.Add(b);
            }
            scrim.Add(menu);
            return scrim;
        }

        public override void OnPush() => _host?.SetPaused(true);
        public override void OnPop() => _host?.SetPaused(false);

        public override bool HandleEscape()
        {
            _manager.Pop();
            return true;
        }

        /// <summary>
        /// Concede. Goes through the lockstep driver like any other command, so
        /// it stays deterministic and lands in the replay. Also the way out of
        /// the faithful stall where a player with one peasant and no gold can
        /// neither win nor be defeated.
        /// </summary>
        void Surrender()
        {
            if (_host == null)
                return;
            var cmd = new GameCommand
            {
                Op = CommandOp.Surrender,
                Player = HudScreen.LocalPlayer,
                SelectionCount = 0,
            };
            _host.SubmitCommand(cmd);
            _manager.Pop(); // unpause so the sim can execute the turn
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
