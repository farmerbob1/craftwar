using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// The sidebar's middle slot. Three modes driven by selection content:
    /// a single unit's stat block, a single building's production readout, or
    /// a grid of tiles for a multi-unit selection. Portraits are initials
    /// boxes until the WC2 art lands at M8.
    /// </summary>
    public sealed class SelectionPanelView
    {
        readonly UIState _ui;

        readonly VisualElement _single;
        readonly Label _name, _hpText, _stats, _progressLabel;
        readonly VisualElement _hpFill, _progressBar, _progressFill;

        readonly VisualElement _grid;
        readonly UnitTileView[] _tiles = new UnitTileView[GameCommand.MaxSelection];

        string _lastName = null, _lastHpText = null, _lastStats = null, _lastProgress = null;
        int _lastHpPercent = -1;
        HpBand _lastBand = HpBand.None;
        int _lastProgressPercent = -1;
        // Seeded true so the constructor's initial hide is not swallowed by the
        // no-op guard in SetSingleVisible/SetGridVisible.
        bool _singleShown = true, _gridShown = true, _progressShown = true;

        public SelectionPanelView(VisualElement hudRoot, UIState ui, UIAssetCatalog assets)
        {
            _ui = ui;

            var panel = hudRoot.Q("selection-panel");

            _single = new VisualElement { name = "selection-single", pickingMode = PickingMode.Ignore };
            _name = AddLabel(_single, "selection__name");
            _hpText = AddLabel(_single, "selection__hp-text");

            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("bar");
            bar.AddToClassList("selection__bar");
            _hpFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _hpFill.AddToClassList("bar__fill");
            bar.Add(_hpFill);
            _single.Add(bar);

            _stats = AddLabel(_single, "selection__stat");
            _progressLabel = AddLabel(_single, "selection__progress-label");

            _progressBar = new VisualElement { pickingMode = PickingMode.Ignore };
            _progressBar.AddToClassList("bar");
            _progressFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _progressFill.AddToClassList("bar__fill");
            _progressBar.Add(_progressFill);
            _single.Add(_progressBar);

            panel.Add(_single);

            _grid = new VisualElement { name = "selection-grid", pickingMode = PickingMode.Ignore };
            _grid.AddToClassList("unit-grid");
            panel.Add(_grid);

            for (int i = 0; i < _tiles.Length; i++)
            {
                VisualElement root;
                if (assets.unitTile != null)
                {
                    var clone = assets.unitTile.Instantiate();
                    root = clone.Q(className: "unit-tile") ?? clone;
                    root.RemoveFromHierarchy();
                }
                else
                {
                    root = BuildFallbackTile();
                }
                _grid.Add(root);
                var tile = new UnitTileView(root);
                _tiles[i] = tile;
                root.RegisterCallback<ClickEvent>(_ =>
                {
                    if (tile.Packed != 0)
                        _ui.Selection.SetSingle(tile.Packed);
                });
                tile.Hide();
            }

            SetSingleVisible(false);
            SetGridVisible(false);
        }

        static VisualElement BuildFallbackTile()
        {
            var root = new VisualElement();
            root.AddToClassList("unit-tile");
            var initials = new Label { name = "initials", text = "?" };
            initials.AddToClassList("unit-tile__initials");
            initials.pickingMode = PickingMode.Ignore;
            var hp = new VisualElement { name = "hp", pickingMode = PickingMode.Ignore };
            hp.AddToClassList("unit-tile__hp");
            hp.AddToClassList("bar");
            var fill = new VisualElement { name = "hp-fill", pickingMode = PickingMode.Ignore };
            fill.AddToClassList("bar__fill");
            hp.Add(fill);
            root.Add(initials);
            root.Add(hp);
            return root;
        }

        static Label AddLabel(VisualElement parent, string cls)
        {
            var l = new Label { text = string.Empty, pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        public void Tick(GameSim sim)
        {
            var state = sim.State;
            var sel = _ui.Selection;

            if (sel.Count == 0)
            {
                SetSingleVisible(false);
                SetGridVisible(false);
                HideTilesFrom(0);
                return;
            }

            if (sel.Count == 1)
            {
                foreach (uint packed in sel)
                {
                    if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                        break;
                    SetGridVisible(false);
                    HideTilesFrom(0);
                    SetSingleVisible(true);
                    RenderSingle(sim, state, idx);
                    return;
                }
                // Stale id (unit died): treat as empty until selection catches up.
                SetSingleVisible(false);
                SetGridVisible(false);
                HideTilesFrom(0);
                return;
            }

            SetSingleVisible(false);
            SetGridVisible(true);
            int n = 0;
            foreach (uint packed in sel)
            {
                if (n >= _tiles.Length)
                    break;
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                ref var u = ref state.Units[idx];
                ref var row = ref state.Rules.Units[u.TypeId];
                _tiles[n++].Show(packed, ref u, ref row);
            }
            HideTilesFrom(n);
        }

        void HideTilesFrom(int start)
        {
            for (int i = start; i < _tiles.Length; i++)
                _tiles[i].Hide();
        }

        void RenderSingle(GameSim sim, GameState state, int idx)
        {
            ref var u = ref state.Units[idx];
            ref var row = ref state.Rules.Units[u.TypeId];

            string name = UnitNames.Of((UnitTypeId)u.TypeId);
            if (name != _lastName)
            {
                _lastName = name;
                _name.text = name;
            }

            string hpText = u.Hp + " / " + row.Hp;
            if (hpText != _lastHpText)
            {
                _lastHpText = hpText;
                _hpText.text = hpText;
            }
            HpBarUtil.Apply(_hpFill, u.Hp, row.Hp, ref _lastHpPercent, ref _lastBand);

            bool isBuilding = (u.Flags & UnitFlags.Building) != 0;
            string stats = isBuilding
                ? "Armor " + sim.EffectiveArmor(ref u) + "   Sight " + sim.EffectiveSight(ref u)
                : "Armor " + sim.EffectiveArmor(ref u)
                    + "   Dmg " + sim.EffectiveStrength(ref u) + "+" + sim.EffectivePierce(ref u)
                    + "   Rng " + sim.EffectiveRange(ref u)
                    + "   Sight " + sim.EffectiveSight(ref u)
                    + (u.Carry != CarryType.None ? "   Carrying " + u.Carry : string.Empty);
            if (stats != _lastStats)
            {
                _lastStats = stats;
                _stats.text = stats;
            }

            RenderProgress(state, ref u, ref row, isBuilding);
        }

        /// <summary>
        /// TrainTicks is a countdown, so progress runs 1 - remaining/total.
        /// Total comes from the sim's own BuildTicksFor so the bar can't drift
        /// from the rule that actually drives the countdown.
        /// </summary>
        void RenderProgress(GameState state, ref Unit u, ref UnitTypeData row, bool isBuilding)
        {
            string label = null;
            int total = 0;

            if (isBuilding)
            {
                if ((u.Flags & UnitFlags.UnderConstruction) != 0)
                {
                    label = "Constructing";
                    total = GameSim.BuildTicksFor(row.BuildTime);
                }
                else if (u.ResearchId != 0)
                {
                    var up = (UpgradeId)(u.ResearchId - 1);
                    label = "Researching " + UnitNames.Of(up);
                    total = GameSim.BuildTicksFor(state.Rules.Upgrades[(int)up].Time);
                }
                else if (u.BuildType != 0)
                {
                    var t = (UnitTypeId)(u.BuildType - 1);
                    label = "Training " + UnitNames.Of(t);
                    total = GameSim.BuildTicksFor(state.Rules.Units[(int)t].BuildTime);
                }
                else if (u.RallyX != 0 || u.RallyY != 0)
                {
                    label = "Rally " + u.RallyX + ", " + u.RallyY;
                }
            }

            if (label == null)
            {
                if (!_progressShown)
                    return;
                _progressShown = false;
                _lastProgress = null;
                _progressLabel.text = string.Empty;
                _progressLabel.AddToClassList("selection__hidden");
                _progressBar.AddToClassList("selection__hidden");
                return;
            }

            if (!_progressShown)
            {
                _progressShown = true;
                _progressLabel.RemoveFromClassList("selection__hidden");
            }
            if (label != _lastProgress)
            {
                _lastProgress = label;
                _progressLabel.text = label;
            }

            bool hasBar = total > 0;
            _progressBar.EnableInClassList("selection__hidden", !hasBar);
            if (!hasBar)
                return;

            int percent = 100 - u.TrainTicks * 100 / total;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            if (percent == _lastProgressPercent)
                return;
            _lastProgressPercent = percent;
            _progressFill.style.width = Length.Percent(percent);
        }

        void SetSingleVisible(bool visible)
        {
            if (visible == _singleShown)
                return;
            _singleShown = visible;
            _single.EnableInClassList("selection__hidden", !visible);
        }

        void SetGridVisible(bool visible)
        {
            if (visible == _gridShown)
                return;
            _gridShown = visible;
            _grid.EnableInClassList("selection__hidden", !visible);
        }
    }
}
