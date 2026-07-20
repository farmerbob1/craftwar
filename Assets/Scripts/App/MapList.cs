using System;
using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>A selectable map: what to show, and what to put in MatchConfig.mapPath.</summary>
    public struct MapEntry
    {
        public string Label;   // file name without extension
        public string Value;   // bare name for StreamingAssets, else an absolute path
    }

    /// <summary>
    /// Finds playable .pud files for the match-setup screen.
    ///
    /// Deliberately mirrors GameBootstrapEditor's rules so the runtime picker and
    /// the inspector dropdown agree: sort case-insensitively so the order is the
    /// same on every machine, and store a bare file name for anything in
    /// StreamingAssets so the value stays portable, falling back to an absolute
    /// path only for maps that live in the player's own install.
    /// </summary>
    public static class MapList
    {
        public static List<MapEntry> Find(LocalAssetPaths paths)
        {
            var entries = new List<MapEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Dev maps shipped in StreamingAssets: store the bare name.
            AddFrom(GameBootstrap.StreamingMapsDir, bareName: true, entries, seen);

            // The player's own install: absolute, since it is machine-specific.
            if (paths != null && !string.IsNullOrEmpty(paths.mapsDir))
                AddFrom(paths.mapsDir, bareName: false, entries, seen);

            entries.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        static void AddFrom(string dir, bool bareName, List<MapEntry> entries, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.pud", SearchOption.TopDirectoryOnly); }
            catch (Exception e) { Debug.LogWarning($"[Craftwar] Map scan failed for {dir}: {e.Message}"); return; }

            foreach (var f in files)
            {
                string file = Path.GetFileName(f);
                if (!seen.Add(file))
                    continue; // a StreamingAssets copy shadows the install's
                entries.Add(new MapEntry
                {
                    Label = Path.GetFileNameWithoutExtension(f),
                    Value = bareName ? file : f,
                });
            }
        }
    }
}
