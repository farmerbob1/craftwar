using System.IO;
using Craftwar.Sim.Ai;
using NUnit.Framework;
using UnityEngine;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// EditMode-only: guards that the built-in strategy text embedded in the sim
    /// (<see cref="BuiltinAiStrategies.LandAttackText"/>) stays in sync with the
    /// player-facing copy under StreamingAssets. Touches UnityEngine
    /// (Application.streamingAssetsPath), so it is excluded from the standalone
    /// dotnet harness — the batch EditMode gate runs it.
    /// </summary>
    public class AiStrategyDriftTests
    {
        [Test]
        public void EmbeddedLandAttack_MatchesStreamingAssetsCopy()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Ai", "land-attack.ai.txt");
            Assert.IsTrue(File.Exists(path), $"missing built-in strategy file: {path}");
            string onDisk = Normalize(File.ReadAllText(path));
            string embedded = Normalize(BuiltinAiStrategies.LandAttackText);
            Assert.AreEqual(embedded, onDisk,
                "land-attack.ai.txt drifted from BuiltinAiStrategies.LandAttackText — " +
                "update both, they are the same strategy.");
        }

        [Test]
        public void StreamingAssetsCopy_ParsesToSameHash()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Ai", "land-attack.ai.txt");
            var fromDisk = AiStrategyParser.Parse(File.ReadAllText(path));
            Assert.AreEqual(BuiltinAiStrategies.Default.Hash(), fromDisk.Hash());
        }

        static string Normalize(string s) =>
            s.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ', '\t');
    }
}
