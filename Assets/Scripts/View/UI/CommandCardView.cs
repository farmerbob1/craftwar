using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Renders <see cref="CommandCardModel"/> into nine pre-instantiated
    /// buttons and turns activations into GameCommands. The elements are
    /// created once and only ever get class/text changes — never rebuilt — so
    /// the card costs nothing per frame beyond int compares.
    /// </summary>
    public sealed class CommandCardView
    {
        const int SafetyRecheckTicks = 25;   // 2 Hz at 50 Hz

        readonly ISimHost _host;
        readonly UIState _ui;
        readonly byte _player;
        readonly CommandCardModel _model = new CommandCardModel();

        readonly VisualElement[] _buttons = new VisualElement[CommandCardModel.SlotCount];
        readonly Label[] _icons = new Label[CommandCardModel.SlotCount];
        readonly Label[] _keys = new Label[CommandCardModel.SlotCount];
        readonly CommandSlotKind[] _lastKind = new CommandSlotKind[CommandCardModel.SlotCount];
        readonly bool[] _lastEnabled = new bool[CommandCardModel.SlotCount];

        readonly Label _status;

        ulong _lastHash;
        bool _built;
        int _sinceRecheck;

        public CommandCardView(VisualElement hudRoot, ISimHost host, UIState ui,
            UIAssetCatalog assets, byte player)
        {
            _host = host;
            _ui = ui;
            _player = player;

            var card = hudRoot.Q("command-card");
            var sidebar = hudRoot.Q("sidebar");

            _status = new Label { name = "card-status", text = string.Empty };
            _status.AddToClassList("card-status__label");
            _status.pickingMode = PickingMode.Ignore;
            sidebar.Insert(sidebar.IndexOf(card), _status);

            for (int i = 0; i < CommandCardModel.SlotCount; i++)
            {
                VisualElement btn;
                if (assets.commandButton != null)
                {
                    var clone = assets.commandButton.Instantiate();
                    // Unwrap the TemplateContainer so the grid lays out the
                    // button itself, not an unstyled wrapper.
                    btn = clone.Q(className: "command-button") ?? clone;
                    btn.RemoveFromHierarchy();
                }
                else
                {
                    btn = BuildFallbackButton();
                }
                card.Add(btn);

                _buttons[i] = btn;
                _icons[i] = btn.Q<Label>("icon");
                // Filled per rebuild: the letter belongs to whatever command
                // lands in this slot, not to the slot.
                _keys[i] = btn.Q<Label>("key");

                int slot = i; // capture
                btn.RegisterCallback<ClickEvent>(_ => Activate(slot));
                // The button is the icon now, so the cost has nowhere to live on
                // it; the original put it on the status line under the card
                // while the cursor is over the button, which is what this does.
                btn.RegisterCallback<PointerEnterEvent>(_ => _hoverSlot = slot);
                btn.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (_hoverSlot == slot)
                        _hoverSlot = -1;
                });
                btn.AddToClassList("command-button--empty");
                _lastKind[i] = CommandSlotKind.None;
            }
        }

        /// <summary>Used only if the UXML template failed to load.</summary>
        static VisualElement BuildFallbackButton()
        {
            var btn = new VisualElement();
            btn.AddToClassList("command-button");
            var icon = new Label { name = "icon", text = "?" };
            icon.AddToClassList("command-button__icon");
            icon.pickingMode = PickingMode.Ignore;
            var key = new Label { name = "key" };
            key.AddToClassList("command-button__key");
            key.pickingMode = PickingMode.Ignore;
            btn.Add(icon);
            btn.Add(key);
            return btn;
        }

        public void Tick(GameSim sim)
        {
            var state = sim.State;

            // Cheap shape check every frame, plus a slow full re-check so a
            // change nothing hashes (a prereq building dying elsewhere) can't
            // leave the card stale forever.
            ulong hash = _model.ComputeStructureHash(state, _ui.Selection, _player);
            bool due = ++_sinceRecheck >= SafetyRecheckTicks;
            if (!_built || hash != _lastHash || due)
            {
                if (due)
                    _sinceRecheck = 0;
                _model.Rebuild(sim, state, _ui.Selection, _player);
                _lastHash = _model.StructureHash;
                _built = true;
                Render();
            }

            _model.RefreshEnabled(state, _player);
            ApplyEnabled();
            UpdateStatus(state);
        }

        /// <summary>
        /// Escape closes the Advanced build page before anything else consumes
        /// it. True if the page was open and has now been closed.
        /// </summary>
        public bool CloseAdvancedPage()
        {
            if (_model.Page == CardPage.Actions)
                return false;
            _model.Page = CardPage.Actions;
            _built = false;
            return true;
        }

        /// <summary>
        /// A WC2 shortcut letter arrived. True if the live card claimed it —
        /// the letters are per-command, so the same key does nothing at all on
        /// a card that has no button for it.
        /// </summary>
        public bool ActivateHotkey(char key)
        {
            int slot = _model.FindHotkey(key);
            if (slot < 0)
                return false;
            Activate(slot);
            return true;
        }

        /// <summary>
        /// Escape drives the card's Cancel / Back button, as in the original.
        /// True if there was one to press. Called after the pending-order and
        /// build-page checks, both of which outrank it.
        /// </summary>
        public bool ActivateEscape()
        {
            int slot = _model.FindEscapeSlot();
            if (slot < 0)
                return false;
            Activate(slot);
            return true;
        }

        /// <summary>Called by a click, a shortcut letter, or Escape.</summary>
        public void Activate(int slot)
        {
            if ((uint)slot >= CommandCardModel.SlotCount)
                return;
            ref var s = ref _model.Slots[slot];
            switch (s.Kind)
            {
                case CommandSlotKind.None:
                    return;

                case CommandSlotKind.BuildBasicMenu:
                    SetPage(CardPage.BuildBasic);
                    return;
                case CommandSlotKind.BuildAdvancedMenu:
                    SetPage(CardPage.BuildAdvanced);
                    return;
                case CommandSlotKind.BackToActions:
                    SetPage(CardPage.Actions);
                    return;

                case CommandSlotKind.Build:
                    // Hands off to the placement ghost + world click.
                    _ui.BeginOrder(PendingOrderKind.Build, s.Param);
                    return;

                // Targeted actions: arm the order and wait for a world click.
                case CommandSlotKind.Move:
                    _ui.BeginOrder(PendingOrderKind.Move);
                    return;
                case CommandSlotKind.Attack:
                    _ui.BeginOrder(PendingOrderKind.Attack);
                    return;
                case CommandSlotKind.Patrol:
                    _ui.BeginOrder(PendingOrderKind.Patrol);
                    return;
                case CommandSlotKind.Harvest:
                    _ui.BeginOrder(PendingOrderKind.Harvest);
                    return;
                case CommandSlotKind.Repair:
                    _ui.BeginOrder(PendingOrderKind.Repair);
                    return;
                case CommandSlotKind.Unload:
                    _ui.BeginOrder(PendingOrderKind.Unload);
                    return;

                // Stop needs no target.
                case CommandSlotKind.Stop:
                    _ui.ClearPendingOrder();
                    SubmitSelection(CommandOp.Stop);
                    return;

                case CommandSlotKind.Train:
                case CommandSlotKind.UpgradeTo:
                    Submit(CommandOp.Train, s.Param, s.BuildingSlot);
                    return;

                case CommandSlotKind.Research:
                    Submit(CommandOp.Research, s.Param, s.BuildingSlot);
                    return;

                case CommandSlotKind.Cancel:
                    Submit(CommandOp.Cancel, 0, s.BuildingSlot);
                    return;
            }
        }

        void SetPage(CardPage page)
        {
            _model.Page = page;
            _built = false; // force a rebuild next tick
        }

        /// <summary>Order applying to the whole selection (Stop).</summary>
        unsafe void SubmitSelection(CommandOp op)
        {
            var cmd = new GameCommand { Op = op, Player = _player };
            foreach (uint packed in _ui.Selection)
            {
                if (cmd.SelectionCount >= GameCommand.MaxSelection)
                    break;
                cmd.Selection.Ids[cmd.SelectionCount++] = packed;
            }
            if (cmd.SelectionCount > 0)
                _host.SubmitCommand(cmd);
        }

        /// <summary>Single-building command, same shape as the old HudController.Submit.</summary>
        unsafe void Submit(CommandOp op, ushort param, int buildingSlot)
        {
            if (buildingSlot < 0)
                return;
            var state = _host.Sim.State;
            var cmd = new GameCommand
            {
                Op = op,
                Player = _player,
                Param = param,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] =
                new UnitId((ushort)buildingSlot, state.Units[buildingSlot].Gen).Packed;
            _host.SubmitCommand(cmd);
        }

        /// <summary>Structure changed: rewrite text and visibility once.</summary>
        void Render()
        {
            for (int i = 0; i < CommandCardModel.SlotCount; i++)
            {
                ref var s = ref _model.Slots[i];
                bool empty = s.Kind == CommandSlotKind.None;
                _buttons[i].EnableInClassList("command-button--empty", empty);
                _lastKind[i] = s.Kind;
                if (_keys[i] != null)
                    _keys[i].text = empty
                        ? string.Empty
                        : CommandHotkeys.LabelFor(s.Kind, s.Hotkey);
                if (empty)
                    continue;

                ApplyIcon(_icons[i], ref s);
                _buttons[i].tooltip = s.Label;
            }
        }

        /// <summary>
        /// Paint the slot's art, or leave the name as text when there is none.
        ///
        /// The icon is derived here rather than stored on CommandSlot because it
        /// is a pure function of (Kind, Param) — both already part of
        /// ComputeStructureHash, so a changed icon can never be missed and the
        /// model stays presentation-free.
        ///
        /// The label survives either way: with art it becomes the accessible
        /// name behind the image, and without it, it is the button.
        /// </summary>
        void ApplyIcon(Label icon, ref CommandSlot s)
        {
            if (icon == null)
                return;

            var sprite = _iconProvider?.Get(IconIndex(ref s));
            icon.style.backgroundImage = sprite == null
                ? new StyleBackground(StyleKeyword.Null)
                : new StyleBackground(sprite);
            icon.text = sprite == null ? s.Label : string.Empty;
        }

        int IconIndex(ref CommandSlot s)
        {
            switch (s.Kind)
            {
                case CommandSlotKind.Build:
                case CommandSlotKind.Train:
                case CommandSlotKind.UpgradeTo:
                    return UnitIconTable.IconFor((UnitTypeId)s.Param);
                case CommandSlotKind.Research:
                    // Upgrades carry their icon in the data (UGRD offset 364),
                    // indexing the same bank as the table.
                    var rules = _host?.Sim?.State.Rules;
                    return rules == null ? UnitIconTable.None : rules.Upgrades[s.Param].Icon;
                default:
                    // Orders (Move/Stop/Attack/...) have their own art, and the
                    // original drew a human and an orc version of most of them.
                    return UnitIconTable.IconFor(s.Kind, LocalRace);
            }
        }

        Race LocalRace
        {
            get
            {
                var state = _host?.Sim?.State;
                return state != null && _player < SimConstants.MaxPlayers
                    ? state.Players[_player].Race
                    : Race.Human;
            }
        }

        IIconProvider _iconProvider;

        /// <summary>Injected once assets resolve; null keeps the text buttons.</summary>
        public void SetIconProvider(IIconProvider provider)
        {
            _iconProvider = provider;
            Render();
        }

        /// <summary>"Footman  60 gold" — the hovered button's name and price.</summary>
        static string HoverText(ref CommandSlot s)
        {
            string t = s.Label ?? string.Empty;
            if (s.Gold > 0) t += "  " + s.Gold + " gold";
            if (s.Lumber > 0) t += "  " + s.Lumber + " lumber";
            if (s.Oil > 0) t += "  " + s.Oil + " oil";
            return t;
        }

        void ApplyEnabled()
        {
            for (int i = 0; i < CommandCardModel.SlotCount; i++)
            {
                if (_lastKind[i] == CommandSlotKind.None)
                    continue;
                bool enabled = _model.Slots[i].Enabled;
                if (enabled == _lastEnabled[i])
                    continue;
                _lastEnabled[i] = enabled;
                _buttons[i].EnableInClassList("command-button--disabled", !enabled);
            }
        }

        string _lastStatus = string.Empty;
        int _hoverSlot = -1;

        void UpdateStatus(GameState state)
        {
            // An armed order takes the line: without a cursor change it is the
            // only feedback that the next click will be consumed as a target.
            if (_ui.HasPendingOrder)
            {
                string prompt = _ui.PendingOrder switch
                {
                    PendingOrderKind.Build => "Select a build site",
                    PendingOrderKind.Move => "Select a destination",
                    PendingOrderKind.Attack => "Select a target",
                    PendingOrderKind.Patrol => "Select a patrol point",
                    PendingOrderKind.Harvest => "Select a mine or forest",
                    PendingOrderKind.Repair => "Select a building to repair",
                    PendingOrderKind.Unload => "Select a shore to unload at",
                    _ => string.Empty,
                };
                SetStatus(prompt);
                return;
            }

            // Hovering a live button: name and price, as the original did.
            if ((uint)_hoverSlot < CommandCardModel.SlotCount
                && _model.Slots[_hoverSlot].Kind != CommandSlotKind.None)
            {
                SetStatus(HoverText(ref _model.Slots[_hoverSlot]));
                return;
            }

            string text = string.Empty;
            for (int i = 0; i < CommandCardModel.SlotCount; i++)
            {
                if (_model.Slots[i].Kind != CommandSlotKind.Cancel)
                    continue;
                int b = _model.Slots[i].BuildingSlot;
                if (b < 0)
                    break;
                ref var bld = ref state.Units[b];
                if ((bld.Flags & UnitFlags.UnderConstruction) != 0)
                    text = "Constructing...";
                else if (bld.ResearchId != 0)
                    text = "Researching " + UnitNames.Of((UpgradeId)(bld.ResearchId - 1));
                else if (bld.BuildType != 0)
                    text = "Training " + UnitNames.Of((UnitTypeId)(bld.BuildType - 1));
                break;
            }
            SetStatus(text);
        }

        void SetStatus(string text)
        {
            if (text == _lastStatus)
                return;
            _lastStatus = text;
            _status.text = text;
        }
    }
}
