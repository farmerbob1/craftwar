using System;
using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// End-of-match announcement. Modal, so world and camera input die while it
    /// is up, and it pauses the sim on push exactly like the pause menu.
    ///
    /// Driven by the hashed <see cref="PlayerState.Outcome"/> rather than by
    /// catching the one-frame PlayerDefeated/PlayerVictorious event: a screen
    /// that must appear should not depend on being present for a single frame.
    /// </summary>
    public sealed class VictoryScreen : UIScreen
    {
        public override bool IsModal => true;

        readonly UIManager _manager;
        readonly ISimHost _host;
        readonly PlayerOutcome _outcome;
        readonly Action _onRestart;
        readonly Action _onQuitToMenu;

        public VictoryScreen(UIManager manager, ISimHost host, PlayerOutcome outcome,
                             Action onRestart, Action onQuitToMenu)
        {
            _manager = manager;
            _host = host;
            _outcome = outcome;
            _onRestart = onRestart;
            _onQuitToMenu = onQuitToMenu;
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

            bool won = _outcome == PlayerOutcome.Victorious;
            var title = new Label { text = won ? "Victory!" : "Defeat" };
            title.AddToClassList("menu__title");
            title.pickingMode = PickingMode.Ignore;
            menu.Add(title);

            // Watching on after the result is the original's behaviour and is
            // genuinely useful in a skirmish; it just unpauses and pops.
            AddButton(menu, "continue", won ? "Keep Playing" : "Watch On", () => _manager.Pop());
            AddButton(menu, "restart", "Play Again", () => _onRestart?.Invoke());
            AddButton(menu, "menu", "Main Menu", () => _onQuitToMenu?.Invoke());

            layerRoot.Add(scrim);
            Root = scrim;
        }

        static void AddButton(VisualElement parent, string name, string text, Action onClick)
        {
            var button = new Button(() => onClick?.Invoke()) { name = name, text = text };
            button.AddToClassList("menu__button");
            parent.Add(button);
        }

        public override void OnPush() => _host?.SetPaused(true);
        public override void OnPop() => _host?.SetPaused(false);

        /// <summary>Escape dismisses back to the board rather than closing the match.</summary>
        public override bool HandleEscape()
        {
            _manager.Pop();
            return true;
        }
    }
}
