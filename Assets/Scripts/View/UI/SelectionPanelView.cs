using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// The sidebar's middle slot, laid out like the original: portrait and HP
    /// on the left, name beside it, then a right-aligned stat block. Three
    /// modes driven by selection content — a single unit's stat block, a single
    /// building's production readout, or a tile grid for a multi-unit
    /// selection. Portraits are initials boxes until the WC2 art lands at M8.
    /// </summary>
    public sealed class SelectionPanelView
    {
        /// <summary>Fixed stat rows, created once and shown/hidden per unit.</summary>
        enum Stat { Armor = 0, Damage, Range, Sight, Speed, Count }

        static readonly string[] StatLabels = { "Armor:", "Damage:", "Range:", "Sight:", "Speed:" };

        readonly UIState _ui;

        readonly VisualElement _single;
        readonly Label _portraitInitials, _name, _level, _hpText, _manaText;
        readonly VisualElement _hpFill, _portrait;
        readonly VisualElement _manaBar, _manaFill;
        IIconProvider _icons;

        readonly VisualElement[] _statRows = new VisualElement[(int)Stat.Count];
        readonly Label[] _statValues = new Label[(int)Stat.Count];
        readonly string[] _lastStatValue = new string[(int)Stat.Count];
        readonly bool[] _statShown = new bool[(int)Stat.Count];

        readonly Label _progressLabel;
        readonly VisualElement _progressBar, _progressFill;

        readonly VisualElement _grid;
        readonly UnitTileView[] _tiles = new UnitTileView[GameCommand.MaxSelection];

        string _lastName, _lastLevel, _lastHpText, _lastProgress, _lastManaText;
        ushort _lastPortraitType = ushort.MaxValue;
        bool _levelShown = true;
        bool _manaShown = true; // seeded true, same reasoning as _singleShown below
        int _lastHpPercent = -1;
        HpBand _lastBand = HpBand.None;
        int _lastManaPercent = -1;
        int _lastProgressPercent = -1;
        // Seeded true so the constructor's initial hide is not swallowed by the
        // no-op guards below.
        bool _singleShown = true, _gridShown = true, _progressShown = true;

        public SelectionPanelView(VisualElement hudRoot, UIState ui, UIAssetCatalog assets)
        {
            _ui = ui;
            var panel = hudRoot.Q("selection-panel");

            _single = Column("selection-single");

            // --- header: portrait + HP on the left, name on the right ---
            var header = Row("sel-header");
            var portraitCol = Column("sel-portrait-col");

            _portrait = new VisualElement { pickingMode = PickingMode.Ignore };
            _portrait.AddToClassList("sel-portrait");
            _portraitInitials = AddLabel(_portrait, "sel-portrait__initials");
            portraitCol.Add(_portrait);

            var hpBar = new VisualElement { pickingMode = PickingMode.Ignore };
            hpBar.AddToClassList("bar");
            hpBar.AddToClassList("sel-hp-bar");
            _hpFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _hpFill.AddToClassList("bar__fill");
            hpBar.Add(_hpFill);
            portraitCol.Add(hpBar);

            _hpText = AddLabel(portraitCol, "sel-hp-text");

            // Mana bar: only shown for CanCast units — hidden by default,
            // toggled per-selection in RenderSingle.
            _manaBar = new VisualElement { pickingMode = PickingMode.Ignore };
            _manaBar.AddToClassList("bar");
            _manaBar.AddToClassList("sel-mana-bar");
            _manaFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _manaFill.AddToClassList("bar__fill");
            _manaFill.AddToClassList("bar__fill--mana");
            _manaBar.Add(_manaFill);
            portraitCol.Add(_manaBar);
            _manaText = AddLabel(portraitCol, "sel-mana-text");
            header.Add(portraitCol);

            var info = Column("sel-info");
            _name = AddLabel(info, "sel-name");
            _level = AddLabel(info, "sel-level");
            header.Add(info);
            _single.Add(header);

            // --- stat block ---
            var stats = Column("sel-stats");
            for (int i = 0; i < (int)Stat.Count; i++)
            {
                var row = Row("sel-stat");
                var label = AddLabel(row, "sel-stat__label");
                label.text = StatLabels[i];
                _statValues[i] = AddLabel(row, "sel-stat__value");
                stats.Add(row);
                _statRows[i] = row;
                row.AddToClassList("selection__hidden");
                _statShown[i] = false;
            }
            _single.Add(stats);

            // --- production progress (buildings) ---
            _progressLabel = AddLabel(_single, "selection__progress-label");
            _progressBar = new VisualElement { pickingMode = PickingMode.Ignore };
            _progressBar.AddToClassList("bar");
            _progressFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _progressFill.AddToClassList("bar__fill");
            _progressBar.Add(_progressFill);
            _single.Add(_progressBar);

            panel.Add(_single);

            // --- multi-unit tile grid ---
            _grid = Column("selection-grid");
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
            }

            SetSingleVisible(false);
            SetGridVisible(false);
        }

        /// <summary>
        /// Injected once the atlas resolves; null keeps the initials boxes. The
        /// panel is built long before the installation's art is decoded, so this
        /// is a hand-over rather than a constructor argument.
        /// </summary>
        public void SetIconProvider(IIconProvider icons)
        {
            _icons = icons;
            _lastPortraitType = ushort.MaxValue; // force a repaint
            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i].SetIconProvider(icons);
        }

        static VisualElement Column(string cls)
        {
            var e = new VisualElement { name = cls, pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            return e;
        }

        /// <summary>Same as Column; the row direction comes from the class in USS
        /// so a theme can restyle it without touching inline styles.</summary>
        static VisualElement Row(string cls) => Column(cls);

        static Label AddLabel(VisualElement parent, string cls)
        {
            var l = new Label { text = string.Empty, pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
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
            var type = (UnitTypeId)u.TypeId;

            // Portrait art, or the initials box where the type has no icon.
            // Keyed on the type so this costs nothing while the same unit stays
            // selected.
            if (u.TypeId != _lastPortraitType)
            {
                _lastPortraitType = u.TypeId;
                UnitPortrait.Apply(_portrait, _portraitInitials, _icons, type);
            }

            string name = UnitNames.Of(type);
            if (name != _lastName)
            {
                _lastName = name;
                _name.text = name;
            }

            // Buildings have no upgrade lines, so no level.
            bool showLevel = (u.Flags & UnitFlags.Building) == 0;
            if (showLevel != _levelShown)
            {
                _levelShown = showLevel;
                _level.EnableInClassList("selection__hidden", !showLevel);
            }
            if (showLevel)
            {
                string levelText = "Level " + sim.UnitLevel(ref u);
                if (levelText != _lastLevel)
                {
                    _lastLevel = levelText;
                    _level.text = levelText;
                }
            }

            string hpText = u.Hp + "/" + row.Hp;
            if (hpText != _lastHpText)
            {
                _lastHpText = hpText;
                _hpText.text = hpText;
            }
            HpBarUtil.Apply(_hpFill, u.Hp, row.Hp, ref _lastHpPercent, ref _lastBand);

            bool showMana = row.Is(UnitTypeFlags.CanCast);
            if (showMana != _manaShown)
            {
                _manaShown = showMana;
                _manaBar.EnableInClassList("selection__hidden", !showMana);
                _manaText.EnableInClassList("selection__hidden", !showMana);
            }
            if (showMana)
            {
                string manaText = u.Mana + "/" + SimConstants.MaxMana;
                if (manaText != _lastManaText)
                {
                    _lastManaText = manaText;
                    _manaText.text = manaText;
                }
                // No colour banding for mana — it's a resource, not a health
                // warning; the fill's flat blue comes from bar__fill--mana.
                int manaPercent = u.Mana * 100 / SimConstants.MaxMana;
                if (manaPercent != _lastManaPercent)
                {
                    _lastManaPercent = manaPercent;
                    _manaFill.style.width = Length.Percent(manaPercent);
                }
            }

            bool isBuilding = (u.Flags & UnitFlags.Building) != 0;
            bool canAttack = row.Is(UnitTypeFlags.CanAttack);
            int speed = UnitSpeeds.Get(u.TypeId);
            // A neutral mine/patch has combat stats in the data, but showing
            // "Armor: 20" for a gold mine is noise — the resources line is the
            // only stat that matters, so drop the block for resource nodes.
            bool isResource = row.Is(UnitTypeFlags.GoldMine | UnitTypeFlags.OilPatch);

            SetStat(Stat.Armor, !isResource, WithBonus(row.Armor, sim.EffectiveArmor(ref u)));
            SetStat(Stat.Damage, !isResource && canAttack, DamageText(sim, ref u, ref row));
            SetStat(Stat.Range, !isResource && canAttack, WithBonus(row.AttackRange, sim.EffectiveRange(ref u)));
            SetStat(Stat.Sight, !isResource, WithBonus(row.Sight, sim.EffectiveSight(ref u)));
            SetStat(Stat.Speed, !isResource && speed > 0, speed.ToString());

            RenderProgress(state, ref u, ref row, isBuilding);
        }

        /// <summary>"2" when unupgraded, "2+4" when upgrades are in play.</summary>
        static string WithBonus(int baseValue, int effective)
        {
            int bonus = effective - baseValue;
            return bonus > 0 ? baseValue + "+" + bonus : baseValue.ToString();
        }

        /// <summary>
        /// The damage roll is 50-100% of (strength - armor + pierce), so the
        /// honest spread against an unarmoured target is [half, 2*half]. That is
        /// this sim's formula rather than the original's display convention, so
        /// the numbers will not always match a WC2 screenshot.
        /// </summary>
        static string DamageText(GameSim sim, ref Unit u, ref UnitTypeData row)
        {
            int baseDamage = row.BasicDamage + row.PiercingDamage;
            int effective = sim.EffectiveStrength(ref u) + sim.EffectivePierce(ref u);
            int half = (baseDamage + 1) / 2;
            string span = half + "-" + (half * 2);
            int bonus = effective - baseDamage;
            return bonus > 0 ? span + "+" + bonus : span;
        }

        void SetStat(Stat stat, bool visible, string value)
        {
            int i = (int)stat;
            if (visible != _statShown[i])
            {
                _statShown[i] = visible;
                _statRows[i].EnableInClassList("selection__hidden", !visible);
            }
            if (!visible || value == _lastStatValue[i])
                return;
            _lastStatValue[i] = value;
            _statValues[i].text = value;
        }

        /// <summary>
        /// TrainTicks is a countdown, so progress runs 1 - remaining/total.
        /// Total comes from the sim's own BuildTicksFor so the bar cannot drift
        /// from the rule that actually drives the countdown.
        /// </summary>
        void RenderProgress(GameState state, ref Unit u, ref UnitTypeData row, bool isBuilding)
        {
            string label = null;
            int total = 0;

            // Neutral gold mines and oil patches report what is left rather than
            // any production, and update live as harvesters draw them down.
            if (state.Rules != null
                && state.Rules.Units[u.TypeId].Is(UnitTypeFlags.GoldMine | UnitTypeFlags.OilPatch))
            {
                bool oil = state.Rules.Units[u.TypeId].Is(UnitTypeFlags.OilPatch);
                label = (oil ? "Oil: " : "Gold: ") + u.ResourceAmount;
            }
            else if (isBuilding)
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
            else if (u.Carry != CarryType.None)
            {
                label = "Carrying " + u.Carry;
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
