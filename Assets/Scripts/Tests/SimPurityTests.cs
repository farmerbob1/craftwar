using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Guards the determinism contract of the Sim assembly at the source level.
    /// The asmdef already blocks UnityEngine (noEngineReferences), but nothing
    /// stops floats or wall-clock time creeping in — these tests do.
    /// </summary>
    public class SimPurityTests
    {
        static string SimRoot => Path.Combine(Application.dataPath, "Scripts", "Sim");

        static readonly (Regex pattern, string why)[] Banned =
        {
            (new Regex(@"\b(float|double|System\.Single|System\.Double)\b"), "floating point breaks cross-platform determinism"),
            (new Regex(@"\bSystem\.Math[fF]?\b|\bMath\.(Sin|Cos|Sqrt|Pow|Exp|Log)\b"), "transcendental math is not bit-reproducible"),
            (new Regex(@"\bDateTime\b|\bStopwatch\b|\bEnvironment\.TickCount\b"), "wall-clock time must never reach the sim"),
            (new Regex(@"\bnew\s+(System\.)?Random\b"), "System.Random is not our deterministic PRNG"),
            (new Regex(@"foreach\s*\(\s*var?\s.*\bin\s.*(Dictionary|HashSet)"), "unordered collection iteration is nondeterministic"),
        };

        [Test]
        public void SimSources_ContainNoNondeterministicConstructs()
        {
            string[] files = Directory.GetFiles(SimRoot, "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(files, "Sim source folder should contain code");

            foreach (string file in files)
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///"))
                        continue;
                    foreach (var (pattern, why) in Banned)
                    {
                        Assert.IsFalse(pattern.IsMatch(line),
                            $"{Path.GetFileName(file)}:{i + 1} banned construct ({why}): {line.Trim()}");
                    }
                }
            }
        }
    }
}
