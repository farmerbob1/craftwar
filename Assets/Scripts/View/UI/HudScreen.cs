using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// The in-game HUD: permanent bottom entry on the screen stack, never
    /// popped. Owns the resource strip, minimap frame slot and (from phase 2)
    /// the command card and selection panel. Constructs its child views once
    /// in <see cref="Attach"/> and ticks them.
    /// </summary>
    public sealed class HudScreen : UIScreen
    {
        public const byte LocalPlayer = 0;

        readonly ISimHost _host;
        readonly UIState _ui;

        ResourcePanelView _resources;
        CommandCardView _card;
        SelectionPanelView _selection;
        NotificationFeedView _notifications;

        /// <summary>Exposed so the input router can fire grid hotkeys.</summary>
        public CommandCardView Card => _card;

        /// <summary>Where the notification feed mounts (phase 4).</summary>
        public VisualElement NotifyLayer { get; }

        public MinimapFrameView Minimap { get; private set; }

        MinimapView _minimapView;

        /// <summary>
        /// Handed over by GameBootstrap once the camera, palette and world
        /// input all exist — the HUD is built before any of them.
        /// </summary>
        public void SetMinimap(MinimapView view) => _minimapView = view;

        /// <summary>
        /// Hand the HUD icon atlas to everything that draws unit art — the
        /// command card, the selection portrait and the multi-selection tiles.
        /// Arrives after Attach, because the installation's art is decoded well
        /// after the HUD is built.
        /// </summary>
        public void SetIconProvider(IIconProvider icons)
        {
            _card?.SetIconProvider(icons);
            _selection?.SetIconProvider(icons);
        }

        public HudScreen(ISimHost host, UIState ui, VisualElement notifyLayer)
        {
            _host = host;
            _ui = ui;
            NotifyLayer = notifyLayer;
        }

        public override void Attach(VisualElement layerRoot, UIAssetCatalog assets)
        {
            // Instantiate() wraps the tree in a TemplateContainer; make it a
            // full-bleed, non-picking passthrough so it never blocks the world.
            var container = assets.hudScreen.Instantiate();
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            container.pickingMode = PickingMode.Ignore;
            layerRoot.Add(container);
            Root = container;

            _resources = new ResourcePanelView(Root);
            Minimap = new MinimapFrameView(Root);
            _card = new CommandCardView(Root, _host, _ui, assets, LocalPlayer);
            _selection = new SelectionPanelView(Root, _ui, assets);
            _notifications = new NotificationFeedView(NotifyLayer, LocalPlayer);
        }

        /// <summary>Drained once per frame by UIManager from the runner's batch.</summary>
        public void HandleSimEvents(System.Collections.Generic.List<Sim.SimEvent> events)
        {
            _notifications?.Handle(events);
            if (_notifications != null && _resources != null)
                _resources.FlashShortfall(_notifications.LastDeny);
        }

        public override void Tick()
        {
            var sim = _host?.Sim;
            if (sim == null)
                return;
            _resources.Tick(sim.State, LocalPlayer);
            _selection.Tick(sim);
            _card.Tick(sim);
            _minimapView?.Tick();
        }

        /// <summary>
        /// Escape order is placement-cancel (handled by InputRouter before the
        /// stack is consulted), then the card's Advanced page, then menus. The
        /// placement check is repeated here so the HUD is still correct if it is
        /// ever reached through a different path.
        /// </summary>
        public override bool HandleEscape()
        {
            if (_ui.HasPendingOrder)
            {
                _ui.ClearPendingOrder();
                return true;
            }
            return _card != null && _card.CloseAdvancedPage();
        }
    }
}
