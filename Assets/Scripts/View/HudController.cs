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

        void DrawCommandCard(GameState state)
        {
            bool hasPeasant = false;
            int building = -1;
            foreach (uint packed in _pool.Selected)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                ref var u = ref state.Units[idx];
                if (state.Rules.Units[u.TypeId].Is(UnitTypeFlags.Peon))
                    hasPeasant = true;
                if ((u.Flags & UnitFlags.Building) != 0 && (u.Flags & UnitFlags.UnderConstruction) == 0)
                    building = idx;
            }

            float y = Screen.height - 34;
            float x = 8;

            if (hasPeasant)
            {
                Btn(ref x, y, "Farm", UnitTypeId.Farm, state, build: true);
                Btn(ref x, y, "Barracks", UnitTypeId.HumanBarracks, state, build: true);
                Btn(ref x, y, "Town Hall", UnitTypeId.TownHall, state, build: true);
            }
            else if (building >= 0)
            {
                var bType = (UnitTypeId)state.Units[building].TypeId;
                switch (bType)
                {
                    case UnitTypeId.TownHall or UnitTypeId.Keep or UnitTypeId.Castle:
                        Btn(ref x, y, "Train Peasant", UnitTypeId.Peasant, state, build: false, building);
                        break;
                    case UnitTypeId.GreatHall or UnitTypeId.Stronghold or UnitTypeId.Fortress:
                        Btn(ref x, y, "Train Peon", UnitTypeId.Peon, state, build: false, building);
                        break;
                    case UnitTypeId.HumanBarracks:
                        Btn(ref x, y, "Train Footman", UnitTypeId.Footman, state, build: false, building);
                        Btn(ref x, y, "Train Archer", UnitTypeId.Archer, state, build: false, building);
                        break;
                    case UnitTypeId.OrcBarracks:
                        Btn(ref x, y, "Train Grunt", UnitTypeId.Grunt, state, build: false, building);
                        Btn(ref x, y, "Train Axethrower", UnitTypeId.Axethrower, state, build: false, building);
                        break;
                }
                if (state.Units[building].BuildType != 0)
                    GUI.Label(new Rect(x + 8, y + 4, 300, 24), "(training...)");
            }
        }

        unsafe void Btn(ref float x, float y, string label, UnitTypeId type, GameState state,
            bool build, int buildingSlot = -1)
        {
            ref var row = ref state.Rules.Units[(int)type];
            string cost = row.LumberCost > 0 ? $"{row.GoldCost}g/{row.LumberCost}w" : $"{row.GoldCost}g";
            float width = 150;
            if (GUI.Button(new Rect(x, y, width, 28), $"{label} ({cost})"))
            {
                if (build)
                {
                    PendingBuildType = (ushort)type;
                }
                else if (buildingSlot >= 0)
                {
                    var cmd = new GameCommand
                    {
                        Op = CommandOp.Train,
                        Player = LocalPlayer,
                        Param = (ushort)type,
                        SelectionCount = 1,
                    };
                    cmd.Selection.Ids[0] =
                        new UnitId((ushort)buildingSlot, state.Units[buildingSlot].Gen).Packed;
                    _host.SubmitCommand(cmd);
                }
            }
            x += width + 6;
        }
    }
}
