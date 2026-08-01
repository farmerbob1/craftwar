using System;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// One locale's Strings/&lt;locale&gt;.json, pre-parsed at Editor time into
    /// plain key/value pairs so runtime never needs a JSON parser (the one
    /// that reads the source file lives in Craftwar.Import, which is
    /// Editor-only). Read by <see cref="BakedStringTable"/> — replaces
    /// <c>Wc2StringTable</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Craftwar/Baked/Localized String Table")]
    public sealed class LocalizedStringTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public string value;
        }

        public string locale = "enUS";
        public Entry[] entries = Array.Empty<Entry>();
    }
}
