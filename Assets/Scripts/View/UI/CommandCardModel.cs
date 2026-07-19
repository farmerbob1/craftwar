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

        // Unit actions. Move/Attack/Patrol/Harvest/Repair need a world click;
        // Stop fires immediately.
        Move,
        Stop,
        Attack,
        Patrol,
        Harvest,
        Repair,
        Unload,           // transport puts its passengers ashore at a clicked tile

        // Page navigation on a worker's card.
        BuildBasicMenu,
        BuildAdvancedMenu,
        BackToActions,
    }

    /// <summary>Which face of the card a mobile selection is showing.</summary>
    public enum CardPage : byte { Actions = 0, BuildBasic, BuildAdvanced }

    public struct CommandSlot
    {
        public CommandSlotKind Kind;
        public ushort Param;            // UnitTypeId or UpgradeId, by Kind
        public int Gold, Lumber, Oil;
        public bool Enabled;            // affordability; recomputed every frame
        public int BuildingSlot;        // unit index that receives the command (-1 = worker placement)
        public string Label;            // button text + tooltip, baked at rebuild
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
        /// <summary>Slot 8 is the Back button on the build sub-pages.</summary>
        const int ToggleSlot = SlotCount - 1;

        public readonly CommandSlot[] Slots = new CommandSlot[SlotCount];

        /// <summary>Which face a mobile selection shows; reset on selection change.</summary>
        public CardPage Page;

        int _lastSelectionVersion = -1;

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
            h = h * 31 + (ulong)Page;
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
            // A new selection always lands on the action page — leaving a build
            // sub-page open across a selection change is disorienting.
            if (sel.Version != _lastSelectionVersion)
            {
                _lastSelectionVersion = sel.Version;
                Page = CardPage.Actions;
            }

            StructureHash = ComputeStructureHash(state, sel, player);
            for (int i = 0; i < SlotCount; i++)
            {
                Slots[i] = default;
                Slots[i].BuildingSlot = -1;
            }

            bool hasWorker = false, hasMobile = false, canAttack = false;
            bool hasTanker = false, hasTransport = false;
            int building = -1;
            foreach (uint packed in sel)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                ref var u = ref state.Units[idx];
                ref var row = ref state.Rules.Units[u.TypeId];
                if ((u.Flags & UnitFlags.Building) != 0)
                {
                    building = idx;
                    continue;
                }
                if (u.Player != player)
                    continue;
                hasMobile = true;
                if (row.Is(UnitTypeFlags.Peon))
                    hasWorker = true;
                if (row.Is(UnitTypeFlags.Tanker))
                    hasTanker = true;
                if (row.Is(UnitTypeFlags.Transport))
                    hasTransport = true;
                if (row.Is(UnitTypeFlags.CanAttack))
                    canAttack = true;
            }

            // Selection keeps buildings and units apart (see
            // WorldInputController.DropBuildings), so this is a guard against a
            // state that should not arise rather than a real tie-break.
            if (hasMobile)
            {
                if (Page != CardPage.Actions && (hasWorker || hasTanker))
                    BuildWorkerMenu(sim, state, player, hasTanker && !hasWorker);
                else
                    BuildActionMenu(hasWorker, canAttack, sim, state, player,
                        hasTanker, hasTransport);
                return;
            }
            if (building >= 0)
                BuildBuildingMenu(sim, state, building, player);
        }

        /// <summary>
        /// Fixed slot positions so the grid hotkeys stay stable per unit kind:
        /// Move/Stop/Attack/Patrol on the top rows, worker jobs below.
        /// </summary>
        void BuildActionMenu(bool hasWorker, bool canAttack,
            GameSim sim, GameState state, byte player,
            bool hasTanker = false, bool hasTransport = false)
        {
            Slots[0] = Action(CommandSlotKind.Move, "Move");
            Slots[1] = Action(CommandSlotKind.Stop, "Stop");
            if (canAttack)
                Slots[2] = Action(CommandSlotKind.Attack, "Attack");
            Slots[3] = Action(CommandSlotKind.Patrol, "Patrol");

            // A transport's only extra verb is putting its passengers ashore.
            if (hasTransport)
                Slots[8] = Action(CommandSlotKind.Unload, "Unload");

            if (hasTanker)
            {
                // Tankers pump oil and raise platforms; they have no other jobs.
                Slots[4] = Action(CommandSlotKind.Harvest, "Harvest Oil");
                if (CountTankerBuildable(sim, state, player) > 0)
                    Slots[6] = Action(CommandSlotKind.BuildBasicMenu, "Build");
            }

            if (!hasWorker)
                return;
            Slots[4] = Action(CommandSlotKind.Harvest, "Harvest");
            Slots[5] = Action(CommandSlotKind.Repair, "Repair");
            // Hide a page button with nothing behind it — early game the
            // advanced structures are all still gated.
            if (CountBuildable(sim, state, player, basic: true) > 0)
                Slots[6] = Action(CommandSlotKind.BuildBasicMenu, "Build");
            if (CountBuildable(sim, state, player, basic: false) > 0)
                Slots[7] = Action(CommandSlotKind.BuildAdvancedMenu, "Advanced");
        }

        static int CountTankerBuildable(GameSim sim, GameState state, byte player)
        {
            var menu = TechTree.TankerBuildings(state.Players[player].Race);
            int n = 0;
            for (int i = 0; i < menu.Length; i++)
                if (sim.CanProduce(player, menu[i]))
                    n++;
            return n;
        }

        static int CountBuildable(GameSim sim, GameState state, byte player, bool basic)
        {
            var menu = TechTree.WorkerBuildings(state.Players[player].Race);
            int lo = basic ? 0 : TechTree.BasicBuildingCount;
            int hi = basic ? TechTree.BasicBuildingCount : menu.Length;
            int n = 0;
            for (int i = lo; i < hi && i < menu.Length; i++)
                if (sim.CanProduce(player, menu[i]))
                    n++;
            return n;
        }

        static CommandSlot Action(CommandSlotKind kind, string label) =>
            new CommandSlot
            {
                Kind = kind,
                BuildingSlot = -1,
                Label = label,
                Enabled = true,
            };

        void BuildWorkerMenu(GameSim sim, GameState state, byte player, bool tanker = false)
        {
            // Basic vs Advanced is the original's split, not a page-size
            // artifact: TechTree orders the menu basic-first and marks the
            // boundary. Each page holds at most 8, leaving slot 8 for Back.
            var menu = tanker
                ? TechTree.TankerBuildings(state.Players[player].Race)
                : TechTree.WorkerBuildings(state.Players[player].Race);
            bool advanced = !tanker && Page == CardPage.BuildAdvanced;
            int lo = advanced ? TechTree.BasicBuildingCount : 0;
            int hi = tanker ? menu.Length
                : advanced ? menu.Length : TechTree.BasicBuildingCount;

            int n = 0;
            for (int i = lo; i < hi && i < menu.Length && n < _menuScratch.Length; i++)
                if (sim.CanProduce(player, menu[i]))
                    _menuScratch[n++] = menu[i];

            const int PerPage = SlotCount - 1;
            for (int s = 0; s < PerPage; s++)
            {
                if (s >= n)
                    break;
                var type = _menuScratch[s];
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
                };
            }

            Slots[ToggleSlot] = Action(CommandSlotKind.BackToActions, "Back");
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
                    // Free actions and navigation are always available; the sim
                    // has the final say on whether an order is legal.
                    case CommandSlotKind.None:
                    case CommandSlotKind.Cancel:
                    case CommandSlotKind.Move:
                    case CommandSlotKind.Stop:
                    case CommandSlotKind.Attack:
                    case CommandSlotKind.Patrol:
                    case CommandSlotKind.Harvest:
                    case CommandSlotKind.Repair:
                    case CommandSlotKind.Unload:
                    case CommandSlotKind.BuildBasicMenu:
                    case CommandSlotKind.BuildAdvancedMenu:
                    case CommandSlotKind.BackToActions:
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
