using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>Regression coverage for GameSim.Setup's per-unit spawn gate —
    /// distinct from VictoryTests' participation-predicate coverage, which
    /// exercises Controller/InGame for the victory evaluator, not spawning.</summary>
    public class GameSimSetupTests
    {
        static PudFile BaseMap()
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;
                pud.MoveMap[i] = 0x0001;
            }
            return pud;
        }

        static void Seat(PudFile pud, int slot, PudOwner owner, Race race = Race.Human)
        {
            pud.Owner[slot] = (byte)owner;
            pud.Side[slot] = (byte)race;
        }

        static void Place(PudFile pud, int slot, UnitTypeId type, int x, int y)
            => pud.Units.Add(new PudUnitEntry { X = (ushort)x, Y = (ushort)y, Type = (byte)type, Owner = (byte)slot });

        static MatchSetup CloseSeat(int seat)
        {
            var setup = new MatchSetup { Slots = new SlotSetup[SimConstants.MaxPlayers] };
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                setup.Slots[p] = new SlotSetup { Controller = Controller.Human, Race = Race.Human, Team = (byte)p };
            setup.Slots[seat] = new SlotSetup { Controller = Controller.None, Race = Race.Human, Team = (byte)seat };
            return setup;
        }

        [Test]
        public void LobbyClosedSeat_DoesNotSpawnItsStartingUnits()
        {
            // An 8-player map where the map itself marks every slot playable
            // (Human), but the lobby closed slot 1 — the exact shape of the
            // reported bug: a Closed seat still had Controller.None forced
            // through Setup while the PUD's own OWNR said "in game".
            var pud = BaseMap();
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                Seat(pud, p, PudOwner.Human);
            Place(pud, 0, UnitTypeId.TownHall, 5, 5);
            Place(pud, 1, UnitTypeId.TownHall, 10, 10);
            Place(pud, 1, UnitTypeId.Peasant, 11, 11);

            var sim = new GameSim(seed: 1);
            sim.Setup(pud, RuleSet.CreateDefault(), CloseSeat(1));

            Assert.AreEqual(Controller.None, sim.State.Players[1].Controller);
            Assert.IsTrue(sim.State.Players[1].InGame, "the map's own OWNR still marks the slot in-game");
            Assert.AreEqual(0, AiTestHarness.CountAlive(sim, 1, UnitTypeId.TownHall),
                "a lobby-closed seat must not spawn the map's starting units for it");
            Assert.AreEqual(0, AiTestHarness.CountAlive(sim, 1, UnitTypeId.Peasant));
            Assert.AreEqual(1, AiTestHarness.CountAlive(sim, 0, UnitTypeId.TownHall),
                "an unrelated open seat is unaffected");
        }

        [Test]
        public void PassiveAndRescueSlots_StillSpawnTheirUnits()
        {
            // The other half of the same gate: these owners are Controller.None
            // by design (scenery, see MatchSetup.ControllerFor) and must keep
            // spawning even though a closed Human/Computer seat with the same
            // Controller.None now does not.
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.PassiveComputer);
            Seat(pud, 2, PudOwner.RescuePassive);
            Seat(pud, 3, PudOwner.RescueActive);
            Place(pud, 0, UnitTypeId.TownHall, 5, 5);
            Place(pud, 1, UnitTypeId.Peasant, 10, 10);
            Place(pud, 2, UnitTypeId.Peasant, 12, 12);
            Place(pud, 3, UnitTypeId.Peasant, 14, 14);

            var sim = new GameSim(seed: 1);
            sim.Setup(pud, RuleSet.CreateDefault()); // map defaults, no lobby override

            Assert.AreEqual(1, AiTestHarness.CountAlive(sim, 1, UnitTypeId.Peasant), "passive-computer scenery still spawns");
            Assert.AreEqual(1, AiTestHarness.CountAlive(sim, 2, UnitTypeId.Peasant), "rescue-passive scenery still spawns");
            Assert.AreEqual(1, AiTestHarness.CountAlive(sim, 3, UnitTypeId.Peasant), "rescue-active scenery still spawns");
        }

        [Test]
        public void FullyResolvedMatch_SpawnsExactlyAsBefore()
        {
            // The common case the fix must not disturb: every slot claimed,
            // nothing closed.
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.TownHall, 5, 5);
            Place(pud, 1, UnitTypeId.GreatHall, 20, 20);

            var setup = new MatchSetup { Slots = new SlotSetup[SimConstants.MaxPlayers] };
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                setup.Slots[p] = new SlotSetup { Controller = Controller.None, Race = Race.Human, Team = (byte)p };
            setup.Slots[0] = new SlotSetup { Controller = Controller.Human, Race = Race.Human, Team = 0 };
            setup.Slots[1] = new SlotSetup { Controller = Controller.Computer, Race = Race.Orc, Team = 1 };

            var sim = new GameSim(seed: 1);
            sim.Setup(pud, RuleSet.CreateDefault(), setup);

            Assert.AreEqual(1, AiTestHarness.CountAlive(sim, 0, UnitTypeId.TownHall));
            Assert.AreEqual(1, AiTestHarness.CountAlive(sim, 1, UnitTypeId.GreatHall));
        }
    }
}
