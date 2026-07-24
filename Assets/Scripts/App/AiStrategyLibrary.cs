using System.Collections.Generic;
using System.IO;
using Craftwar.Sim.Ai;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// The app's catalogue of AI strategies: the built-ins embedded in the sim
    /// plus any player-authored <c>*.ai.txt</c> files dropped into
    /// <c>persistentDataPath/Ai/</c>. Lives in Craftwar.App because discovery
    /// touches the filesystem and UnityEngine — the sim only ever receives the
    /// parsed, integer-only <see cref="AiStrategy"/>.
    /// </summary>
    public static class AiStrategyLibrary
    {
        public const string DefaultName = BuiltinAiStrategies.LandAttackName;

        static string PlayerDir => Path.Combine(Application.persistentDataPath, "Ai");

        /// <summary>Selectable strategy names: built-ins first, then player files
        /// (by file stem), de-duplicated, in a stable order.</summary>
        public static List<string> Names()
        {
            var names = new List<string> { BuiltinAiStrategies.LandAttackName };
            try
            {
                if (Directory.Exists(PlayerDir))
                {
                    var files = Directory.GetFiles(PlayerDir, "*.ai.txt");
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
                Debug.LogWarning($"[Craftwar] AI strategy discovery failed: {e.Message}");
            }
            return names;
        }

        /// <summary>Resolve a name to a strategy: a built-in, else a player file,
        /// else the default. Never throws — a broken file falls back with a warning
        /// so a bad mod can't brick the lobby.</summary>
        public static AiStrategy Resolve(string name)
        {
            if (string.IsNullOrEmpty(name))
                return BuiltinAiStrategies.Default;

            var builtin = BuiltinAiStrategies.Get(name);
            if (builtin != null)
                return builtin;

            try
            {
                string path = Path.Combine(PlayerDir, name + ".ai.txt");
                if (File.Exists(path))
                    return AiStrategyParser.Parse(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Craftwar] AI strategy '{name}' failed to load: {e.Message}");
            }
            return BuiltinAiStrategies.Default;
        }

        static string StemOf(string path)
        {
            string file = Path.GetFileName(path); // "<name>.ai.txt"
            const string suffix = ".ai.txt";
            return file.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - suffix.Length)
                : Path.GetFileNameWithoutExtension(path);
        }
    }
}
