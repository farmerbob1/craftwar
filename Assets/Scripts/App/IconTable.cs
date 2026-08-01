using System;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Every HUD icon sliced out of the installation's Portrait-face atlas,
    /// keyed by its TexturePacker frame name (era prefix + index, e.g.
    /// "forest_12"). Read at runtime by <see cref="BakedIconAtlas"/> —
    /// replaces <c>IconAtlas</c>'s live PNG+JSON slicing.
    /// </summary>
    [CreateAssetMenu(menuName = "Craftwar/Baked/Icon Table")]
    public sealed class IconTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            public Sprite sprite;
        }

        public Entry[] entries = Array.Empty<Entry>();
    }
}
