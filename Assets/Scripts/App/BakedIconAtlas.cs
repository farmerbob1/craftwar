using System.Collections.Generic;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Reads a pre-baked <see cref="IconTable"/> instead of slicing the
    /// installation's HUD atlas every session. Replaces <c>IconAtlas</c> — see
    /// <c>Craftwar/Setup/Import Warcraft II Assets</c>.
    /// </summary>
    public sealed class BakedIconAtlas : IIconProvider
    {
        readonly Dictionary<string, Sprite> _sprites;
        readonly string _prefix;

        public static string ResourcePath => "Icons/IconTable";

        /// <summary>Null when the icon table has not been baked yet.</summary>
        public static BakedIconAtlas Load(PudEra era)
        {
            var table = Resources.Load<IconTable>(ResourcePath);
            return table == null ? null : new BakedIconAtlas(table, era);
        }

        BakedIconAtlas(IconTable table, PudEra era)
        {
            _sprites = new Dictionary<string, Sprite>(table.entries.Length);
            foreach (var e in table.entries)
                if (e.sprite != null)
                    _sprites[e.name] = e.sprite;
            _prefix = PrefixFor(era);
        }

        /// <summary>Era prefixes follow the tileset naming, not the sprite naming
        /// (see <c>IconAtlas</c>'s original note): "ice" is Winter, "swamp" is Wasteland.</summary>
        static string PrefixFor(PudEra era) => era switch
        {
            PudEra.Winter => "ice",
            PudEra.Wasteland => "swamp",
            PudEra.Swamp => "xswamp",
            _ => "forest",
        };

        public Sprite Get(int index)
        {
            if (index < 0)
                return null;
            if (_sprites.TryGetValue($"{_prefix}_{index}", out var sprite))
                return sprite;
            // Not every era defines every icon; forest is the complete set.
            _sprites.TryGetValue($"forest_{index}", out sprite);
            return sprite;
        }
    }
}
