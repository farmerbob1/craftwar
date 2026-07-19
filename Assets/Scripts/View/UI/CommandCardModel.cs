using Craftwar.Sim;

namespace Craftwar.View
{
    public enum CommandSlotKind : byte
    {
        None = 0,
        Build,            // worker places a building; Param = UnitTypeId
        Train,            // building trains a unit; Param = UnitTypeId
        UpgradeTo,        // building swaps to its next tier; Param = UnitTypeId
        Research,         // building researches; Param = UpgradeId
        Cancel,           // busy building aborts with a full refund
        BuildMenuToggle,  // worker Basic <-> Advanced page
    }

    public struct CommandSlot
    {
        public CommandSlotKind Kind;
        public ushort Param;            // UnitTypeId or UpgradeId, by Kind
        public int Gold, Lumber, Oil;
        public bool Enabled;            // affordability; recomputed every frame
        public int BuildingSlot;        // unit index that receives the command (-1 = worker placement)
        public string Label;            // display name, baked at rebuild
        public string Initials;         // placeholder icon text, baked at rebuild
    }

    /// <summary>
    /// The command card's contents as plain data — no VisualElements, so it is
    /// unit-testable and cheap to diff. Ports the gating logic that used to
    /// live in HudController.DrawCommandCard: worker -> build menu gated by
    /// CanProduce; building -> Trains (+TrainSubstitute) / UpgradesTo /
    /// Research, each gated by the matching GameSim.Can* rule.
    /// </summary>
    public sealed class CommandCardModel
    {
        public const int SlotCount = 9;
        /// <summary>Slot 8 becomes the page toggle when the menu overflows.</summary>
        const int ToggleSlot = SlotCount - 1;

        public readonly CommandSlot[] Slots = new CommandSlot[SlotCount];

        /// <summary>Worker build menu is paged; WC2 splits Basic/Advanced too.</summary>
        public bool AdvancedPage;

        /// <summary>True while the current card is a worker's build menu.</summary>
        public bool IsBuildMenu { get; private set; }

        /// <summary>Set when the menu needs a second page.</summary>
        public bool HasSecondPage { get; private set; }

        /// <summary>
        /// Everything that changes the card's *shape*. Deliberately excludes
        /// TrainTicks — a ticking progress bar must not trigger a rebuild.
        /// </summary>
        public ulong StructureHash { get; private set; }

        readonly UnitTypeId[] _menuScratch = new UnitTypeId[32];

        public ulong ComputeStructureHash(GameState state, SelectionState sel, byte player)
        {
            ulong h = (ulong)sel.Version * 0x9E3779B97F4A7C15ul;
            h ^= state.Players[player].Researched;
            h = h * 31 + (AdvancedPage ? 1ul : 0ul);
            foreach (uint packed in sel)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                ref var u = ref state.Units[idx];
                ulong f = (ulong)(u.Flags & (UnitFlags.Building | UnitFlags.UnderConstruction));
                h = h * 1099511628211ul
                    + packed
                    + (f << 32)
                    + ((ulong)u.BuildType << 40)
                    + ((ulong)u.ResearchId << 52);
            }
            return h;
        }

        public void Rebuild(GameSim sim, GameState state, SelectionState sel, byte player)
        {
            StructureHash = ComputeStructureHash(state, sel, player);
            for (int i = 0; i < SlotCount; i++)
                Slots[i] = default;
            for (int i = 0; i < SlotCount; i++)
                Slots[i].BuildingSlot = -1;
            IsBuildMenu = false;
            HasSecondPage = false;

            bool hasWorker = false;
            int building = -1;
            foreach (uint packed in sel)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                ref var u = ref state.Units[idx];
                if (state.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                    hasWorker = true;
                if ((u.Flags & UnitFlags.Building) != 0)
                    building = idx;
            }

            if (hasWorker)
            {
                BuildWorkerMenu(sim, state, player);
                return;
            }
            if (building >= 0)
                BuildBuildingMenu(sim, state, building, player);
        }

        void BuildWorkerMenu(GameSim sim, GameState state, byte player)
        {
            IsBuildMenu = true;
            var menu = TechTree.WorkerBuildings(state.Players[player].Race);
            int n = 0;
            for (int i = 0; i < menu.Length && n < _menuScratch.Length; i++)
                if (sim.CanProduce(player, menu[i]))
                    _menuScratch[n++] = menu[i];

            // Everything fits: one page, no toggle. Otherwise 8 per page with
            // the toggle parked in the last slot.
            int perPage = n > SlotCount ? SlotCount - 1 : SlotCount;
            HasSecondPage = n > SlotCount;
            if (!HasSecondPage)
                AdvancedPage = false;

            int start = AdvancedPage ? perPage : 0;
            for (int s = 0; s < perPage; s++)
            {
                int src = start + s;
                if (src >= n)
                    break;
                var type = _menuScratch[src];
                ref var row = ref state.Rules.Units[(int)type];
                Slots[s] = new CommandSlot
                {
                    Kind = CommandSlotKind.Build,
                    Param = (ushort)type,
                    Gold = row.GoldCost,
                    Lumber = row.LumberCost,
                    Oil = row.OilCost,
                    BuildingSlot = -1,
                    Label = UnitNames.Of(type),
                    Initials = UnitNames.InitialsOf(type),
                };
            }

            if (HasSecondPage)
                Slots[ToggleSlot] = new CommandSlot
                {
                    Kind = CommandSlotKind.BuildMenuToggle,
                    BuildingSlot = -1,
                    Label = AdvancedPage ? "Basic" : "Advanced",
                    Initials = AdvancedPage ? "<<" : ">>",
                    Enabled = true,
                };
        }

        void BuildBuildingMenu(GameSim sim, GameState state, int building, byte player)
        {
            ref var bld = ref state.Units[building];
            var bType = (UnitTypeId)bld.TypeId;

            // Busy (constructing / training / researching): the only action is Cancel.
            if ((bld.Flags & UnitFlags.UnderConstruction) != 0
                || bld.BuildType != 0 || bld.ResearchId != 0)
            {
                Slots[0] = new CommandSlot
                {
                    Kind = CommandSlotKind.Cancel,
                    BuildingSlot = building,
                    Label = "Cancel",
                    Initials = "X",
                    Enabled = true,
                };
                return;
            }

            int s = 0;
            ulong researched = state.Players[player].Researched;

            foreach (var baseType in TechTree.Trains(bType))
            {
                if (s >= SlotCount)
                    break;
                var t = TechTree.TrainSubstitute(baseType, researched);
                if (!sim.CanTrainAt(player, bType, t))
                    continue;
                ref var row = ref state.Rules.Units[(int)t];
                Slots[s++] = new CommandSlot
                {
                    Kind = CommandSlotKind.Train,
                    Param = (ushort)t,
                    Gold = row.GoldCost,
                    Lumber = row.LumberCost,
                    Oil = row.OilCost,
                    BuildingSlot = building,
                    Label = UnitNames.Of(t),
                    Initials = UnitNames.InitialsOf(t),
                };
            }

            foreach (var target in TechTree.UpgradesTo(bType))
            {
                if (s >= SlotCount)
                    break;
                if (!sim.CanUpgradeBuildingTo(player, bType, target))
                    continue;
                ref var row = ref state.Rules.Units[(int)target];
                Slots[s++] = new CommandSlot
                {
                    Kind = CommandSlotKind.UpgradeTo,
                    Param = (ushort)target,
                    Gold = row.GoldCost,
                    Lumber = row.LumberCost,
                    Oil = row.OilCost,
                    BuildingSlot = building,
                    Label = UnitNames.Of(target),
                    Initials = UnitNames.InitialsOf(target),
                };
            }

            foreach (var up in TechTree.Research(bType))
            {
                if (s >= SlotCount)
                    break;
                if (!sim.CanResearchAt(player, bType, up))
                    continue;
                ref var row = ref state.Rules.Upgrades[(int)up];
                Slots[s++] = new CommandSlot
                {
                    Kind = CommandSlotKind.Research,
                    Param = (ushort)up,
                    Gold = row.Gold,
                    Lumber = row.Lumber,
                    Oil = row.Oil,
                    BuildingSlot = building,
                    Label = UnitNames.Of(up),
                    Initials = UnitNames.InitialsOf(up),
                };
            }
        }

        /// <summary>Affordability only — int compares, safe to run every frame.</summary>
        public void RefreshEnabled(GameState state, byte player)
        {
            ref var p = ref state.Players[player];
            for (int i = 0; i < SlotCount; i++)
            {
                ref var slot = ref Slots[i];
                switch (slot.Kind)
                {
                    case CommandSlotKind.None:
                    case CommandSlotKind.Cancel:
                    case CommandSlotKind.BuildMenuToggle:
                        continue;
                    case CommandSlotKind.Train:
                        // Buildings train into the food cap; tier upgrades don't.
                        slot.Enabled = p.Gold >= slot.Gold && p.Lumber >= slot.Lumber
                            && p.Oil >= slot.Oil && p.FoodUsed + 1 <= p.FoodMax;
                        break;
                    default:
                        slot.Enabled = p.Gold >= slot.Gold && p.Lumber >= slot.Lumber
                            && p.Oil >= slot.Oil;
                        break;
                }
            }
        }
    }
}
