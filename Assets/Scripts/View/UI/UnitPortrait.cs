using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Paints a unit's HUD art onto a box: the icon where the installation
    /// provides one, and the initials box where it does not (no atlas at all, or
    /// a type with no entry in <see cref="UnitIconTable"/>). Shared by the
    /// selection panel's portrait and the multi-selection tiles so the two
    /// cannot disagree about which art a unit has.
    /// </summary>
    public static class UnitPortrait
    {
        /// <summary>
        /// <paramref name="box"/> takes the art as a background image;
        /// <paramref name="label"/> holds the initials fallback and is blanked
        /// when there is art to show.
        /// </summary>
        public static void Apply(VisualElement box, Label label,
            IIconProvider icons, UnitTypeId type)
        {
            var sprite = icons?.Get(UnitIconTable.IconFor(type));
            if (box != null)
                box.style.backgroundImage = sprite == null
                    ? new StyleBackground(StyleKeyword.Null)
                    : new StyleBackground(sprite);
            if (label != null)
                label.text = sprite != null ? string.Empty : UnitNames.InitialsOf(type);
        }
    }
}
