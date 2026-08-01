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
    /// Deliberately mirrors GameLoopRunnerEditor's rules so the runtime picker and
    /// the inspector dropdown agree: sort case-insensitively so the order is the
    /// same on every machine, and store a bare file name for anything in
    /// StreamingAssets so the value stays portable, falling back to an absolute
    /// path only for maps that live in the player's own install.
    /// </summary>
    public static class MapList
    {
        /// <summary>Where the importer bakes raw .pud bytes as TextAssets — see
        /// Craftwar.EditorTools.MapBaker. A bare map name (with its .pud
        /// extension) doubles as both the wire-format value and the
        /// Resources key, so nothing downstream needs to know a bake even
        /// happened.</summary>
        public const string ResourcesFolder = "Maps";

        public static List<MapEntry> Find(LocalAssetPaths paths)
        {
            var entries = new List<MapEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Baked maps first: no install required, and identical on every
            // machine that shipped the same build.
            foreach (var asset in Resources.LoadAll<TextAsset>(ResourcesFolder))
            {
                if (!seen.Add(asset.name))
                    continue;
                entries.Add(new MapEntry
                {
                    Label = Path.GetFileNameWithoutExtension(asset.name),
                    Value = asset.name,
                });
            }

            // Dev maps shipped in StreamingAssets: store the bare name.
            AddFrom(GameLoopRunner.StreamingMapsDir, bareName: true, entries, seen);

            // The player's own install: absolute, since it is machine-specific.
            if (paths != null && !string.IsNullOrEmpty(paths.mapsDir))
                AddFrom(paths.mapsDir, bareName: false, entries, seen);

            entries.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        /// <summary>
        /// Resolves a wire-format map value (a bare baked/StreamingAssets name,
        /// an absolute path, or empty for the per-machine default) to bytes.
        /// Tries the baked Resources catalog first — the only path that needs
        /// no install and is guaranteed identical across every machine running
        /// the same build — then falls back to disk exactly as before.
        /// </summary>
        public static bool TryReadMapBytes(LocalAssetPaths paths, string mapPath, out byte[] bytes)
        {
            bytes = null;
            string value = string.IsNullOrEmpty(mapPath) ? paths?.defaultMap : mapPath;
            if (string.IsNullOrEmpty(value))
                return false;

            bool bareName = value.IndexOf('/') < 0 && value.IndexOf('\\') < 0;
            if (bareName)
            {
                var asset = Resources.Load<TextAsset>($"{ResourcesFolder}/{value}");
                if (asset != null)
                {
                    bytes = asset.bytes;
                    return true;
                }
            }

            var candidates = bareName
                ? new[]
                {
                    Path.Combine(GameLoopRunner.StreamingMapsDir, value),
                    paths != null && !string.IsNullOrEmpty(paths.mapsDir) ? Path.Combine(paths.mapsDir, value) : null,
                }
                : new[] { value };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
                    continue;
                try
                {
                    bytes = File.ReadAllBytes(candidate);
                    return true;
                }
                catch (IOException) { }
            }
            return false;
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
