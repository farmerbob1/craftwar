using System.Collections.Generic;
using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class DeterminismTests
    {
        static readonly IReadOnlyList<GameCommand> NoCommands = new List<GameCommand>();

        static GameSim RunSim(ulong seed, int ticks, bool withActivity)
        {
            var sim = new GameSim(seed);
            if (withActivity)
            {
                // Touch every state field type so the hash walk covers them.
                sim.State.Players[0].InGame = true;
                sim.State.Players[0].Gold = 1200;
                sim.State.Players[0].Lumber = 800;
                var a = sim.State.SpawnUnit(0x00, 0, 10, 12); // footman
                var b = sim.State.SpawnUnit(0x02, 0, 11, 12); // peasant
                sim.State.SpawnUnit(0x01, 1, 60, 60);         // enemy grunt
                sim.State.DestroyUnit(b);
                sim.State.SpawnUnit(0x03, 1, 61, 60);         // peon, recycles slot
                Assert.IsTrue(sim.State.TryGetUnitIndex(a, out _));
                Assert.IsFalse(sim.State.TryGetUnitIndex(b, out _), "stale handle must not resolve");
            }
            for (int i = 0; i < ticks; i++)
                sim.Advance(NoCommands);
            return sim;
        }

        [Test]
        public void EmptySim_1000Ticks_IdenticalHashAcrossRuns()
        {
            var simA = RunSim(seed: 0xC0FFEE, ticks: 1000, withActivity: false);
            var simB = RunSim(seed: 0xC0FFEE, ticks: 1000, withActivity: false);
            Assert.AreEqual(simA.State.ComputeHash(), simB.State.ComputeHash());
            Assert.AreEqual(1000, simA.State.Tick);
        }

        [Test]
        public void ActiveSim_1000Ticks_IdenticalHashAcrossRuns()
        {
            var simA = RunSim(seed: 42, ticks: 1000, withActivity: true);
            var simB = RunSim(seed: 42, ticks: 1000, withActivity: true);
            Assert.AreEqual(simA.State.ComputeHash(), simB.State.ComputeHash());
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentHashes()
        {
            var simA = RunSim(seed: 1, ticks: 10, withActivity: false);
            var simB = RunSim(seed: 2, ticks: 10, withActivity: false);
            Assert.AreNotEqual(simA.State.ComputeHash(), simB.State.ComputeHash());
        }

        [Test]
        public void StateHash_IsSensitiveToUnitChanges()
        {
            var sim = RunSim(seed: 7, ticks: 0, withActivity: true);
            uint before = sim.State.ComputeHash();
            sim.State.Units[0].Hp += 1;
            Assert.AreNotEqual(before, sim.State.ComputeHash());
        }
    }
}
