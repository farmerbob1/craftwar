using System.Collections.Generic;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// Shared scaffolding for the M9 AI tests: a synthetic two-base melee map
    /// and a driver loop that thinks every computer slot in slot order before
    /// each Advance — the same shape GameLoopRunner uses in the app.
    /// </summary>
    static class AiTestHarness
    {
        /// <summary>
        /// 64x64 flat grass, two mirrored bases: hall + one worker each, a
        /// neutral gold mine and a forest block near each. Slot 0 human race,
        /// slot 1 orc, both Computer unless reseated by the caller.
        /// </summary>
        public static PudFile TwoBaseMap()
        {
            const int size = 64;
            var pud = new PudFile { Width = size, Height = size };
            pud.Tiles = new ushort[size * size];
            pud.MoveMap = new ushort[size * size];
            for (int i = 0; i < pud.MoveMap.Length; i++)
            {
                pud.Tiles[i] = 0x0050;  // grass
                pud.MoveMap[i] = 0x0001; // land-passable
            }
            Forest(pud, 2, 19, 14, 26);
            Forest(pud, 49, 37, 61, 44);

            Seat(pud, 0, PudOwner.Computer, Race.Human);
            Seat(pud, 1, PudOwner.Computer, Race.Orc);
            pud.StartGold[0] = pud.StartGold[1] = 2000;
            pud.StartLumber[0] = pud.StartLumber[1] = 1500;
            // Real melee maps carry SOIL; without it blacksmiths and hall
            // tiers (oil-costing) are unbuildable and both AIs cap out at
            // unupgraded footmen.
            pud.StartOil[0] = pud.StartOil[1] = 1000;

            Place(pud, 0, UnitTypeId.TownHall, 8, 8);
            Place(pud, 0, UnitTypeId.Peasant, 13, 12);
            pud.Units.Add(new PudUnitEntry
            {
                X = 18, Y = 6, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 25,
            });

            Place(pud, 1, UnitTypeId.GreatHall, 52, 52);
            Place(pud, 1, UnitTypeId.Peon, 48, 50);
            pud.Units.Add(new PudUnitEntry
            {
                X = 42, Y = 55, Type = (byte)UnitTypeId.GoldMine, Owner = 15, Alter = 25,
            });
            return pud;
        }

        public static void Forest(PudFile pud, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    pud.Tiles[y * pud.Width + x] = 0x0070;   // solid forest
                    pud.MoveMap[y * pud.Width + x] = 0x0081; // blocked by trees
                }
        }

        public static void Seat(PudFile pud, int slot, PudOwner owner, Race race)
        {
            pud.Owner[slot] = (byte)owner;
            pud.Side[slot] = (byte)race;
        }

        public static void Place(PudFile pud, int slot, UnitTypeId type, int x, int y) =>
            pud.Units.Add(new PudUnitEntry
            {
                X = (ushort)x, Y = (ushort)y, Type = (byte)type, Owner = (byte)slot,
            });

        public static GameSim Boot(PudFile pud, ulong seed)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
        }

        public static List<AiPlayer> CreateAis(GameSim sim)
        {
            var ais = new List<AiPlayer>();
            for (byte p = 0; p < SimConstants.MaxPlayers; p++)
                if (sim.State.Players[p].Controller == Controller.Computer
                    && sim.State.Players[p].InGame)
                    ais.Add(new AiPlayer(p, AiBehavior.LandAttack));
            return ais;
        }

        /// <summary>
        /// Drive an AI match: think all AIs in slot order, optionally record
        /// and observe, Advance. Stops early when `stop` returns true.
        /// </summary>
        public static int RunAiMatch(GameSim sim, List<AiPlayer> ais, int maxTicks,
            Replay replay = null,
            System.Action<int, GameCommand> onCommand = null,
            System.Func<GameSim, bool> stop = null)
        {
            var tickCommands = new List<GameCommand>();
            var buffer = new List<GameCommand>();
            int t = 0;
            for (; t < maxTicks; t++)
            {
                tickCommands.Clear();
                for (int a = 0; a < ais.Count; a++)
                {
                    buffer.Clear();
                    ais[a].Think(sim, buffer);
                    tickCommands.AddRange(buffer);
                }
                foreach (var cmd in tickCommands)
                {
                    replay?.Record(sim.State.Tick, cmd);
                    onCommand?.Invoke(sim.State.Tick, cmd);
                }
                sim.Advance(tickCommands);
                if (stop != null && stop(sim))
                    break;
            }
            return t;
        }

        /// <summary>Feed a recorded command log back with NO AIs constructed —
        /// how real replay playback works.</summary>
        public static GameSim Playback(PudFile pud, ulong seed, Replay replay, int maxTicks)
        {
            var sim = Boot(pud, seed);
            var tickCommands = new List<GameCommand>();
            int cursor = 0;
            for (int t = 0; t < maxTicks; t++)
            {
                tickCommands.Clear();
                while (cursor < replay.Entries.Count
                    && replay.Entries[cursor].tick == sim.State.Tick)
                    tickCommands.Add(replay.Entries[cursor++].cmd);
                sim.Advance(tickCommands);
            }
            return sim;
        }

        public static int CountAlive(GameSim sim, int slot, UnitTypeId type)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == slot && u.TypeId == (ushort)type)
                    n++;
            }
            return n;
        }

        public static int CountWorkersOnWood(GameSim sim, int slot)
        {
            int n = 0;
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
            {
                ref Unit u = ref sim.State.Units[i];
                if (u.IsAlive && u.Player == slot && u.Order == OrderType.Harvest
                    && (u.ResourceTarget & 0x80000000) != 0)
                    n++;
            }
            return n;
        }
    }
}
