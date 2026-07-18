using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// Command card + resource bar v1 (IMGUI; replaced by the real WC2 panel
    /// art at M8). Contextual: peasants get build buttons, production
    /// buildings get train buttons. Placement mode hands off to
    /// SelectionController via PendingBuildType.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        const byte LocalPlayer = 0;

        ISimHost _host;
        UnitViewPool _pool;

        /// <summary>Nonzero while the player is placing a building.</summary>
        public ushort PendingBuildType { get; set; }

        public void Init(ISimHost host, UnitViewPool pool)
        {
            _host = host;
            _pool = pool;
        }

        void OnGUI()
        {
            if (_host?.Sim == null)
                return;
            var state = _host.Sim.State;
            ref var p = ref state.Players[LocalPlayer];

            GUI.Box(new Rect(0, 0, Screen.width, 24), GUIContent.none);
            GUI.Label(new Rect(8, 3, Screen.width - 16, 20),
                $"Gold {p.Gold}    Lumber {p.Lumber}    Oil {p.Oil}    Food {p.FoodUsed}/{p.FoodMax}" +
                (PendingBuildType != 0 ? "      [click to place, right-click to cancel]" : ""));

            DrawCommandCard(state);
        }

        float _btnX, _btnY;

        void DrawCommandCard(GameState state)
        {
            var sim = _host.Sim;
            bool hasWorker = false;
            int building = -1;
            foreach (uint packed in _pool.Selected)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                ref var u = ref state.Units[idx];
                if (state.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                    hasWorker = true;
                if ((u.Flags & UnitFlags.Building) != 0)
                    building = idx;
            }

            _btnX = 8;
            _btnY = Screen.height - 34;

            if (hasWorker)
            {
                var menu = TechTree.WorkerBuildings(state.Players[LocalPlayer].Race);
                foreach (var b in menu)
                    if (sim.CanProduce(LocalPlayer, b))
                        if (Btn($"{NameOf(b)} ({CostOf(state, b)})"))
                            PendingBuildType = (ushort)b;
                return;
            }
            if (building < 0)
                return;

            ref var bld = ref state.Units[building];
            var bType = (UnitTypeId)bld.TypeId;

            // Busy: show what's cooking and offer Cancel.
            if ((bld.Flags & UnitFlags.UnderConstruction) != 0
                || bld.BuildType != 0 || bld.ResearchId != 0)
            {
                string doing = (bld.Flags & UnitFlags.UnderConstruction) != 0 ? "constructing..."
                    : bld.ResearchId != 0 ? $"researching {NameOf((UpgradeId)(bld.ResearchId - 1))}..."
                    : $"{NameOf((UnitTypeId)bld.BuildType)}...";
                if (Btn("Cancel"))
                    Submit(CommandOp.Cancel, 0, building, state);
                GUI.Label(new Rect(_btnX + 8, _btnY + 4, 400, 24), doing);
                return;
            }

            // Train (research substitutions already applied).
            ulong researched = state.Players[LocalPlayer].Researched;
            foreach (var baseType in TechTree.Trains(bType))
            {
                var t = TechTree.TrainSubstitute(baseType, researched);
                if (sim.CanTrainAt(LocalPlayer, bType, t))
                    if (Btn($"Train {NameOf(t)} ({CostOf(state, t)})"))
                        Submit(CommandOp.Train, (ushort)t, building, state);
            }

            // Building tier upgrades.
            foreach (var target in TechTree.UpgradesTo(bType))
                if (sim.CanUpgradeBuildingTo(LocalPlayer, bType, target))
                    if (Btn($"Upgrade to {NameOf(target)} ({CostOf(state, target)})"))
                        Submit(CommandOp.Train, (ushort)target, building, state);

            // Research.
            foreach (var u in TechTree.Research(bType))
                if (sim.CanResearchAt(LocalPlayer, bType, u))
                {
                    ref var row = ref state.Rules.Upgrades[(int)u];
                    string cost = row.Lumber > 0 ? $"{row.Gold}g/{row.Lumber}w" : $"{row.Gold}g";
                    if (Btn($"{NameOf(u)} ({cost})"))
                        Submit(CommandOp.Research, (ushort)u, building, state);
                }
        }

        /// <summary>Bottom-anchored button that wraps into rows above.</summary>
        bool Btn(string label)
        {
            float width = 160;
            if (_btnX + width > Screen.width - 8)
            {
                _btnX = 8;
                _btnY -= 32;
            }
            bool hit = GUI.Button(new Rect(_btnX, _btnY, width, 28), label);
            _btnX += width + 6;
            return hit;
        }

        unsafe void Submit(CommandOp op, ushort param, int buildingSlot, GameState state)
        {
            var cmd = new GameCommand
            {
                Op = op,
                Player = LocalPlayer,
                Param = param,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] =
                new UnitId((ushort)buildingSlot, state.Units[buildingSlot].Gen).Packed;
            _host.SubmitCommand(cmd);
        }

        static string CostOf(GameState state, UnitTypeId type)
        {
            ref var row = ref state.Rules.Units[(int)type];
            return row.LumberCost > 0 ? $"{row.GoldCost}g/{row.LumberCost}w" : $"{row.GoldCost}g";
        }

        /// <summary>Enum name with spaces ("ElvenLumberMill" → "Elven Lumber Mill").</summary>
        static string NameOf<T>(T id)
        {
            string s = id.ToString();
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
