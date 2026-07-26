using System.Collections.Generic;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Snapshot round-trips. The bar is not "the game looks right after loading"
    /// but "a loaded sim is indistinguishable from the live one and stays that
    /// way", because reconnect drops a player back into a match already running:
    /// any difference is an instant desync.
    /// </summary>
    public class SimSerializerTests
    {
        static GameSim RunFor(int ticks, ulong seed = 31337)
        {
            var pud = AiTestHarness.TwoBaseMap();
            var sim = AiTestHarness.Boot(pud, seed);
            var ais = AiTestHarness.CreateAis(sim);
            AiTestHarness.RunAiMatch(sim, ais, ticks);
            return sim;
        }

        [Test]
        public void ALoadedSnapshot_HashesIdenticallyToTheLiveSim()
        {
            var live = RunFor(2000);
            var loaded = SimSerializer.Load(SimSerializer.Save(live));

            Assert.AreEqual(live.State.Tick, loaded.State.Tick);
            Assert.AreEqual(live.State.ComputeHash(), loaded.State.ComputeHash(),
                "a snapshot must reproduce the state hash exactly");
            Assert.IsNull(loaded.State.VerifyChecksums(),
                "and the running checksums must be reseeded, not left at zero");
        }

        [Test]
        public void ALoadedSnapshot_KeepsMatchingAsBothRunOn()
        {
            // The real test: identical now is easy, identical after both sides
            // simulate independently is what reconnect actually needs.
            var live = RunFor(1500);
            var loaded = SimSerializer.Load(SimSerializer.Save(live));

            var none = new List<GameCommand>();
            for (int i = 0; i < 1200; i++)
            {
                live.Advance(none);
                loaded.Advance(none);
                if (live.State.ComputeHash() != loaded.State.ComputeHash())
                    Assert.Fail($"diverged {i} ticks after loading (tick {live.State.Tick})");
            }
            Assert.IsNull(loaded.State.VerifyChecksums());
        }

        [Test]
        public void UnhashedButAuthoritativeState_SurvivesTheRoundTrip()
        {
            // Occupancy, paths and the terrain planes are the pieces a hash-only
            // comparison would miss: paths are not hashed at all, and the others
            // only became hashed in phase 1. Compare them directly.
            var live = RunFor(2000);
            var loaded = SimSerializer.Load(SimSerializer.Save(live));
            var a = live.State;
            var b = loaded.State;

            CollectionAssert.AreEqual(a.OccupancySurface, b.OccupancySurface, "surface occupancy");
            CollectionAssert.AreEqual(a.OccupancyAir, b.OccupancyAir, "air occupancy");

            for (int i = 0; i < a.HighestUnitIndex; i++)
            {
                int length = a.Units[i].PathLength;
                if (length == 0 || a.UnitPaths[i] == null)
                    continue;
                Assert.IsNotNull(b.UnitPaths[i], $"unit {i} lost its path");
                for (int n = 0; n < length && n < a.UnitPaths[i].Length; n++)
                    Assert.AreEqual(a.UnitPaths[i][n], b.UnitPaths[i][n],
                        $"unit {i} path step {n}");
            }

            CollectionAssert.AreEqual(a.Terrain.PassablePlane, b.Terrain.PassablePlane, "passability");
            CollectionAssert.AreEqual(a.Terrain.WoodPlane, b.Terrain.WoodPlane, "remaining wood");
            CollectionAssert.AreEqual(a.Terrain.ShorePlane, b.Terrain.ShorePlane,
                "shore, which gates transport unloading and is not derivable from passability");
        }

        [Test]
        public void DeadSlotsKeepTheirGeneration_SoTheNextSpawnGetsTheSameId()
        {
            // Gen is bumped on slot reuse. Saving only living units would make
            // the first post-load spawn mint a different UnitId than a peer's.
            var live = RunFor(600);

            // Kill something outright rather than waiting for the AIs to meet —
            // an AI match does not reach combat for tens of thousands of ticks.
            int deadSlot = -1;
            for (int i = 0; i < live.State.HighestUnitIndex; i++)
                if (live.State.Units[i].IsAlive && live.State.Units[i].Player == 0)
                {
                    deadSlot = i;
                    live.State.DestroyUnit(new UnitId((ushort)i, live.State.Units[i].Gen));
                    break;
                }
            Assert.GreaterOrEqual(deadSlot, 0, "the map should have units to kill");
            Assert.IsFalse(live.State.Units[deadSlot].IsAlive);

            var loaded = SimSerializer.Load(SimSerializer.Save(live));
            Assert.AreEqual(live.State.Units[deadSlot].Gen, loaded.State.Units[deadSlot].Gen);
            Assert.AreEqual(live.State.HighestUnitIndex, loaded.State.HighestUnitIndex);

            // And the recycle stack itself, which decides WHICH slot is next.
            var liveId = live.State.SpawnUnit((ushort)UnitTypeId.Footman, 0, 5, 5);
            var loadedId = loaded.State.SpawnUnit((ushort)UnitTypeId.Footman, 0, 5, 5);
            Assert.AreEqual(liveId.Packed, loadedId.Packed,
                "the next spawn must take the same slot and generation");
        }

        [Test]
        public void ChoppedForest_SurvivesTheRoundTrip()
        {
            // Wood is not reconstructible from the tile layer: the one-tree
            // remnant tiles still hold wood but are not classified as forest.
            var pud = AiTestHarness.TwoBaseMap();
            var sim = AiTestHarness.Boot(pud, 5);
            sim.State.Terrain.Chop(6, 24);
            sim.State.Terrain.Chop(7, 24);

            var loaded = SimSerializer.Load(SimSerializer.Save(sim));
            Assert.IsFalse(loaded.State.Terrain.HasWood(6, 24), "felled trees stay felled");
            Assert.IsFalse(loaded.State.Terrain.HasWood(7, 24));
            Assert.IsTrue(loaded.State.Terrain.IsPassable(MoveDomain.Land, 6, 24),
                "and the cleared tile stays walkable");
            Assert.AreEqual(sim.State.ComputeHash(), loaded.State.ComputeHash());
        }

        [Test]
        public void RngStream_IsRestoredRatherThanReseeded()
        {
            // Draw counts are data-dependent (NextUInt uses rejection sampling),
            // so the stream position cannot be recovered by counting from a seed.
            var live = RunFor(700);
            var loaded = SimSerializer.Load(SimSerializer.Save(live));

            Assert.AreEqual(live.State.Rng.State, loaded.State.Rng.State);
            Assert.AreEqual(live.State.Rng.Inc, loaded.State.Rng.Inc);
            Assert.AreEqual(live.State.Rng.NextUInt(), loaded.State.Rng.NextUInt(),
                "the next draw must match");
        }

        [Test]
        public void ASaveFromAnotherSimVersion_IsRefusedRatherThanLoaded()
        {
            var live = RunFor(100);
            byte[] bytes = SimSerializer.Save(live);
            // Corrupt the SimVersion field (magic 4 + version 2).
            bytes[6] = (byte)(bytes[6] + 1);

            Assert.Throws<System.IO.InvalidDataException>(() => SimSerializer.Load(bytes),
                "loading a save from different sim rules would silently not reproduce the game");
        }

        [Test]
        public void ASaveIsSubstantiallySmallerThanTheRawState()
        {
            // The grids run-length code well; a save that ballooned would make
            // reconnect snapshot transfer painful.
            var live = RunFor(1500);
            byte[] bytes = SimSerializer.Save(live);
            Assert.Less(bytes.Length, 400_000, "snapshot should stay compact");
            Assert.Greater(bytes.Length, 1000, "and obviously must contain the match");
        }
    }
}
