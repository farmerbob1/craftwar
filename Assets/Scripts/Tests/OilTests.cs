using System.Collections.Generic;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// The oil cycle: tanker raises a platform on a patch, pumps it, and unloads
    /// at a shipyard or refinery. Amounts are the original's (utype.h
    /// OIL_HARVEST 100, REFINERY_FACTOR 25).
    /// </summary>
    public class OilTests
    {
        const int PatchX = 20, PatchY = 6;

        /// <summary>
        /// Land on the left (x &lt; 10), coast at x == 10, open water to the right —
        /// so a shore building can berth at the coast and a platform sits offshore.
        /// </summary>
        static PudFile CoastMap()
        {
            var pud = new PudFile { Width = 32, Height = 32 };
            pud.Tiles = new ushort[32 * 32];
            pud.MoveMap = new ushort[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    int i = y * 32 + x;
                    pud.Tiles[i] = 0x0050;
                    pud.MoveMap[i] = x < 10 ? (ushort)0x0001   // land
                        : x == 10 ? (ushort)0x0082             // coast
                        : (ushort)0x0040;                      // water
                }
            pud.Owner[0] = (byte)PudOwner.Human;
            pud.StartGold[0] = 10000;
            pud.StartLumber[0] = 10000;
            pud.StartOil[0] = 10000;
            return pud;
        }

        static GameSim Boot(PudFile pud, ulong seed = 7)
        {
            var sim = new GameSim(seed);
            sim.Setup(pud, RuleSet.CreateDefault());
            return sim;
        }

        static unsafe GameCommand Cmd(GameSim sim, CommandOp op, int unitSlot,
            ushort tx = 0, ushort ty = 0, uint targetUnit = 0, ushort param = 0)
        {
            var cmd = new GameCommand
            {
                Op = op,
                Player = 0,
                TargetX = tx,
                TargetY = ty,
                TargetUnit = targetUnit,
                Param = param,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId((ushort)unitSlot, sim.State.Units[unitSlot].Gen).Packed;
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

        static int SlotOf(GameSim sim, UnitTypeId type)
        {
            for (int i = 0; i < sim.State.HighestUnitIndex; i++)
                if (sim.State.Units[i].IsAlive && sim.State.Units[i].TypeId == (ushort)type)
                    return i;
            return -1;
        }

        static uint Packed(GameSim sim, int slot) =>
            new UnitId((ushort)slot, sim.State.Units[slot].Gen).Packed;

        /// <summary>Drop a completed building straight into the world.</summary>
        static int Place(GameSim sim, UnitTypeId type, ushort x, ushort y, byte owner = 0)
        {
            var id = sim.State.SpawnUnit((ushort)type, owner, x, y);
            sim.State.TryGetUnitIndex(id, out int i);
            ref Unit u = ref sim.State.Units[i];
            u.Flags |= UnitFlags.Building;
            u.Hp = sim.State.Rules.Units[(ushort)type].Hp;
            return i;
        }

        // ---- placement rules -------------------------------------------------

        [Test]
        public void OilPlatform_RequiresAPatchUnderneath()
        {
            var pud = CoastMap();
            pud.Units.Add(new PudUnitEntry
            { X = PatchX, Y = PatchY, Type = (byte)UnitTypeId.OilPatch, Owner = 15, Alter = 4 });
            var sim = Boot(pud);
            ushort well = (ushort)UnitTypeId.HumanOilWell;

            Assert.AreEqual(SiteBlock.None,
                BuildSite.Check(sim.State, well, PatchX, PatchY, 0, out uint patch),
                "on the patch");
            Assert.AreNotEqual(0u, patch, "the patch is reported so it can be consumed");

            Assert.AreEqual(SiteBlock.NoOilPatch,
                BuildSite.Check(sim.State, well, PatchX, PatchY + 8, 0, out _),
                "open water with no patch");
        }

        [Test]
        public void OilPlatform_MayNotStraddleTheCoast()
        {
            var sim = Boot(CoastMap());
            // x == 10 is coast; a 3x3 footprint at x=10 covers it.
            Assert.AreEqual(SiteBlock.BadTerrain,
                BuildSite.Check(sim.State, (ushort)UnitTypeId.HumanOilWell, 10, 6, 0, out _));
        }

        [Test]
        public void ShoreBuilding_BerthsOnCoast_NotInland()
        {
            var sim = Boot(CoastMap());
            ushort yard = (ushort)UnitTypeId.HumanShipyard;

            Assert.AreEqual(SiteBlock.None,
                BuildSite.Check(sim.State, yard, 10, 6, 0, out _),
                "3x3 spanning coast + water is a valid berth");
            Assert.AreEqual(SiteBlock.BadTerrain,
                BuildSite.Check(sim.State, yard, 5, 6, 0, out _),
                "inland is not");
        }

        [Test]
        public void LandBuilding_StaysOnLand()
        {
            var sim = Boot(CoastMap());
            ushort farm = (ushort)UnitTypeId.Farm;
            Assert.AreEqual(SiteBlock.None, BuildSite.Check(sim.State, farm, 5, 6, 0, out _));
            Assert.AreEqual(SiteBlock.BadTerrain, BuildSite.Check(sim.State, farm, 20, 6, 0, out _),
                "a farm may not float");
        }

        // ---- the harvest cycle ----------------------------------------------

        /// <summary>Patch + platform + shipyard + a tanker, ready to pump.</summary>
        static GameSim RiggedMap(out int tanker, out int platform, bool withRefinery = false)
        {
            var pud = CoastMap();
            var sim = Boot(pud);

            platform = Place(sim, UnitTypeId.HumanOilWell, PatchX, PatchY);
            sim.State.Units[platform].ResourceAmount = 10000;
            Place(sim, UnitTypeId.HumanShipyard, 11, 12);
            if (withRefinery)
                Place(sim, UnitTypeId.HumanRefinery, 11, 20);

            var id = sim.State.SpawnUnit((ushort)UnitTypeId.HumanTanker, 0, 16, 10);
            sim.State.TryGetUnitIndex(id, out tanker);
            sim.State.Units[tanker].Hp = 100;
            return sim;
        }

        [Test]
        public void Tanker_PumpsOilAndUnloadsAtShipyard()
        {
            var sim = RiggedMap(out int tanker, out int platform);
            int before = sim.State.Players[0].Oil;

            Run(sim, 1200, Cmd(sim, CommandOp.Harvest, tanker,
                targetUnit: Packed(sim, platform)));

            Assert.Greater(sim.State.Players[0].Oil, before, "oil came in");
            Assert.AreEqual(0, (sim.State.Players[0].Oil - before) % SimConstants.OilPerTrip,
                "delivered in whole 100-oil loads");
            Assert.Less(sim.State.Units[platform].ResourceAmount, 10000, "the reserve drained");
        }

        [Test]
        public void RefineryBonus_IsPerPlayer_NotPerDropOffPoint()
        {
            // Same run, with and without a refinery standing. The tanker unloads
            // at the shipyard either way, so any difference is the player-wide
            // refinery bonus (HARVEST.C gwRefineryTbl).
            var plain = RiggedMap(out int t1, out int p1);
            Run(plain, 1200, Cmd(plain, CommandOp.Harvest, t1, targetUnit: Packed(plain, p1)));

            var refined = RiggedMap(out int t2, out int p2, withRefinery: true);
            Run(refined, 1200, Cmd(refined, CommandOp.Harvest, t2, targetUnit: Packed(refined, p2)));

            int plainOil = plain.State.Players[0].Oil;
            int refinedOil = refined.State.Players[0].Oil;
            Assert.Greater(refinedOil, plainOil, "the refinery pays a bonus");

            int trips = (plainOil - 10000) / SimConstants.OilPerTrip;
            Assert.Greater(trips, 0, "the plain run actually delivered something");
            int expected = 10000 + trips * (SimConstants.OilPerTrip
                + SimConstants.OilPerTrip * SimConstants.RefineryFactorPct / 100);
            Assert.AreEqual(expected, refinedOil, "125 per trip with a refinery");
        }

        [Test]
        public void Tanker_CannotBeOrderedToChopWood()
        {
            var sim = RiggedMap(out int tanker, out _);
            Run(sim, 5, Cmd(sim, CommandOp.Harvest, tanker, tx: 5, ty: 5));
            Assert.AreNotEqual(OrderType.Harvest, sim.State.Units[tanker].Order,
                "a tanker has no wood cycle");
        }

        [Test]
        public void Tanker_IgnoresAnOilPatchThatHasNoPlatformYet()
        {
            var pud = CoastMap();
            pud.Units.Add(new PudUnitEntry
            { X = PatchX, Y = PatchY, Type = (byte)UnitTypeId.OilPatch, Owner = 15, Alter = 4 });
            var sim = Boot(pud);
            var id = sim.State.SpawnUnit((ushort)UnitTypeId.HumanTanker, 0, 16, 10);
            sim.State.TryGetUnitIndex(id, out int tanker);
            int patch = SlotOf(sim, UnitTypeId.OilPatch);

            Run(sim, 5, Cmd(sim, CommandOp.Harvest, tanker, targetUnit: Packed(sim, patch)));
            Assert.AreNotEqual(OrderType.Harvest, sim.State.Units[tanker].Order,
                "the patch must be built on first");
        }

        [Test]
        public void PlatformRunsDry_AndIsRemoved()
        {
            var sim = RiggedMap(out int tanker, out int platform);
            sim.State.Units[platform].ResourceAmount = SimConstants.CarryAmount; // one load left

            Run(sim, 1200, Cmd(sim, CommandOp.Harvest, tanker,
                targetUnit: Packed(sim, platform)));

            Assert.IsFalse(sim.State.Units[platform].IsAlive, "a dry platform is removed");
        }

        // ---- playtest regressions -------------------------------------------

        [Test]
        public void Tanker_CanBeOrderedToRaiseAPlatform()
        {
            // Regression: the Build gate only consulted the *worker* menu, so a
            // tanker's oil platform came back TechUnavailable.
            var pud = CoastMap();
            pud.Units.Add(new PudUnitEntry
            { X = PatchX, Y = PatchY, Type = (byte)UnitTypeId.OilPatch, Owner = 15, Alter = 4 });
            var sim = Boot(pud);
            var id = sim.State.SpawnUnit((ushort)UnitTypeId.HumanTanker, 0, 16, 10);
            sim.State.TryGetUnitIndex(id, out int tanker);
            sim.State.Units[tanker].Hp = 90;

            Run(sim, 1, Cmd(sim, CommandOp.Build, tanker,
                tx: PatchX, ty: PatchY, param: (ushort)UnitTypeId.HumanOilWell));

            Assert.AreEqual(OrderType.Build, sim.State.Units[tanker].Order,
                "the tanker accepted the build order");
        }

        [Test]
        public void Peasant_CannotRaiseAPlatform()
        {
            var pud = CoastMap();
            pud.Units.Add(new PudUnitEntry
            { X = PatchX, Y = PatchY, Type = (byte)UnitTypeId.OilPatch, Owner = 15, Alter = 4 });
            var sim = Boot(pud);
            var id = sim.State.SpawnUnit((ushort)UnitTypeId.Peasant, 0, 4, 6);
            sim.State.TryGetUnitIndex(id, out int peasant);
            sim.State.Units[peasant].Hp = 90;

            Run(sim, 1, Cmd(sim, CommandOp.Build, peasant,
                tx: PatchX, ty: PatchY, param: (ushort)UnitTypeId.HumanOilWell));

            Assert.AreNotEqual(OrderType.Build, sim.State.Units[peasant].Order,
                "platforms are the tanker's job");
        }

        [Test]
        public void UnclaimedOilPatch_DoesNotBlockShips()
        {
            // Regression: the patch is flagged Building, so it walled off the
            // water around it — ships could not sail over an unbuilt patch.
            var pud = CoastMap();
            pud.Units.Add(new PudUnitEntry
            { X = 15, Y = 10, Type = (byte)UnitTypeId.OilPatch, Owner = 15, Alter = 4 });
            var sim = Boot(pud);

            Assert.IsTrue(
                sim.State.FootprintFree(UnitId.None, (ushort)UnitTypeId.HumanTanker, 16, 11),
                "the patch does not occupy the water");

            var pf = new Pathfinder(sim.State.Terrain, sim.State);
            var path = new ushort[sim.State.Terrain.Width * sim.State.Terrain.Height];
            int steps = pf.FindPath(MoveDomain.SeaDock, 1, 12, 11, 20, 11, path);
            Assert.Greater(steps, 0, "a route exists");
            Assert.AreEqual(20, path[steps - 1] % sim.State.Terrain.Width,
                "and it runs straight through the patch");
        }

        [Test]
        public void FinishedPlatform_DoesBlockShips()
        {
            var sim = Boot(CoastMap());
            Place(sim, UnitTypeId.HumanOilWell, 15, 10);
            Assert.IsFalse(
                sim.State.FootprintFree(UnitId.None, (ushort)UnitTypeId.HumanTanker, 16, 11),
                "once raised, the rig is solid");
        }

        [Test]
        public void PlatformSite_IsFoundByUnitScan_NotOccupancy()
        {
            // The patch is deliberately absent from the occupancy layer, so the
            // site check must locate it by scanning units.
            var pud = CoastMap();
            pud.Units.Add(new PudUnitEntry
            { X = PatchX, Y = PatchY, Type = (byte)UnitTypeId.OilPatch, Owner = 15, Alter = 4 });
            var sim = Boot(pud);

            Assert.AreEqual(SiteBlock.None,
                BuildSite.Check(sim.State, (ushort)UnitTypeId.HumanOilWell,
                    PatchX, PatchY, 0, out uint patch));
            Assert.AreNotEqual(0u, patch);
        }

        [Test]
        public void ClickNearAPatch_SnapsOntoIt()
        {
            // The ghost and the Build command share this, so a rig never needs
            // hand-alignment and the preview always sits on the real site.
            var pud = CoastMap();
            pud.Units.Add(new PudUnitEntry
            { X = PatchX, Y = PatchY, Type = (byte)UnitTypeId.OilPatch, Owner = 15, Alter = 4 });
            var sim = Boot(pud);

            Assert.IsTrue(BuildSite.TrySnapToPatch(sim.State, PatchX + 2, PatchY + 1, 3,
                out int sx, out int sy), "an overlapping click finds the patch");
            Assert.AreEqual(PatchX, sx);
            Assert.AreEqual(PatchY, sy);

            Assert.IsFalse(BuildSite.TrySnapToPatch(sim.State, PatchX + 9, PatchY, 3, out _, out _),
                "a click well clear of it does not");
        }

        [Test]
        public void OilCycle_IsDeterministic()
        {
            static uint RunOnce()
            {
                var sim = RiggedMap(out int tanker, out int platform);
                Run(sim, 1500, Cmd(sim, CommandOp.Harvest, tanker,
                    targetUnit: Packed(sim, platform)));
                return sim.State.ComputeHash();
            }
            Assert.AreEqual(RunOnce(), RunOnce());
        }
    }
}
