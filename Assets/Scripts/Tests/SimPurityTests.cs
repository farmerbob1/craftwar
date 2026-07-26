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
                ScanFile(file, Banned);
        }

        static string NetRoot => Path.Combine(Application.dataPath, "Scripts", "Net");

        /// <summary>
        /// The lockstep protocol lives in Craftwar.Net and must stay pure C#: it
        /// runs inside the sim's determinism contract, and the standalone dotnet
        /// harness compiles Sim + Net together to test two peers in-process
        /// without an editor. A single UnityEngine reference would end both.
        /// Transport/socket code that genuinely needs Unity belongs in the
        /// separate Craftwar.Net.Unity assembly.
        /// </summary>
        [Test]
        public void NetSources_StayEngineFreeAndDeterministic()
        {
            if (!Directory.Exists(NetRoot))
                return;
            var banned = new (Regex, string)[Banned.Length + 1];
            System.Array.Copy(Banned, banned, Banned.Length);
            banned[Banned.Length] = (new Regex(@"\bUnityEngine\b|\bUnityEditor\b"),
                "Craftwar.Net must compile outside Unity (standalone test harness)");

            string[] files = Directory.GetFiles(NetRoot, "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(files, "Net source folder should contain code");
            foreach (string file in files)
                ScanFile(file, banned);
        }

        /// <summary>
        /// The tile layer reaches the state hash through a running checksum, so a
        /// write that skips GameState.SetTile is invisible to desync detection —
        /// silently, and only on the machine that diverged. The funnel is only a
        /// guarantee if nothing can bypass it.
        /// </summary>
        [Test]
        public void TileWrites_AllGoThroughTheSetTileFunnel()
        {
            var direct = new Regex(@"\bTiles\s*\[[^\]]*\]\s*=[^=]");
            string[] files = Directory.GetFiles(SimRoot, "*.cs", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                // GameState owns the array; PudFile.Tiles is the immutable source
                // map, not the mutable per-match layer.
                string name = Path.GetFileName(file);
                if (name == "GameState.cs" || name == "PudFile.cs")
                    continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///"))
                        continue;
                    if (trimmed.Contains("pud.Tiles"))
                        continue;
                    Assert.IsFalse(direct.IsMatch(lines[i]),
                        $"{name}:{i + 1} writes the tile layer directly; use GameState.SetTile: {trimmed}");
                }
            }
        }

        static void ScanFile(string file, (Regex pattern, string why)[] banned)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///"))
                    continue;
                foreach (var (pattern, why) in banned)
                {
                    Assert.IsFalse(pattern.IsMatch(line),
                        $"{Path.GetFileName(file)}:{i + 1} banned construct ({why}): {line.Trim()}");
                }
            }
        }
    }
}
