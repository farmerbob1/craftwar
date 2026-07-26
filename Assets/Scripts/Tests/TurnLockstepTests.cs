using System.Collections.Generic;
using Craftwar.Net;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Turn scheduling and the host-relay protocol, exercised in-process. The
    /// load-bearing claim is the first test: at one tick per turn and one turn of
    /// delay, the turn driver behaves exactly like the local driver single player
    /// has always used — so the scheduling arithmetic is measured against
    /// behaviour that is already known good, before a socket exists.
    /// </summary>
    public class TurnLockstepTests
    {
        static GameCommand Cmd(byte player, CommandOp op, ushort param = 0) =>
            new GameCommand { Op = op, Player = player, Param = param };

        static TurnLockstepDriver LocalDriver(int ticksPerTurn, int delay, byte slot = 0) =>
            new TurnLockstepDriver(ticksPerTurn, delay, slot, new LocalTurnExchange());

        [Test]
        public void OneTickPerTurn_OneTurnDelay_MatchesTheLocalDriverExactly()
        {
            var local = new LocalLockstepDriver();
            var turned = LocalDriver(1, 1);

            var localGot = new List<GameCommand>();
            var turnedGot = new List<GameCommand>();

            // A submission schedule that mixes "before the first tick" with
            // "during a tick", which is where the two could differ.
            for (int tick = 0; tick < 20; tick++)
            {
                if (tick % 3 == 0)
                {
                    local.SubmitLocalCommand(Cmd(0, CommandOp.Move, (ushort)tick));
                    turned.SubmitLocalCommand(Cmd(0, CommandOp.Move, (ushort)tick));
                }

                Assert.IsTrue(local.TryGetTickCommands(tick, localGot));
                Assert.IsTrue(turned.TryGetTickCommands(tick, turnedGot),
                    $"the turn driver must never stall in local mode (tick {tick})");

                Assert.AreEqual(localGot.Count, turnedGot.Count, $"tick {tick} bundle size");
                for (int i = 0; i < localGot.Count; i++)
                    Assert.AreEqual(localGot[i].Param, turnedGot[i].Param, $"tick {tick} entry {i}");
            }
        }

        [Test]
        public void CommandIssuedDuringATurn_ExecutesExactlyInputDelayTurnsLater()
        {
            const int TicksPerTurn = 4, Delay = 2;
            var driver = LocalDriver(TicksPerTurn, Delay);
            var got = new List<GameCommand>();

            // Run turn 0 (ticks 0..3), issuing an order partway through it.
            for (int tick = 0; tick < TicksPerTurn; tick++)
            {
                Assert.IsTrue(driver.TryGetTickCommands(tick, got));
                if (tick == 1)
                    driver.SubmitLocalCommand(Cmd(0, CommandOp.Move, 99));
            }

            // Turn 1 must not carry it.
            for (int tick = TicksPerTurn; tick < 2 * TicksPerTurn; tick++)
            {
                Assert.IsTrue(driver.TryGetTickCommands(tick, got));
                Assert.AreEqual(0, got.Count, $"tick {tick} should be empty");
            }

            // Turn 2 = issued turn (0) + delay (2). It arrives on the turn's
            // first tick, and only there.
            Assert.IsTrue(driver.TryGetTickCommands(2 * TicksPerTurn, got));
            Assert.AreEqual(1, got.Count, "the order executes on the first tick of turn 2");
            Assert.AreEqual(99, got[0].Param);

            for (int tick = 2 * TicksPerTurn + 1; tick < 3 * TicksPerTurn; tick++)
            {
                Assert.IsTrue(driver.TryGetTickCommands(tick, got));
                Assert.AreEqual(0, got.Count, "the rest of the turn runs empty");
            }
        }

        [Test]
        public void MidTurnTicks_CannotRunAheadOfTheirTurn()
        {
            // Slot 1 never submits, so nothing can be committed.
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var driver = new TurnLockstepDriver(4, 2, 0, new LoopbackTurnExchange(relay, 0));
            var got = new List<GameCommand>();

            Assert.IsFalse(driver.TryGetTickCommands(0, got), "turn 0 is not committed");
            Assert.IsFalse(driver.TryGetTickCommands(1, got),
                "a mid-turn tick must not slip through while its turn is unconfirmed");
        }

        [Test]
        public void ATurnWaitsForEveryParticipant_ThenReleases()
        {
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var got = new List<GameCommand>();

            // Only slot 0 exists so far, so turn 0 is missing slot 1's input and
            // no amount of asking will release it.
            var a = new TurnLockstepDriver(4, 2, 0, new LoopbackTurnExchange(relay, 0));
            Assert.IsFalse(a.TryGetTickCommands(0, got), "turn 0 still waits on slot 1");
            Assert.AreEqual(NetStatus.Waiting, a.Status);
            Assert.IsFalse(a.TryGetTickCommands(0, got), "asking again changes nothing");

            // Slot 1 turning up completes the set, and the turn releases.
            var b = new TurnLockstepDriver(4, 2, 1, new LoopbackTurnExchange(relay, 1));
            Assert.IsTrue(a.TryGetTickCommands(0, got), "slot 1's input completes turn 0");
            Assert.AreEqual(NetStatus.Running, a.Status);
            Assert.IsTrue(b.TryGetTickCommands(0, got), "and the same bundle is there for slot 1");
        }

        [Test]
        public void CommittedBundle_IsGroupedByPlayer_RegardlessOfArrivalOrder()
        {
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var a = new TurnLockstepDriver(4, 2, 0, new LoopbackTurnExchange(relay, 0));
            var b = new TurnLockstepDriver(4, 2, 1, new LoopbackTurnExchange(relay, 1));
            var got = new List<GameCommand>();

            a.SubmitLocalCommand(Cmd(0, CommandOp.Build, 1));
            a.SubmitLocalCommand(Cmd(0, CommandOp.Move, 2));
            b.SubmitLocalCommand(Cmd(1, CommandOp.Train, 3));

            // Slot 1 publishes first each turn; the bundle must still come out
            // slot-ordered. Run both peers forward until the delayed commands
            // land.
            for (int turn = 0; turn < 6; turn++)
            {
                PumpTurn(b, got);
                var bundle = PumpTurn(a, got);
                if (bundle != null && bundle.Count > 0)
                    break;
            }

            Assert.AreEqual(3, got.Count);
            Assert.AreEqual(new byte[] { 0, 0, 1 }, new[] { got[0].Player, got[1].Player, got[2].Player });
            Assert.AreEqual(CommandOp.Build, got[0].Op, "a player's own submission order survives");
            Assert.AreEqual(CommandOp.Move, got[1].Op);
        }

        [Test]
        public void APeerMaySubmitCommandsOnlyForItsOwnSlot()
        {
            var relay = new TurnRelay(new byte[] { 0, 1 });
            var got = new List<GameCommand>();
            var forged = new List<GameCommand> { Cmd(1, CommandOp.Surrender) };

            relay.SubmitInput(0, 0, forged, -1, 0u);      // slot 0 speaking for slot 1
            relay.SubmitInput(1, 0, new List<GameCommand>(), -1, 0u);

            Assert.IsTrue(relay.TryGetCommitted(0, got));
            Assert.AreEqual(0, got.Count, "a command attributed to another slot is dropped, not executed");
        }

        [Test]
        public void MismatchedHashesForTheSameTurn_ReportADesync()
        {
            var relay = new TurnRelay(new byte[] { 0, 1 });
            DesyncReport? seen = null;
            relay.Desynced += r => seen = r;

            var none = new List<GameCommand>();
            relay.SubmitInput(0, 5, none, hashTurn: 3, stateHash: 0xAAAAAAAA);
            relay.SubmitInput(1, 5, none, hashTurn: 3, stateHash: 0xBBBBBBBB);

            Assert.IsTrue(seen.HasValue, "disagreeing hashes must be reported");
            Assert.AreEqual(3, seen.Value.Turn);
            Assert.AreEqual(0xAAAAAAAAu, seen.Value.ExpectedHash);
            Assert.AreEqual(0xBBBBBBBBu, seen.Value.ActualHash);
        }

        [Test]
        public void HashesTakenAtDifferentTurns_AreNotCompared()
        {
            // Peers under a pause execute different numbers of ticks per turn, so
            // a hash is only meaningful against one taken at the same point.
            var relay = new TurnRelay(new byte[] { 0, 1 });
            bool fired = false;
            relay.Desynced += _ => fired = true;

            var none = new List<GameCommand>();
            relay.SubmitInput(0, 5, none, hashTurn: 3, stateHash: 0xAAAAAAAA);
            relay.SubmitInput(1, 5, none, hashTurn: 4, stateHash: 0xBBBBBBBB);

            Assert.IsFalse(fired, "different hash turns are incomparable, not a desync");
        }

        [Test]
        public void TwoPeers_OverTheRelay_SimulateIdenticalWorlds()
        {
            // The end-to-end claim: two independent sims, each driven by its own
            // driver through the shared relay, stay bit-identical while both
            // players issue orders.
            const int TicksPerTurn = 4, Delay = 2, Ticks = 1200;

            var pud = AiTestHarness.TwoBaseMap();
            var simA = AiTestHarness.Boot(pud, seed: 4242);
            var simB = AiTestHarness.Boot(pud, seed: 4242);

            var relay = new TurnRelay(new byte[] { 0, 1 });
            var driverA = new TurnLockstepDriver(TicksPerTurn, Delay, 0, new LoopbackTurnExchange(relay, 0));
            var driverB = new TurnLockstepDriver(TicksPerTurn, Delay, 1, new LoopbackTurnExchange(relay, 1));

            var bundleA = new List<GameCommand>();
            var bundleB = new List<GameCommand>();

            for (int tick = 0; tick < Ticks; tick++)
            {
                // Each peer orders one of its own units around now and then.
                if (tick % 97 == 0)
                {
                    driverA.SubmitLocalCommand(MoveAnyUnit(simA, 0, (ushort)(10 + tick % 15), 12));
                    driverB.SubmitLocalCommand(MoveAnyUnit(simB, 1, (ushort)(40 + tick % 15), 40));
                }

                driverA.RecordTurnHash(tick / TicksPerTurn, simA.State.ComputeHash());
                driverB.RecordTurnHash(tick / TicksPerTurn, simB.State.ComputeHash());

                // Both peers must be fed before either can advance — that is
                // lockstep, and the relay enforces it.
                Assert.IsTrue(driverA.TryGetTickCommands(tick, bundleA) |
                              driverB.TryGetTickCommands(tick, bundleB),
                    $"neither peer could advance at tick {tick}");
                Assert.IsTrue(driverA.TryGetTickCommands(tick, bundleA), $"peer A stalled at tick {tick}");
                Assert.IsTrue(driverB.TryGetTickCommands(tick, bundleB), $"peer B stalled at tick {tick}");

                CollectionAssert.AreEqual(
                    Describe(bundleA), Describe(bundleB),
                    $"both peers must execute the identical bundle at tick {tick}");

                simA.Advance(bundleA);
                simB.Advance(bundleB);

                Assert.AreEqual(simA.State.ComputeHash(), simB.State.ComputeHash(),
                    $"worlds diverged at tick {tick}");
            }

            Assert.IsNull(simA.State.VerifyChecksums());
            Assert.IsNull(simB.State.VerifyChecksums());
        }

        // --- Pause ----------------------------------------------------------------

        [Test]
        public void APausedMatch_StopsTheSimClockButNotTheTurnClock()
        {
            // This is the whole reason the turn counter is not derived from the
            // tick: if a pause stopped the turn clock too, the Resume would have
            // nothing to travel in and the match could never restart.
            const int TicksPerTurn = 4;
            var driver = LocalDriver(TicksPerTurn, 1);
            var got = new List<GameCommand>();

            Assert.IsTrue(driver.TryGetTickCommands(0, got));
            driver.SubmitLocalCommand(Cmd(0, CommandOp.Pause));

            // Run out the current turn, then the pause takes effect.
            int tick = 1;
            for (; tick < 40 && !driver.IsPaused; tick++)
                driver.TryGetTickCommands(tick, got);

            Assert.IsTrue(driver.IsPaused, "the pause command should have been committed");
            int turnWhenPaused = driver.CurrentTurn;

            // The sim may not advance...
            for (int i = 0; i < 10; i++)
                Assert.IsFalse(driver.TryGetTickCommands(tick, got),
                    "no sim tick may execute while paused");

            // ...but turns must keep being agreed, or nothing can un-pause us.
            Assert.Greater(driver.CurrentTurn, turnWhenPaused,
                "the turn clock has to keep running so a Resume can reach us");
        }

        [Test]
        public void ResumeAfterAPause_LetsTheSimRunAgain()
        {
            var driver = LocalDriver(4, 1);
            var got = new List<GameCommand>();
            driver.TryGetTickCommands(0, got);

            driver.SubmitLocalCommand(Cmd(0, CommandOp.Pause));
            int tick = 1;
            for (; tick < 40 && !driver.IsPaused; tick++)
                driver.TryGetTickCommands(tick, got);
            Assert.IsTrue(driver.IsPaused);

            driver.SubmitLocalCommand(Cmd(0, CommandOp.Resume));
            for (int i = 0; i < 40 && driver.IsPaused; i++)
                driver.TryGetTickCommands(tick, got);

            Assert.IsFalse(driver.IsPaused, "the Resume travelled through the paused turns");
            Assert.IsTrue(driver.TryGetTickCommands(tick, got), "and the sim runs again");
        }

        [Test]
        public void TwoPlayersPausing_BothMustResume()
        {
            // A set, not a flag: if one player's Resume cleared someone else's
            // pause, a player could un-pause a match they never paused.
            var driver = LocalDriver(1, 1);
            var got = new List<GameCommand>();
            driver.TryGetTickCommands(0, got);

            driver.SubmitLocalCommand(Cmd(0, CommandOp.Pause));
            driver.SubmitLocalCommand(Cmd(1, CommandOp.Pause));
            for (int i = 1; i < 10 && !driver.IsPaused; i++)
                driver.TryGetTickCommands(i, got);
            Assert.IsTrue(driver.IsPaused);

            driver.SubmitLocalCommand(Cmd(0, CommandOp.Resume));
            for (int i = 0; i < 10; i++)
                driver.TryGetTickCommands(1, got);
            Assert.IsTrue(driver.IsPaused, "player 1 is still holding a pause");

            driver.SubmitLocalCommand(Cmd(1, CommandOp.Resume));
            for (int i = 0; i < 10 && driver.IsPaused; i++)
                driver.TryGetTickCommands(1, got);
            Assert.IsFalse(driver.IsPaused, "now both have released it");
        }

        [Test]
        public void ADroppedPlayersPause_CanBeReleased()
        {
            // Otherwise one peer vanishing mid-pause freezes the match forever.
            var driver = LocalDriver(1, 1);
            var got = new List<GameCommand>();
            driver.TryGetTickCommands(0, got);
            driver.SubmitLocalCommand(Cmd(3, CommandOp.Pause));
            for (int i = 1; i < 10 && !driver.IsPaused; i++)
                driver.TryGetTickCommands(i, got);
            Assert.IsTrue(driver.IsPaused);

            driver.ReleasePause(3);
            Assert.IsFalse(driver.IsPaused);
        }

        static List<string> Describe(List<GameCommand> commands)
        {
            var result = new List<string>(commands.Count);
            for (int i = 0; i < commands.Count; i++)
                result.Add($"{commands[i].Player}:{commands[i].Op}:{commands[i].TargetX},{commands[i].TargetY}");
            return result;
        }

        static unsafe GameCommand MoveAnyUnit(GameSim sim, byte player, ushort x, ushort y)
        {
            var cmd = new GameCommand
            {
                Op = CommandOp.Move,
                Player = player,
                TargetX = x,
                TargetY = y,
            };
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (!u.IsAlive || u.Player != player || (u.Flags & UnitFlags.Building) != 0)
                    continue;
                cmd.SelectionCount = 1;
                cmd.Selection.Ids[0] = new UnitId((ushort)i, u.Gen).Packed;
                break;
            }
            return cmd;
        }

        /// <summary>
        /// Drive one whole turn, returning the bundle delivered on its first
        /// tick (or null if the driver stalled). The tick argument no longer
        /// selects the turn — the driver tracks its own position, because a
        /// paused match advances turns without advancing ticks.
        /// </summary>
        static List<GameCommand> PumpTurn(TurnLockstepDriver driver, List<GameCommand> into)
        {
            if (!driver.TryGetTickCommands(0, into))
                return null;
            var scratch = new List<GameCommand>();
            for (int i = 1; i < driver.TicksPerTurn; i++)
                driver.TryGetTickCommands(0, scratch);
            return into;
        }
    }
}
