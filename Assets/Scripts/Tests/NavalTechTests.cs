using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The naval + air branch of the tech tree. The ALOW bits and the upgrade
    /// magnitudes landed in M5; what M7 adds is the menus, the production lists
    /// and the prerequisite chains that make them reachable.
    /// </summary>
    public class NavalTechTests
    {
        static bool Lists(UnitTypeId[] menu, UnitTypeId want)
        {
            foreach (var t in menu) if (t == want) return true;
            return false;
        }

        static bool Lists(UpgradeId[] menu, UpgradeId want)
        {
            foreach (var u in menu) if (u == want) return true;
            return false;
        }

        [TestCase(Race.Human, UnitTypeId.HumanShipyard)]
        [TestCase(Race.Human, UnitTypeId.HumanFoundry)]
        [TestCase(Race.Human, UnitTypeId.HumanRefinery)]
        [TestCase(Race.Human, UnitTypeId.GryphonAviary)]
        [TestCase(Race.Orc, UnitTypeId.OrcShipyard)]
        [TestCase(Race.Orc, UnitTypeId.OrcFoundry)]
        [TestCase(Race.Orc, UnitTypeId.OrcRefinery)]
        [TestCase(Race.Orc, UnitTypeId.DragonRoost)]
        public void WorkerMenu_OffersTheNavalAndAirStructures(Race race, UnitTypeId type)
        {
            Assert.IsTrue(Lists(TechTree.WorkerBuildings(race), type));
            Assert.IsFalse(TechTree.IsBasicBuilding(race, type),
                "they belong on the advanced page");
        }

        [TestCase(Race.Human, UnitTypeId.HumanOilWell)]
        [TestCase(Race.Orc, UnitTypeId.OrcOilWell)]
        public void OilPlatform_IsOnTheTankerCard_NotTheWorkerCard(Race race, UnitTypeId well)
        {
            Assert.IsTrue(Lists(TechTree.TankerBuildings(race), well));
            Assert.IsFalse(Lists(TechTree.WorkerBuildings(race), well),
                "a peasant cannot raise a platform — only a tanker can");
        }

        [Test]
        public void AdvancedPage_StillFitsTheCard()
        {
            // The card reserves slot 8 for Back, so a page may hold at most 8.
            foreach (var race in new[] { Race.Human, Race.Orc })
            {
                int advanced = TechTree.WorkerBuildings(race).Length - TechTree.BasicBuildingCount;
                Assert.LessOrEqual(advanced, 8, $"{race} advanced page overflows the card");
                Assert.LessOrEqual(TechTree.BasicBuildingCount, 8, $"{race} basic page");
            }
        }

        [TestCase(UnitTypeId.HumanShipyard, UnitTypeId.HumanTanker)]
        [TestCase(UnitTypeId.HumanShipyard, UnitTypeId.HumanTransport)]
        [TestCase(UnitTypeId.HumanShipyard, UnitTypeId.ElvenDestroyer)]
        [TestCase(UnitTypeId.HumanShipyard, UnitTypeId.Battleship)]
        [TestCase(UnitTypeId.HumanShipyard, UnitTypeId.GnomishSubmarine)]
        [TestCase(UnitTypeId.OrcShipyard, UnitTypeId.OrcTanker)]
        [TestCase(UnitTypeId.OrcShipyard, UnitTypeId.Juggernaught)]
        [TestCase(UnitTypeId.OrcShipyard, UnitTypeId.GiantTurtle)]
        [TestCase(UnitTypeId.GryphonAviary, UnitTypeId.GryphonRider)]
        [TestCase(UnitTypeId.DragonRoost, UnitTypeId.Dragon)]
        [TestCase(UnitTypeId.GnomishInventor, UnitTypeId.GnomishFlyingMachine)]
        [TestCase(UnitTypeId.GoblinAlchemist, UnitTypeId.GoblinZeppelin)]
        public void Buildings_TrainTheirRoster(UnitTypeId building, UnitTypeId unit)
        {
            Assert.IsTrue(Lists(TechTree.Trains(building), unit));
        }

        [Test]
        public void Foundry_ProvidesTheShipUpgrades()
        {
            // These already apply in combat (GameSim.Tech's MoveDomain == 2
            // branches) — before M7 no building offered them.
            Assert.IsTrue(Lists(TechTree.Research(UnitTypeId.HumanFoundry),
                UpgradeId.HumanShipCannon1));
            Assert.IsTrue(Lists(TechTree.Research(UnitTypeId.HumanFoundry),
                UpgradeId.HumanShipArmor2));
            Assert.IsTrue(Lists(TechTree.Research(UnitTypeId.OrcFoundry),
                UpgradeId.OrcShipCannon1));
            Assert.IsTrue(Lists(TechTree.Research(UnitTypeId.OrcFoundry),
                UpgradeId.OrcShipArmor2));
        }

        // Ground truth: PEON.C fnCanBuild -> OLDSB.C can_build_* (structures)
        // and the shipyard/aviary card gates bf_*_ok (units).
        [TestCase(UnitTypeId.HumanShipyard, UnitTypeId.ElvenLumberMill)]
        [TestCase(UnitTypeId.OrcShipyard, UnitTypeId.TrollLumberMill)]
        [TestCase(UnitTypeId.HumanFoundry, UnitTypeId.HumanShipyard)]
        [TestCase(UnitTypeId.HumanRefinery, UnitTypeId.HumanShipyard)]
        [TestCase(UnitTypeId.HumanTransport, UnitTypeId.HumanFoundry)]
        [TestCase(UnitTypeId.OrcTransport, UnitTypeId.OrcFoundry)]
        [TestCase(UnitTypeId.Battleship, UnitTypeId.HumanFoundry)]
        [TestCase(UnitTypeId.Juggernaught, UnitTypeId.OrcFoundry)]
        [TestCase(UnitTypeId.GnomishSubmarine, UnitTypeId.GnomishInventor)]
        [TestCase(UnitTypeId.GiantTurtle, UnitTypeId.GoblinAlchemist)]
        [TestCase(UnitTypeId.GnomishFlyingMachine, UnitTypeId.GnomishInventor)]
        [TestCase(UnitTypeId.GnomishFlyingMachine, UnitTypeId.ElvenLumberMill)]
        [TestCase(UnitTypeId.GryphonRider, UnitTypeId.GryphonAviary)]
        [TestCase(UnitTypeId.Dragon, UnitTypeId.DragonRoost)]
        public void Prereqs_MatchTheOriginalGateTable(UnitTypeId type, UnitTypeId required)
        {
            Assert.IsTrue(Lists(TechTree.Prereqs(type), required),
                $"{type} should require {required}");
        }

        [Test]
        public void TankerAndDestroyer_NeedNothingBeyondTheShipyard()
        {
            // bf_tanker_ok / bf_destroyer_ok gate on nothing but the ALOW bit —
            // the shipyard hosting the button is the only requirement.
            Assert.AreEqual(0, TechTree.Prereqs(UnitTypeId.HumanTanker).Length);
            Assert.AreEqual(0, TechTree.Prereqs(UnitTypeId.ElvenDestroyer).Length);
            Assert.AreEqual(0, TechTree.Prereqs(UnitTypeId.TrollDestroyer).Length);
        }

        [Test]
        public void OilPlatform_HasNoPrereq()
        {
            // can_build_always in the original: owning a tanker to raise it
            // already implies a shipyard.
            Assert.AreEqual(0, TechTree.Prereqs(UnitTypeId.HumanOilWell).Length);
            Assert.AreEqual(0, TechTree.Prereqs(UnitTypeId.OrcOilWell).Length);
        }

        [Test]
        public void EveryNavalAndAirUnit_HasAProducer()
        {
            UnitTypeId[] roster =
            {
                UnitTypeId.HumanTanker, UnitTypeId.OrcTanker,
                UnitTypeId.HumanTransport, UnitTypeId.OrcTransport,
                UnitTypeId.ElvenDestroyer, UnitTypeId.TrollDestroyer,
                UnitTypeId.Battleship, UnitTypeId.Juggernaught,
                UnitTypeId.GnomishSubmarine, UnitTypeId.GiantTurtle,
                UnitTypeId.GnomishFlyingMachine, UnitTypeId.GoblinZeppelin,
                UnitTypeId.GryphonRider, UnitTypeId.Dragon,
            };

            foreach (var unit in roster)
            {
                bool produced = false;
                for (int b = 0; b < UdtaParser.UnitCount && !produced; b++)
                    produced = Lists(TechTree.Trains((UnitTypeId)b), unit);
                Assert.IsTrue(produced, $"{unit} is unbuildable — no building trains it");
            }
        }
    }
}
