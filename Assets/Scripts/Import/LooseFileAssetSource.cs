using System;
using System.Collections.Generic;
using System.IO;

namespace Craftwar.Import
{
    /// <summary>
    /// Reads a Warcraft II installation's loose Data folder — the primary
    /// source, since everything the game needs ships uncompressed and named
    /// there: Art/unit/**.grp, Art/bgs/&lt;era&gt;/*.{ppl,vr4,vx4,cv4},
    /// Art/classic/HUD/*.png, Gamesfx/**.wav, Music/*.wav, Strings/*.json.
    ///
    /// Builds a logical→real index once at construction. That indirection is not
    /// decoration: on disk the names are mixed case ("Gamesfx/Human/Hready.wav")
    /// while every call site wants to write a stable lowercase path, and NTFS
    /// would forgive the mismatch while a case-sensitive filesystem would not.
    /// Doing it once also means no per-read directory probing.
    /// </summary>
    public sealed class LooseFileAssetSource : IAssetSource
    {
        readonly string _root;
        readonly Dictionary<string, string> _index; // logical -> full path
        readonly List<string> _sorted;              // logical, ordered, for List()

        public string Root => _root;
        public int Count => _index.Count;

        public LooseFileAssetSource(string dataRoot)
        {
            _root = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
            _index = new Dictionary<string, string>(StringComparer.Ordinal);

            if (Directory.Exists(_root))
            {
                foreach (var full in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                {
                    string logical = ToLogical(full);
                    // First writer wins; duplicates differing only by case are a
                    // packaging quirk, not something to fail the whole load over.
                    if (!_index.ContainsKey(logical))
                        _index[logical] = full;
                }
            }

            _sorted = new List<string>(_index.Keys);
            _sorted.Sort(StringComparer.Ordinal);
        }

        string ToLogical(string fullPath)
        {
            string rel = fullPath.Substring(_root.Length).TrimStart('\\', '/');
            return rel.Replace('\\', '/').ToLowerInvariant();
        }

        public bool TryRead(string logicalPath, out byte[] data)
        {
            data = null;
            if (string.IsNullOrEmpty(logicalPath))
                return false;
            if (!_index.TryGetValue(Normalize(logicalPath), out string full))
                return false;
            try
            {
                data = File.ReadAllBytes(full);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool Exists(string logicalPath) =>
            !string.IsNullOrEmpty(logicalPath) && _index.ContainsKey(Normalize(logicalPath));

        public IEnumerable<string> List(string prefix)
        {
            string p = prefix == null ? string.Empty : Normalize(prefix);
            // Sorted, so callers get a deterministic order without sorting again.
            foreach (var key in _sorted)
                if (key.StartsWith(p, StringComparison.Ordinal))
                    yield return key;
        }

        public string Describe() => $"loose install: {_root} ({_index.Count} files)";

        static string Normalize(string logicalPath) =>
            logicalPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
    }
}
