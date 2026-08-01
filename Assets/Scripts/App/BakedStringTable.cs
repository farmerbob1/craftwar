using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Reads a pre-baked <see cref="LocalizedStringTable"/> instead of parsing
    /// Strings/&lt;locale&gt;.json every session. Replaces <c>Wc2StringTable</c>
    /// — see <c>Craftwar/Setup/Import Warcraft II Assets</c>.
    /// </summary>
    public sealed class BakedStringTable : IStringTable
    {
        readonly Dictionary<string, string> _strings;

        public int Count => _strings.Count;

        public static string ResourcePath(string locale) => $"Strings/{locale}";

        /// <summary>Null when that locale has not been baked; callers fall back
        /// to UnitNames' reflection path, same as Wc2StringTable did.</summary>
        public static BakedStringTable Load(string locale = "enUS")
        {
            var table = Resources.Load<LocalizedStringTable>(ResourcePath(locale));
            return table == null ? null : new BakedStringTable(table);
        }

        BakedStringTable(LocalizedStringTable table)
        {
            _strings = new Dictionary<string, string>(table.entries.Length);
            foreach (var e in table.entries)
                _strings[e.key] = e.value;
        }

        public string UnitName(UnitTypeId type) => Get("unit_" + (int)type);

        public string Get(string key) =>
            key != null && _strings.TryGetValue(key, out var value) ? value : null;
    }
}
