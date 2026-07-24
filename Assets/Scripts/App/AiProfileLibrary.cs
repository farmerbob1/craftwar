using System.Collections.Generic;
using System.IO;
using Craftwar.Sim.Ai;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// The app's catalogue of AI profiles: the built-ins embedded in the sim plus
    /// any modder-authored <c>*.ai</c> files dropped into
    /// <c>persistentDataPath/Ai/</c>. Lives in Craftwar.App because discovery
    /// touches the filesystem and UnityEngine — the sim only ever receives the
    /// parsed, integer-only <see cref="AiProfile"/>, which is lockstep-safe by
    /// construction (the DSL admits no floats or nondeterminism).
    /// </summary>
    public static class AiProfileLibrary
    {
        public const string DefaultName = BuiltinAiProfiles.LandAttackName;

        static string PlayerDir => Path.Combine(Application.persistentDataPath, "Ai");

        /// <summary>Selectable profile names: built-ins first, then player files (by
        /// file stem), de-duplicated, in a stable order.</summary>
        public static List<string> Names()
        {
            var names = new List<string> { BuiltinAiProfiles.LandAttackName };
            try
            {
                if (Directory.Exists(PlayerDir))
                {
                    var files = Directory.GetFiles(PlayerDir, "*.ai");
                    System.Array.Sort(files, System.StringComparer.Ordinal); // deterministic
                    foreach (var f in files)
                    {
                        string stem = StemOf(f);
                        if (!names.Contains(stem))
                            names.Add(stem);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Craftwar] AI profile discovery failed: {e.Message}");
            }
            return names;
        }

        /// <summary>Resolve a name to a profile: a built-in, else a player file, else
        /// the default. Never throws — a broken file falls back with a warning so a
        /// bad mod can't brick the lobby.</summary>
        public static AiProfile Resolve(string name)
        {
            if (string.IsNullOrEmpty(name))
                return BuiltinAiProfiles.Default;

            var builtin = BuiltinAiProfiles.Get(name);
            if (builtin != null)
                return builtin;

            try
            {
                string path = Path.Combine(PlayerDir, name + ".ai");
                if (File.Exists(path))
                    return AiProfileParser.Parse(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Craftwar] AI profile '{name}' failed to load: {e.Message}");
            }
            return BuiltinAiProfiles.Default;
        }

        static string StemOf(string path)
        {
            string file = Path.GetFileName(path); // "<name>.ai"
            const string suffix = ".ai";
            return file.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - suffix.Length)
                : Path.GetFileNameWithoutExtension(path);
        }
    }
}
