using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Submarines are invisible and untargetable except inside a detector's
    /// sight disc (UDTA Submarine / SeesSubmarine). This is the one place fog
    /// gates gameplay rather than just rendering.
    /// </summary>
    public class SubmarineTests
    {
        static PudFile OpenSea()
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0040; // all water
            }
            pud.Owner[0] = (byte)PudOwner.Human;
            pud.Owner[1] = (byte)PudOwner.Computer;
            pud.Side[0] = 0;
            pud.Side[1] = 1;
            return pud;
        }

        static GameSim Boot(PudFile pud, ulong seed = 5)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
        }

        static int Spawn(GameSim sim, UnitTypeId type, ushort x, ushort y, byte owner)
        {
            var id = sim.State.SpawnUnit((ushort)type, owner, x, y);
            sim.State.TryGetUnitIndex(id, out int i);
            sim.State.Units[i].Hp = sim.State.Rules.Units[(ushort)type].Hp;
            return i;
        }

        static bool Sees(GameSim sim, int player, int slot) =>
            sim.IsUnitVisible(player, ref sim.State.Units[slot]);

        static void Run(GameSim sim, int ticks)
        {
            var none = new List<GameCommand>();
            for (int t = 0; t < ticks; t++) sim.Advance(none);
        }

        [Test]
        public void Submarine_IsInvisibleToAnUndetectingEnemy()
        {
            var sim = Boot(OpenSea());
            int sub = Spawn(sim, UnitTypeId.GnomishSubmarine, 16, 16, owner: 1);
            // A destroyer sees submarines; a battleship does not.
            Spawn(sim, UnitTypeId.Battleship, 18, 16, owner: 0);
            Run(sim, 2);

            Assert.IsFalse(sim.State.Rules.Units[(ushort)UnitTypeId.Battleship]
                    .Is(UnitTypeFlags.SeesSubmarine),
                "precondition: a battleship has no sonar");
            Assert.IsFalse(Sees(sim, 0, sub), "the sub stays hidden");
        }

        [TestCase(UnitTypeId.GnomishFlyingMachine)]
        [TestCase(UnitTypeId.GryphonRider)]
        [TestCase(UnitTypeId.HumanScoutTower)]
        public void Detectors_ExposeANearbySubmarine(UnitTypeId detector)
        {
            // Per the BNE table the spotters are the flyers and the towers —
            // notably NOT surface warships, which is why a flying machine is
            // the classic answer to a submarine.
            var sim = Boot(OpenSea());
            int sub = Spawn(sim, UnitTypeId.GnomishSubmarine, 16, 16, owner: 1);
            Spawn(sim, detector, 18, 16, owner: 0);
            Run(sim, 2);

            Assert.IsTrue(sim.State.Rules.Units[(ushort)detector].Is(UnitTypeFlags.SeesSubmarine),
                $"precondition: {detector} has sonar");
            Assert.IsTrue(Sees(sim, 0, sub), "the detector exposes it");
        }

        [Test]
        public void SurfaceWarships_HaveNoSonarOfTheirOwn()
        {
            var sim = Boot(OpenSea());
            int sub = Spawn(sim, UnitTypeId.GnomishSubmarine, 16, 16, owner: 1);
            Spawn(sim, UnitTypeId.ElvenDestroyer, 18, 16, owner: 0);
            Run(sim, 2);
            Assert.IsFalse(Sees(sim, 0, sub), "a destroyer cannot find it alone");
        }

        [Test]
        public void OwnSubmarines_AreAlwaysVisibleToTheirOwner()
        {
            var sim = Boot(OpenSea());
            int sub = Spawn(sim, UnitTypeId.GnomishSubmarine, 16, 16, owner: 0);
            Run(sim, 2);
            Assert.IsTrue(Sees(sim, 0, sub));
        }

        [Test]
        public void UndetectedSubmarine_IsNotAutoAcquired()
        {
            var sim = Boot(OpenSea());
            int sub = Spawn(sim, UnitTypeId.GnomishSubmarine, 17, 16, owner: 1);
            int ship = Spawn(sim, UnitTypeId.Battleship, 18, 16, owner: 0);
            int subHp = sim.State.Units[sub].Hp;

            Run(sim, 300);

            Assert.AreEqual(0u, sim.State.Units[ship].AttackTarget,
                "a battleship never finds it");
            Assert.AreEqual(subHp, sim.State.Units[sub].Hp, "and never damages it");
        }

        [Test]
        public void DetectionIsPlayerWide_SoASpotterLetsTheFleetFire()
        {
            // The flying machine cannot shoot and the destroyer cannot see;
            // together they sink it. Detection is a per-player grid, not a
            // per-unit check.
            var sim = Boot(OpenSea());
            int sub = Spawn(sim, UnitTypeId.GnomishSubmarine, 17, 16, owner: 1);
            Spawn(sim, UnitTypeId.ElvenDestroyer, 18, 16, owner: 0);
            Spawn(sim, UnitTypeId.GnomishFlyingMachine, 18, 17, owner: 0);
            int subHp = sim.State.Units[sub].Hp;

            Run(sim, 300);

            Assert.Less(sim.State.Units[sub].Hp, subHp,
                "spotted for the fleet, the sub takes fire");
        }

        [Test]
        public void SubmarineDetection_IsHashedAndDeterministic()
        {
            static uint RunOnce()
            {
                var sim = Boot(OpenSea());
                Spawn(sim, UnitTypeId.GnomishSubmarine, 17, 16, owner: 1);
                Spawn(sim, UnitTypeId.ElvenDestroyer, 20, 16, owner: 0);
                Spawn(sim, UnitTypeId.GnomishFlyingMachine, 20, 17, owner: 0);
                Run(sim, 400);
                return sim.State.ComputeHash();
            }
            Assert.AreEqual(RunOnce(), RunOnce());
        }
    }
}
