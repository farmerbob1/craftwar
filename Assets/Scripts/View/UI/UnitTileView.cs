using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// One tile in a multi-unit selection: initials box plus a mini HP bar.
    /// Pooled — created once and shown/hidden, never re-instantiated.
    /// </summary>
    public sealed class UnitTileView
    {
        public VisualElement Root { get; }

        readonly Label _initials;
        readonly VisualElement _hpFill;

        ushort _lastType = ushort.MaxValue;
        int _lastPercent = -1;
        HpBand _lastBand = HpBand.None;

        // Tracked separately from Packed: a tile starts hidden AND unassigned,
        // so keying the class toggle off Packed would skip the initial hide and
        // leave the whole pool on screen.
        bool _hidden;

        /// <summary>The unit this tile currently shows; 0 when hidden.</summary>
        public uint Packed { get; private set; }

        public UnitTileView(VisualElement root)
        {
            Root = root;
            _initials = root.Q<Label>("initials");
            _hpFill = root.Q("hp-fill");
            Root.AddToClassList("unit-tile--empty");
            _hidden = true;
        }

        public void Hide()
        {
            Packed = 0;
            if (_hidden)
                return;
            _hidden = true;
            Root.AddToClassList("unit-tile--empty");
        }

        public void Show(uint packed, ref Unit u, ref UnitTypeData row)
        {
            if (_hidden)
            {
                _hidden = false;
                Root.RemoveFromClassList("unit-tile--empty");
            }
            Packed = packed;

            if (u.TypeId != _lastType)
            {
                _lastType = u.TypeId;
                _initials.text = UnitNames.InitialsOf((UnitTypeId)u.TypeId);
                Root.tooltip = UnitNames.Of((UnitTypeId)u.TypeId);
            }
            HpBarUtil.Apply(_hpFill, u.Hp, row.Hp, ref _lastPercent, ref _lastBand);
        }
    }

    public enum HpBand : byte { None = 0, High, Mid, Low }

    /// <summary>
    /// Shared HP-bar painting: width as a percentage plus a color band, both
    /// written only when they actually change.
    /// </summary>
    public static class HpBarUtil
    {
        public static void Apply(VisualElement fill, int hp, int maxHp,
            ref int lastPercent, ref HpBand lastBand)
        {
            if (fill == null)
                return;
            if (maxHp <= 0)
                maxHp = 1;
            if (hp < 0)
                hp = 0;
            int percent = hp * 100 / maxHp;
            if (percent != lastPercent)
            {
                lastPercent = percent;
                fill.style.width = Length.Percent(percent);
            }

            var band = percent > 50 ? HpBand.High : percent > 25 ? HpBand.Mid : HpBand.Low;
            if (band == lastBand)
                return;
            lastBand = band;
            fill.EnableInClassList("bar__fill--mid", band == HpBand.Mid);
            fill.EnableInClassList("bar__fill--low", band == HpBand.Low);
        }
    }
}
