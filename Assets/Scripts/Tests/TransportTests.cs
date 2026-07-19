using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Ferrying land units across water. The original caps a hold at
    /// MAX_MEN_IN_TRANSPORT (6) and only lets a transport disgorge against a
    /// SQ_SHORE tile (DISPATCH.C dispatch_unload_all).
    /// </summary>
    public class TransportTests
    {
        /// <summary>Land left of x=10, coast at x=10, water right — plus a far
        /// shore at x=28 so a crossing has somewhere to land.</summary>
        static PudFile StraitMap()
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    int i = y * 32 + x;
                    pud.Tiles[i] = 0x0050;
                    pud.MoveMap[i] =
                        x < 10 ? (ushort)0x0001 :
                        x == 10 ? (ushort)0x0082 :
                        x < 28 ? (ushort)0x0040 :
                        x == 28 ? (ushort)0x0082 :
                        (ushort)0x0001;
                }
            pud.Owner[0] = (byte)PudOwner.Human;
            pud.StartGold[0] = 10000;
            pud.StartLumber[0] = 10000;
            pud.StartOil[0] = 10000;
            return pud;
        }

        static GameSim Boot(PudFile pud, ulong seed = 11)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
        }

        static int Spawn(GameSim sim, UnitTypeId type, ushort x, ushort y, byte owner = 0)
        {
            var id = sim.State.SpawnUnit((ushort)type, owner, x, y);
            sim.State.TryGetUnitIndex(id, out int i);
            sim.State.Units[i].Hp = sim.State.Rules.Units[(ushort)type].Hp;
            return i;
        }

        static uint Packed(GameSim sim, int slot) =>
            new UnitId((ushort)slot, sim.State.Units[slot].Gen).Packed;

        static unsafe GameCommand Cmd(GameSim sim, CommandOp op, IReadOnlyList<int> slots,
            ushort tx = 0, ushort ty = 0, uint targetUnit = 0)
        {
            var cmd = new GameCommand
            {
                Op = op,
                Player = 0,
                TargetX = tx,
                TargetY = ty,
                TargetUnit = targetUnit,
                SelectionCount = (byte)slots.Count,
            };
            for (int i = 0; i < slots.Count; i++)
                cmd.Selection.Ids[i] = Packed(sim, slots[i]);
            return cmd;
        }

        static void Run(GameSim sim, int ticks, GameCommand? first = null)
        {
            var none = new List<GameCommand>();
            if (first.HasValue)
                sim.Advance(new List<GameCommand> { first.Value });
            for (int t = 0; t < ticks; t++)
                sim.Advance(none);
        }

        /// <summary>A transport idling just off the near coast, plus n footmen on land.</summary>
        static GameSim Loaded(int footmen, out int ship, out List<int> troops, int boardTicks = 900)
        {
            var sim = Boot(StraitMap());
            ship = Spawn(sim, UnitTypeId.HumanTransport, 10, 16); // docked on the near coast
            troops = new List<int>();
            for (int i = 0; i < footmen; i++)
                troops.Add(Spawn(sim, UnitTypeId.Footman, (ushort)(6 + i % 3), (ushort)(14 + i / 3)));

            Run(sim, boardTicks, Cmd(sim, CommandOp.Board, troops, targetUnit: Packed(sim, ship)));
            return sim;
        }

        static int Aboard(GameSim sim, int ship)
        {
            uint id = Packed(sim, ship);
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].Transport == id)
                    n++;
            return n;
        }

        [Test]
        public void Footmen_BoardATransport()
        {
            var sim = Loaded(3, out int ship, out var troops);

            Assert.AreEqual(3, Aboard(sim, ship), "all three climbed in");
            Assert.AreEqual(3, sim.State.Units[ship].CargoCount, "the carrier counted them");
            foreach (int t in troops)
                Assert.AreNotEqual(0, (int)(sim.State.Units[t].Flags & UnitFlags.Hidden),
                    "passengers ride hidden");
        }

        [Test]
        public void Hold_IsCappedAtSix()
        {
            var sim = Loaded(8, out int ship, out _);
            Assert.AreEqual(GameSim.TransportCapacity, Aboard(sim, ship), "six aboard, no more");
            Assert.AreEqual(GameSim.TransportCapacity, sim.State.Units[ship].CargoCount);
        }

        [Test]
        public void Unload_PutsTroopsAshoreAtTheFarCoast()
        {
            var sim = Loaded(3, out int ship, out var troops);
            Assert.AreEqual(3, Aboard(sim, ship), "precondition: loaded");

            // Sail to the far bank and disembark.
            Run(sim, 900, Cmd(sim, CommandOp.Move, new[] { ship }, tx: 28, ty: 16));
            Run(sim, 200, Cmd(sim, CommandOp.Unload, new[] { ship }, tx: 28, ty: 16));

            Assert.AreEqual(0, Aboard(sim, ship), "the hold emptied");
            Assert.AreEqual(0, sim.State.Units[ship].CargoCount);
            foreach (int t in troops)
            {
                ref Unit p = ref sim.State.Units[t];
                Assert.AreEqual(UnitFlags.None, p.Flags & UnitFlags.Hidden, "back in the world");
                Assert.Greater(p.TileX, 20, "landed on the far side, not where they embarked");
                Assert.IsTrue(sim.State.Terrain.IsPassable(MoveDomain.Land, p.TileX, p.TileY),
                    "and on ground they can stand on");
            }
        }

        [Test]
        public void Unload_InOpenWaterIsRefused()
        {
            var sim = Loaded(3, out int ship, out _);
            // Mid-strait: no shore within reach.
            Run(sim, 600, Cmd(sim, CommandOp.Move, new[] { ship }, tx: 19, ty: 16));
            Run(sim, 100, Cmd(sim, CommandOp.Unload, new[] { ship }, tx: 19, ty: 16));

            Assert.AreEqual(3, Aboard(sim, ship), "nobody walks on water");
        }

        [Test]
        public void SinkingTransport_DrownsItsCargo()
        {
            var sim = Loaded(3, out int ship, out var troops);
            Assert.AreEqual(3, Aboard(sim, ship), "precondition: loaded");

            ref Unit s = ref sim.State.Units[ship];
            s.Hp = 1;
            var destroyer = Spawn(sim, UnitTypeId.ElvenDestroyer, 14, 16, owner: 1);
            sim.State.Units[destroyer].Player = 1;

            // Kill the transport outright rather than wait on combat pathing.
            sim.State.Units[ship].Hp = 0;
            Run(sim, 1, Cmd(sim, CommandOp.Attack, new[] { destroyer },
                targetUnit: Packed(sim, ship)));
            Run(sim, 200);

            Assert.IsFalse(sim.State.Units[ship].IsAlive, "the transport went down");
            foreach (int t in troops)
                Assert.IsFalse(sim.State.Units[t].IsAlive, "and so did the men aboard");
        }

        [Test]
        public void Boarding_GivesUpIfTheTransportNeverDocks()
        {
            // Coast is not land-passable, so troops can never reach a transport
            // idling in open water — they must not mill on the bank forever.
            var sim = Boot(StraitMap());
            int ship = Spawn(sim, UnitTypeId.HumanTransport, 15, 16); // mid-strait
            var troops = new List<int> { Spawn(sim, UnitTypeId.Footman, 6, 16) };

            Run(sim, 400, Cmd(sim, CommandOp.Board, troops, targetUnit: Packed(sim, ship)));

            Assert.AreEqual(0, Aboard(sim, ship));
            Assert.AreEqual(OrderType.None, sim.State.Units[troops[0]].Order,
                "the order was abandoned rather than retried forever");
        }

        [Test]
        public void Ships_CannotBoardATransport()
        {
            var sim = Boot(StraitMap());
            int ship = Spawn(sim, UnitTypeId.HumanTransport, 10, 16); // docked on the near coast
            int tanker = Spawn(sim, UnitTypeId.HumanTanker, 13, 16);

            Run(sim, 300, Cmd(sim, CommandOp.Board, new[] { tanker },
                targetUnit: Packed(sim, ship)));

            Assert.AreEqual(0, Aboard(sim, ship), "only ground units ride");
        }

        [Test]
        public void FerryCycle_IsDeterministic()
        {
            static uint RunOnce()
            {
                var sim = Loaded(4, out int ship, out _);
                Run(sim, 900, Cmd(sim, CommandOp.Move, new[] { ship }, tx: 28, ty: 16));
                Run(sim, 300, Cmd(sim, CommandOp.Unload, new[] { ship }, tx: 28, ty: 16));
                return sim.State.ComputeHash();
            }
            Assert.AreEqual(RunOnce(), RunOnce());
        }
    }
}
