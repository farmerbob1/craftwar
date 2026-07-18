using System.Collections.Generic;
using Craftwar.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// Drag-box selection + right-click move orders. Terminates exclusively
    /// in GameCommands handed to the sim host — the view never touches state.
    /// M2: local player is always slot 0.
    /// </summary>
    public sealed class SelectionController : MonoBehaviour
    {
        const byte LocalPlayer = 0;

        ISimHost _host;
        UnitViewPool _pool;
        Camera _camera;
        int _mapHeight;
        HudController _hud;

        Vector2 _dragStartScreen;
        bool _dragging;

        public void Init(ISimHost host, UnitViewPool pool, Camera cam, int mapHeight, HudController hud)
        {
            _host = host;
            _pool = pool;
            _camera = cam;
            _mapHeight = mapHeight;
            _hud = hud;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _host?.Sim == null)
                return;

            // Building placement mode intercepts clicks entirely.
            if (_hud != null && _hud.PendingBuildType != 0)
            {
                if (mouse.rightButton.wasPressedThisFrame)
                    _hud.PendingBuildType = 0;
                else if (mouse.leftButton.wasPressedThisFrame)
                    PlaceBuilding(mouse.position.ReadValue());
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _dragStartScreen = mouse.position.ReadValue();
                _dragging = true;
            }

            if (_dragging && mouse.leftButton.wasReleasedThisFrame)
            {
                _dragging = false;
                SelectInRect(_dragStartScreen, mouse.position.ReadValue(),
                    additive: Keyboard.current != null && Keyboard.current.shiftKey.isPressed);
            }

            if (mouse.rightButton.wasPressedThisFrame && _pool.Selected.Count > 0)
                IssueSmartOrder(mouse.position.ReadValue(), attackMove: false);

            // A + left-click = attack-move.
            if (Keyboard.current != null && Keyboard.current.aKey.isPressed
                && mouse.leftButton.wasPressedThisFrame && _pool.Selected.Count > 0)
            {
                _dragging = false;
                IssueSmartOrder(mouse.position.ReadValue(), attackMove: true);
            }
        }

        /// <summary>
        /// Context order: enemy -> Attack, gold mine -> Harvest, tree ->
        /// Harvest wood, otherwise Move/AttackMove.
        /// </summary>
        unsafe void IssueSmartOrder(Vector2 screenPos, bool attackMove)
        {
            var state = _host.Sim.State;
            Vector2 world = _camera.ScreenToWorldPoint(screenPos);
            int tileX = Mathf.FloorToInt(world.x);
            int tileY = _mapHeight - 1 - Mathf.FloorToInt(world.y);
            if (state.Terrain == null || !state.Terrain.InBounds(tileX, tileY))
                return;

            uint targetPacked = 0;
            var op = attackMove ? CommandOp.AttackMove : CommandOp.Move;
            if (!attackMove)
            {
                uint occ = state.OccupancySurface[tileY * state.Terrain.Width + tileX];
                if (occ == 0)
                    occ = state.OccupancyAir[tileY * state.Terrain.Width + tileX];
                if (occ != 0 && state.TryGetUnitIndex(UnitId.FromPacked(occ), out int ti))
                {
                    ref var target = ref state.Units[ti];
                    if (state.Rules.Units[target.TypeId].Is(UnitTypeFlags.GoldMine))
                    {
                        op = CommandOp.Harvest;
                        targetPacked = occ;
                    }
                    else if (target.Player != LocalPlayer && target.Player < SimConstants.MaxPlayers)
                    {
                        op = CommandOp.Attack;
                        targetPacked = occ;
                    }
                    else if (target.Player == LocalPlayer
                        && (target.Flags & UnitFlags.Building) != 0
                        && (target.Flags & UnitFlags.UnderConstruction) == 0
                        && target.Hp < state.Rules.Units[target.TypeId].Hp
                        && SelectionHasWorker(state))
                    {
                        op = CommandOp.Repair;
                        targetPacked = occ;
                    }
                }
                else if (state.Terrain.HasWood(tileX, tileY))
                {
                    op = CommandOp.Harvest; // TargetUnit stays 0 -> wood
                }
            }

            var cmd = new GameCommand
            {
                Op = op,
                Player = LocalPlayer,
                TargetX = (ushort)tileX,
                TargetY = (ushort)tileY,
                TargetUnit = targetPacked,
            };
            foreach (uint packed in _pool.Selected)
            {
                if (cmd.SelectionCount >= GameCommand.MaxSelection)
                    break;
                cmd.Selection.Ids[cmd.SelectionCount++] = packed;
            }
            _host.SubmitCommand(cmd);
        }

        bool SelectionHasWorker(GameState state)
        {
            foreach (uint packed in _pool.Selected)
                if (state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx)
                    && state.Rules.Units[state.Units[idx].TypeId].Is(UnitTypeFlags.Peon))
                    return true;
            return false;
        }

        void SelectInRect(Vector2 a, Vector2 b, bool additive)
        {
            if (!additive)
                _pool.Selected.Clear();

            Vector2 wa = _camera.ScreenToWorldPoint(a);
            Vector2 wb = _camera.ScreenToWorldPoint(b);
            var rect = Rect.MinMaxRect(
                Mathf.Min(wa.x, wb.x), Mathf.Min(wa.y, wb.y),
                Mathf.Max(wa.x, wb.x), Mathf.Max(wa.y, wb.y));
            // Click (tiny drag) still selects the unit under the cursor.
            if (rect.width < 0.2f && rect.height < 0.2f)
                rect = Rect.MinMaxRect(rect.xMin - 0.4f, rect.yMin - 0.4f, rect.xMax + 0.4f, rect.yMax + 0.4f);

            var state = _host.Sim.State;
            for (int i = 0; i < state.HighestUnitIndex; i++)
            {
                ref var u = ref state.Units[i];
                if (!u.IsAlive || u.Player != LocalPlayer || (u.Flags & UnitFlags.Hidden) != 0)
                    continue;
                if ((u.Flags & UnitFlags.Building) != 0)
                    continue;
                float wx = u.PixX / 32f + 0.5f;
                float wy = _mapHeight - u.PixY / 32f - 0.5f;
                if (rect.Contains(new Vector2(wx, wy)))
                {
                    _pool.Selected.Add(new UnitId((ushort)i, u.Gen).Packed);
                    if (_pool.Selected.Count >= GameCommand.MaxSelection)
                        break;
                }
            }

            // Click with no mobile units hit: select the building under the cursor.
            if (_pool.Selected.Count == 0)
            {
                int tileX = Mathf.FloorToInt(rect.center.x);
                int tileY = _mapHeight - 1 - Mathf.FloorToInt(rect.center.y);
                if (state.Terrain != null && state.Terrain.InBounds(tileX, tileY))
                {
                    uint occ = state.OccupancySurface[tileY * state.Terrain.Width + tileX];
                    if (occ != 0 && state.TryGetUnitIndex(UnitId.FromPacked(occ), out int bi)
                        && state.Units[bi].Player == LocalPlayer
                        && (state.Units[bi].Flags & UnitFlags.Building) != 0)
                        _pool.Selected.Add(occ);
                }
            }
        }

        unsafe void PlaceBuilding(Vector2 screenPos)
        {
            var state = _host.Sim.State;
            Vector2 world = _camera.ScreenToWorldPoint(screenPos);
            int size = state.Footprint(_hud.PendingBuildType);
            // Click points at the footprint center; convert to top-left tile.
            int tileX = Mathf.FloorToInt(world.x) - (size - 1) / 2;
            int tileY = _mapHeight - 1 - Mathf.FloorToInt(world.y) - (size - 1) / 2;
            if (state.Terrain == null || !state.Terrain.InBounds(tileX, tileY))
                return;

            var cmd = new GameCommand
            {
                Op = CommandOp.Build,
                Player = LocalPlayer,
                TargetX = (ushort)tileX,
                TargetY = (ushort)tileY,
                Param = _hud.PendingBuildType,
            };
            foreach (uint packed in _pool.Selected)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx)
                    || !state.Rules.Units[state.Units[idx].TypeId].Is(UnitTypeFlags.Peon))
                    continue;
                cmd.Selection.Ids[cmd.SelectionCount++] = packed;
                break; // one builder
            }
            if (cmd.SelectionCount > 0)
                _host.SubmitCommand(cmd);
            _hud.PendingBuildType = 0;
        }

        void OnGUI()
        {
            if (!_dragging || Mouse.current == null)
                return;
            Vector2 cur = Mouse.current.position.ReadValue();
            var r = Rect.MinMaxRect(
                Mathf.Min(_dragStartScreen.x, cur.x), Screen.height - Mathf.Max(_dragStartScreen.y, cur.y),
                Mathf.Max(_dragStartScreen.x, cur.x), Screen.height - Mathf.Min(_dragStartScreen.y, cur.y));
            GUI.Box(r, GUIContent.none);
        }
    }
}
