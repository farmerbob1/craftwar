using System.Collections.Generic;
using Craftwar.Import;
using Craftwar.Sim;
using Craftwar.View;

namespace Craftwar.App
{
    /// <summary>
    /// The game's own localized names, from Strings/&lt;locale&gt;.json.
    ///
    /// The install's key space lines up with ours exactly: "unit_&lt;typeId&gt;"
    /// is indexed by the same PUD type id the sim uses, verified across the
    /// whole roster. So this replaces UnitNames' reflection-derived spellings
    /// ("ElvenLumberMill" → "Elven Lumber Mill") with the real strings, and
    /// brings ten languages along for free.
    ///
    /// Lives in App because it implements a View interface over an Import
    /// source, and those two are sibling assemblies that cannot see each other.
    /// Wc2SoundCatalog and UnitSpriteBank sit here for the same reason.
    /// </summary>
    public sealed class Wc2StringTable : IStringTable
    {
        readonly Dictionary<string, string> _strings;

        public int Count => _strings.Count;

        Wc2StringTable(Dictionary<string, string> strings) => _strings = strings;

        /// <summary>
        /// Load one locale, or null if it is absent or malformed. Callers fall
        /// back to <see cref="UnitNames"/>' reflection path, so a missing string
        /// table costs fidelity rather than blocking play.
        /// </summary>
        public static Wc2StringTable Load(IAssetSource source, string locale = "enUS")
        {
            if (source == null)
                return null;
            string path = $"strings/{locale.ToLowerInvariant()}.json";
            if (!source.TryRead(path, out var bytes))
                return null;
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(bytes);
                var map = JsonValue.Parse(json).ToStringMap();
                return map.Count == 0 ? null : new Wc2StringTable(map);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public string UnitName(UnitTypeId type) => Get("unit_" + (int)type);

        public string Get(string key) =>
            key != null && _strings.TryGetValue(key, out var value) ? value : null;
    }
}
