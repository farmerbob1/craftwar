using System;
using System.Collections.Generic;
using System.IO;

namespace Craftwar.Import
{
    /// <summary>A candidate installation, with enough detail for the import UI to explain itself.</summary>
    public struct InstallCandidate
    {
        /// <summary>The Data folder itself, e.g. "...\Warcraft II Remastered\x86\Data".</summary>
        public string DataRoot;

        /// <summary>Where the guess came from, shown to the player.</summary>
        public string Origin;

        /// <summary>0-100. Complete installs rank above partial ones.</summary>
        public int Confidence;

        /// <summary>Which required asset classes were found; drives the UI's checklist.</summary>
        public bool HasTilesets, HasSprites, HasSounds, HasStrings, HasIcons;

        public bool IsUsable => HasTilesets && HasSprites;
    }

    /// <summary>
    /// Finds a Warcraft II installation so the player does not have to type a
    /// path. Returns ranked candidates rather than one answer: several installs
    /// can coexist (Remastered, a GOG copy, an old CD install), and the import
    /// screen should show the choice rather than silently picking.
    ///
    /// Validation probes for one representative file per asset class instead of
    /// trusting the folder name, so a half-copied or wrong directory is caught
    /// here rather than as a decode failure ten screens later.
    /// </summary>
    public static class Wc2InstallLocator
    {
        // Representative probes, one per class. Lowercase logical paths.
        public const string ProbeTileset = "art/bgs/forest/forest.ppl";
        public const string ProbeSounds = "gamesfx.lst";
        public const string ProbeStrings = "strings/enus.json";
        public const string ProbeIcons = "art/classic/hud/portrait-face.png";

        /// <summary>
        /// Sprites are probed by directory rather than by a named file. The two
        /// race folders hold *the same generic filenames* over different art —
        /// Human/grunt.grp is the footman, Human/peon.grp the peasant — so any
        /// single filename here is a guess waiting to be wrong. Asking "does
        /// art/unit contain .grp files" is both simpler and more honest.
        /// </summary>
        public const string ProbeSpriteDir = "art/unit";

        /// <summary>Standard install locations, most likely first.</summary>
        public static IEnumerable<(string path, string origin)> KnownRoots()
        {
            foreach (var pf in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("ProgramFiles"),
            })
            {
                if (string.IsNullOrEmpty(pf))
                    continue;
                yield return (Path.Combine(pf, "Warcraft II Remastered", "x86", "Data"), "Warcraft II Remastered");
                yield return (Path.Combine(pf, "GOG Galaxy", "Games", "Warcraft II BNE", "Data"), "GOG Galaxy");
                yield return (Path.Combine(pf, "Steam", "steamapps", "common", "Warcraft II BNE", "Data"), "Steam");
            }
        }

        /// <summary>
        /// Rank every known location plus any extra paths the player supplied.
        /// Unusable candidates are still returned, so the UI can say *why* a
        /// folder was rejected rather than just refusing it.
        /// </summary>
        public static List<InstallCandidate> Find(params string[] extraRoots)
        {
            var results = new List<InstallCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (extraRoots != null)
                foreach (var root in extraRoots)
                    TryAdd(root, "chosen by you", results, seen);

            foreach (var (path, origin) in KnownRoots())
                TryAdd(path, origin, results, seen);

            results.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            return results;
        }

        static void TryAdd(string root, string origin, List<InstallCandidate> results, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(root))
                return;

            string full;
            try { full = Path.GetFullPath(root); }
            catch { return; }

            if (!seen.Add(full) || !Directory.Exists(full))
                return;

            var c = Inspect(full, origin);
            if (c.Confidence > 0)
                results.Add(c);
        }

        /// <summary>
        /// Score a folder. Accepts either the Data folder itself or the install
        /// root above it, since a player pointed at a folder picker will
        /// reasonably choose either.
        /// </summary>
        public static InstallCandidate Inspect(string root, string origin = "manual")
        {
            string dataRoot = ResolveDataRoot(root);
            var c = new InstallCandidate { DataRoot = dataRoot, Origin = origin };
            if (dataRoot == null)
                return c;

            c.HasTilesets = FileExists(dataRoot, ProbeTileset);
            c.HasSprites = DirHasFiles(dataRoot, ProbeSpriteDir, "*.grp");
            c.HasSounds = FileExists(dataRoot, ProbeSounds);
            c.HasStrings = FileExists(dataRoot, ProbeStrings);
            c.HasIcons = FileExists(dataRoot, ProbeIcons);

            // Tilesets and sprites are load-bearing; the rest degrade to
            // placeholder tones and enum-derived names without blocking play.
            int score = 0;
            if (c.HasTilesets) score += 30;
            if (c.HasSprites) score += 30;
            if (c.HasSounds) score += 15;
            if (c.HasIcons) score += 15;
            if (c.HasStrings) score += 10;
            c.Confidence = score;
            return c;
        }

        /// <summary>Accept ".../x86/Data", ".../x86", or the install root.</summary>
        static string ResolveDataRoot(string root)
        {
            foreach (var candidate in new[]
            {
                root,
                Path.Combine(root, "Data"),
                Path.Combine(root, "x86", "Data"),
            })
            {
                if (Directory.Exists(candidate) && FileExists(candidate, ProbeTileset))
                    return candidate;
            }
            // No tileset anywhere: still report the most plausible spelling so
            // the UI can show which probes failed.
            return Directory.Exists(root) ? root : null;
        }

        /// <summary>
        /// Case-insensitive existence check. The probes are written lowercase but
        /// the install ships mixed case, and only NTFS forgives that.
        /// </summary>
        static bool FileExists(string dataRoot, string logicalPath)
        {
            string direct = Path.Combine(dataRoot, logicalPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(direct))
                return true;

            // Walk it segment by segment, matching case-insensitively.
            string current = dataRoot;
            var parts = logicalPath.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                string match = null;
                try
                {
                    foreach (var entry in last ? Directory.GetFiles(current) : Directory.GetDirectories(current))
                    {
                        if (string.Equals(Path.GetFileName(entry), parts[i], StringComparison.OrdinalIgnoreCase))
                        {
                            match = entry;
                            break;
                        }
                    }
                }
                catch { return false; }

                if (match == null)
                    return false;
                current = match;
            }
            return true;
        }

        /// <summary>Does a logical directory contain any matching file, at any depth?</summary>
        static bool DirHasFiles(string dataRoot, string logicalDir, string pattern)
        {
            string dir = ResolveDir(dataRoot, logicalDir);
            if (dir == null)
                return false;
            try
            {
                // Stop at the first hit; art/unit is a large tree.
                using (var e = Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).GetEnumerator())
                    return e.MoveNext();
            }
            catch { return false; }
        }

        /// <summary>Walk a logical directory path, matching each segment case-insensitively.</summary>
        static string ResolveDir(string dataRoot, string logicalDir)
        {
            string current = dataRoot;
            foreach (var part in logicalDir.Split('/'))
            {
                string match = null;
                try
                {
                    foreach (var sub in Directory.GetDirectories(current))
                    {
                        if (string.Equals(Path.GetFileName(sub), part, StringComparison.OrdinalIgnoreCase))
                        {
                            match = sub;
                            break;
                        }
                    }
                }
                catch { return null; }
                if (match == null)
                    return null;
                current = match;
            }
            return current;
        }

        /// <summary>Map folders to offer, most canonical first. The Remastered layout
        /// splits these three ways, and only x86\Maps is what mapsDir has pointed at.</summary>
        public static List<string> MapFolders(string dataRoot)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(dataRoot))
                return found;

            string x86 = Path.GetDirectoryName(dataRoot); // ...\x86
            foreach (var candidate in new[]
            {
                x86 == null ? null : Path.Combine(x86, "Maps"),
                Path.Combine(dataRoot, "Maps"),
                Path.Combine(dataRoot, "OrigMaps"),
            })
            {
                if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                    found.Add(candidate);
            }
            return found;
        }
    }
}
