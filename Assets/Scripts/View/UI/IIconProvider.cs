using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// Resolves an icon index to a sprite. Declared in View and implemented in
    /// App, the same seam shape as IUnitSpriteProvider and IStringTable — View
    /// must not reference Import, and the atlas lives on the asset side.
    /// </summary>
    public interface IIconProvider
    {
        /// <summary>Null for an unknown index; callers fall back to the initials box.</summary>
        Sprite Get(int index);
    }
}
