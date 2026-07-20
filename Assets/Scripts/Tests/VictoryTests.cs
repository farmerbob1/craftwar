using System.Collections.Generic;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class VictoryTests
    {
        /// <summary>Open 32x32 field, no starting units — callers add their own.</summary>
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

        static GameSim Boot(PudFile pud, MatchSetup? setup = null, ulong seed = 7)
        {
            var sim = new GameSim(seed);
            if (setup.HasValue)
                sim.Setup(pud, RuleSet.CreateDefault(), setup.Value);
            else
                sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
        }

        static void Run(GameSim sim, int ticks)
        {
            var none = new List<GameCommand>();
            for (int t = 0; t < ticks; t++)
                sim.Advance(none);
        }

        /// <summary>Kill every unit belonging to a slot, the way combat would.</summary>
        static void WipeSlot(GameSim sim, int slot)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if ((u.Flags & UnitFlags.Alive) != 0 && u.Player == slot)
                    sim.State.DestroyUnit(new UnitId((ushort)i, u.Gen));
            }
        }

        static MatchSetup Teams(params (int slot, Controller c, byte team)[] rows)
        {
            var setup = new MatchSetup { Slots = new SlotSetup[SimConstants.MaxPlayers] };
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                setup.Slots[p] = new SlotSetup { Controller = Controller.None, Race = Race.Human, Team = (byte)p };
            foreach (var (slot, c, team) in rows)
                setup.Slots[slot] = new SlotSetup { Controller = c, Race = Race.Human, Team = team };
            return setup;
        }

        // ---------- free-for-all ----------

        [Test]
        public void LastPlayerStanding_WinsAndLoser_IsDefeated()
        {
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.Grunt, 20, 20);

            var sim = Boot(pud);
            Run(sim, SimConstants.VictoryCheckTicks + 1);
            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[0].Outcome, "nobody has lost yet");
            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[1].Outcome);

            WipeSlot(sim, 1);
            Run(sim, SimConstants.VictoryCheckTicks + 1);

            Assert.AreEqual(PlayerOutcome.Defeated, sim.State.Players[1].Outcome);
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[0].Outcome);
        }

        [Test]
        public void BuildingsAloneKeepAPlayerAlive()
        {
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.TownHall, 20, 20); // a building is still a unit

            var sim = Boot(pud);
            Run(sim, SimConstants.VictoryCheckTicks + 1);

            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[1].Outcome,
                "a player with only buildings has not lost");
            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[0].Outcome);
        }

        [Test]
        public void LonePeasantIsNotDefeated()
        {
            // Faithful stall: no gold, no army, but still in the game.
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.Peasant, 20, 20);

            var sim = Boot(pud);
            Run(sim, SimConstants.VictoryCheckTicks * 3);

            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[1].Outcome);
            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[0].Outcome);
        }

        // ---------- participation predicate ----------

        [Test]
        public void PassiveAndRescueSlots_DoNotBlockVictory()
        {
            // The regression this guards: these owners are InGame (their units
            // spawn) but are not opponents. Keying victory off InGame would leave
            // slot 0 Playing forever.
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.PassiveComputer);
            Seat(pud, 2, PudOwner.RescuePassive);
            Seat(pud, 3, PudOwner.RescueActive);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.Peasant, 10, 10);
            Place(pud, 2, UnitTypeId.Peasant, 12, 12);
            Place(pud, 3, UnitTypeId.Peasant, 14, 14);

            var sim = Boot(pud);
            Assert.IsTrue(sim.State.Players[1].InGame, "passive slots still spawn units");
            Assert.AreEqual(Controller.None, sim.State.Players[1].Controller);

            Run(sim, SimConstants.VictoryCheckTicks + 1);
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[0].Outcome);
        }

        [Test]
        public void NeutralResourcesAndCritters_DoNotCount()
        {
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.Grunt, 20, 20);
            pud.Units.Add(new PudUnitEntry { X = 25, Y = 25, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 4 });

            var sim = Boot(pud);
            WipeSlot(sim, 1);
            Run(sim, SimConstants.VictoryCheckTicks + 1);

            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[0].Outcome,
                "a neutral gold mine is not an opponent");
        }

        // ---------- teams ----------

        [Test]
        public void TeamMelee_DefeatRequiresBothEnemiesDown()
        {
            var pud = BaseMap();
            for (int p = 0; p < 4; p++)
            {
                Seat(pud, p, p == 0 ? PudOwner.Human : PudOwner.Computer);
                Place(pud, p, UnitTypeId.Footman, 4 + p * 4, 4 + p * 4);
            }

            // 0+1 versus 2+3.
            var sim = Boot(pud, Teams(
                (0, Controller.Human, 0), (1, Controller.Computer, 0),
                (2, Controller.Computer, 1), (3, Controller.Computer, 1)));

            WipeSlot(sim, 2);
            Run(sim, SimConstants.VictoryCheckTicks + 1);
            Assert.AreEqual(PlayerOutcome.Defeated, sim.State.Players[2].Outcome);
            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[0].Outcome,
                "slot 3 is still alive on the enemy team");

            WipeSlot(sim, 3);
            Run(sim, SimConstants.VictoryCheckTicks + 1);
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[0].Outcome);
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[1].Outcome,
                "both surviving allies win");
        }

        [Test]
        public void AllyDeath_DoesNotWinTheGame()
        {
            var pud = BaseMap();
            for (int p = 0; p < 3; p++)
            {
                Seat(pud, p, p == 0 ? PudOwner.Human : PudOwner.Computer);
                Place(pud, p, UnitTypeId.Footman, 4 + p * 4, 4 + p * 4);
            }

            var sim = Boot(pud, Teams(
                (0, Controller.Human, 0), (1, Controller.Computer, 0),
                (2, Controller.Computer, 1)));

            WipeSlot(sim, 1); // our own ally dies
            Run(sim, SimConstants.VictoryCheckTicks + 1);

            Assert.AreEqual(PlayerOutcome.Defeated, sim.State.Players[1].Outcome);
            Assert.AreEqual(PlayerOutcome.Playing, sim.State.Players[0].Outcome);
        }

        // ---------- surrender ----------

        [Test]
        public void Surrender_DefeatsTheConceder_AndLetsTheEnemyWin()
        {
            // The point of the feature: the conceder still has a full army, so
            // a units-only test would leave the match unresolvable forever.
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 0, UnitTypeId.TownHall, 8, 8);
            Place(pud, 1, UnitTypeId.Grunt, 20, 20);

            var sim = Boot(pud);
            sim.Advance(new List<GameCommand>
            {
                new GameCommand { Op = CommandOp.Surrender, Player = 0, SelectionCount = 0 },
            });

            Assert.AreEqual(PlayerOutcome.Defeated, sim.State.Players[0].Outcome,
                "surrender resolves immediately, not on the next victory check");

            Run(sim, SimConstants.VictoryCheckTicks + 1);
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[1].Outcome,
                "the surviving army must not keep the match alive");
        }

        [Test]
        public void Surrender_IsAnnouncedOnce_AndCannotBeRepeated()
        {
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.Grunt, 20, 20);

            var sim = Boot(pud);
            var surrender = new List<GameCommand>
            {
                new GameCommand { Op = CommandOp.Surrender, Player = 0, SelectionCount = 0 },
            };

            sim.Advance(surrender);
            int first = CountDefeats(sim);
            sim.Advance(surrender); // a second click, or a duplicated packet
            int second = CountDefeats(sim);

            Assert.AreEqual(1, first);
            Assert.AreEqual(0, second, "already-defeated players must not re-announce");
        }

        static int CountDefeats(GameSim sim)
        {
            int n = 0;
            foreach (var e in sim.State.Events)
                if (e.Kind == SimEventKind.PlayerDefeated) n++;
            return n;
        }

        // ---------- events, latching, determinism ----------

        [Test]
        public void OutcomeIsLatched_AndEventsFireExactlyOnce()
        {
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.Grunt, 20, 20);

            var sim = Boot(pud);
            WipeSlot(sim, 1);

            int defeats = 0, victories = 0;
            var none = new List<GameCommand>();
            for (int t = 0; t < SimConstants.VictoryCheckTicks * 4; t++)
            {
                sim.Advance(none);
                foreach (var e in sim.State.Events)
                {
                    if (e.Kind == SimEventKind.PlayerDefeated) defeats++;
                    if (e.Kind == SimEventKind.PlayerVictorious) victories++;
                }
            }

            Assert.AreEqual(1, defeats, "defeat announced once, not every check");
            Assert.AreEqual(1, victories);
        }

        [Test]
        public void MatchThroughVictory_IsHashIdenticalAcrossRuns()
        {
            uint Play()
            {
                var pud = BaseMap();
                Seat(pud, 0, PudOwner.Human);
                Seat(pud, 1, PudOwner.Computer);
                Place(pud, 0, UnitTypeId.Footman, 5, 5);
                Place(pud, 0, UnitTypeId.Peasant, 6, 6);
                Place(pud, 1, UnitTypeId.Grunt, 20, 20);

                var sim = Boot(pud);
                Run(sim, SimConstants.VictoryCheckTicks);
                WipeSlot(sim, 1);
                Run(sim, SimConstants.VictoryCheckTicks * 3);
                return sim.State.ComputeHash();
            }

            Assert.AreEqual(Play(), Play());
        }

        [Test]
        public void OutcomeIsHashed()
        {
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Place(pud, 0, UnitTypeId.Footman, 5, 5);
            Place(pud, 1, UnitTypeId.Grunt, 20, 20);

            var sim = Boot(pud);
            uint before = sim.State.ComputeHash();
            sim.State.Players[1].Outcome = PlayerOutcome.Defeated;
            Assert.AreNotEqual(before, sim.State.ComputeHash(),
                "PlayerState.Outcome must reach the hash or it can desync silently");
        }

        [Test]
        public void SetupFromPud_PreservesHumanComputerDistinction()
        {
            var pud = BaseMap();
            Seat(pud, 0, PudOwner.Human);
            Seat(pud, 1, PudOwner.Computer);
            Seat(pud, 2, PudOwner.PassiveComputer);
            Seat(pud, 3, PudOwner.Nobody);

            var sim = Boot(pud);
            Assert.AreEqual(Controller.Human, sim.State.Players[0].Controller);
            Assert.AreEqual(Controller.Computer, sim.State.Players[1].Controller);
            Assert.AreEqual(Controller.None, sim.State.Players[2].Controller);
            Assert.AreEqual(Controller.None, sim.State.Players[3].Controller);
            Assert.IsFalse(sim.State.Players[3].InGame);
        }
    }
}
