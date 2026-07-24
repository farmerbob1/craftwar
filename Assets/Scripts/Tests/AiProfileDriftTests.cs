using System.IO;
using Craftwar.Sim.Ai;
using NUnit.Framework;
using UnityEngine;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// EditMode-only: guards that the built-in profile text embedded in the sim
    /// (<see cref="BuiltinAiProfiles.LandAttackText"/>) stays in sync with the
    /// modder-facing copy under StreamingAssets. Touches UnityEngine
    /// (Application.streamingAssetsPath), so it is excluded from the standalone
    /// dotnet harness — the batch EditMode gate runs it.
    /// </summary>
    public class AiProfileDriftTests
    {
        [Test]
        public void EmbeddedLandAttack_MatchesStreamingAssetsCopy()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Ai", "land-attack.ai");
            Assert.IsTrue(File.Exists(path), $"missing built-in profile file: {path}");
            string onDisk = Normalize(File.ReadAllText(path));
            string embedded = Normalize(BuiltinAiProfiles.LandAttackText);
            Assert.AreEqual(embedded, onDisk,
                "land-attack.ai drifted from BuiltinAiProfiles.LandAttackText — " +
                "update both, they are the same profile.");
        }

        [Test]
        public void StreamingAssetsCopy_ParsesToSameHash()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Ai", "land-attack.ai");
            var fromDisk = AiProfileParser.Parse(File.ReadAllText(path));
            Assert.AreEqual(BuiltinAiProfiles.Default.Hash(), fromDisk.Hash());
        }

        static string Normalize(string s) =>
            s.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ', '\t');
    }
}
