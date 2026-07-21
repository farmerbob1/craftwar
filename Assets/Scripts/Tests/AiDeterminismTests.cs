using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class AiDeterminismTests
    {
        const int Ticks = 12000;

        [Test]
        public void AiMatch_SameSeed_TwoRuns_HashIdentical()
        {
            uint RunOnce()
            {
                var sim = AiTestHarness.Boot(AiTestHarness.TwoBaseMap(), seed: 11);
                AiTestHarness.RunAiMatch(sim, AiTestHarness.CreateAis(sim), Ticks);
                return sim.State.ComputeHash();
            }

            Assert.AreEqual(RunOnce(), RunOnce(),
                "an AI match must be a pure function of (map, seed)");
        }

        [Test]
        public void AiMatch_ReplayPlayback_ReproducesFinalHash()
        {
            var pud = AiTestHarness.TwoBaseMap();
            var replay = new Replay { Seed = 11 };

            var live = AiTestHarness.Boot(pud, seed: 11);
            AiTestHarness.RunAiMatch(live, AiTestHarness.CreateAis(live), Ticks, replay);
            Assert.Greater(replay.Entries.Count, 0, "the AIs must actually have acted");

            // Playback constructs NO AiPlayer: the recorded commands alone must
            // reproduce the world. This is what makes AI tuning replay-safe.
            var back = AiTestHarness.Playback(pud, seed: 11, replay, Ticks);
            Assert.AreEqual(live.State.ComputeHash(), back.State.ComputeHash(),
                "recorded AI commands must replay to the identical world");
        }

        [Test]
        public void AiMatch_ReplayRoundTripsThroughBytes()
        {
            var pud = AiTestHarness.TwoBaseMap();
            var replay = new Replay { Seed = 11 };
            var live = AiTestHarness.Boot(pud, seed: 11);
            AiTestHarness.RunAiMatch(live, AiTestHarness.CreateAis(live), 4000, replay);

            var wire = Replay.FromBytes(replay.ToBytes());
            var back = AiTestHarness.Playback(pud, seed: 11, wire, 4000);
            Assert.AreEqual(live.State.ComputeHash(), back.State.ComputeHash());
        }
    }
}
