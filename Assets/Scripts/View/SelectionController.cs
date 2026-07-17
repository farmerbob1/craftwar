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

        Vector2 _dragStartScreen;
        bool _dragging;

        public void Init(ISimHost host, UnitViewPool pool, Camera cam, int mapHeight)
        {
            _host = host;
            _pool = pool;
            _camera = cam;
            _mapHeight = mapHeight;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _host?.Sim == null)
                return;

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
                IssueMove(mouse.position.ReadValue());
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
                if (!u.IsAlive || u.Player != LocalPlayer)
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
        }

        unsafe void IssueMove(Vector2 screenPos)
        {
            Vector2 world = _camera.ScreenToWorldPoint(screenPos);
            int tileX = Mathf.FloorToInt(world.x);
            int tileY = _mapHeight - 1 - Mathf.FloorToInt(world.y);
            var state = _host.Sim.State;
            if (state.Terrain == null || !state.Terrain.InBounds(tileX, tileY))
                return;

            var cmd = new GameCommand
            {
                Op = CommandOp.Move,
                Player = LocalPlayer,
                TargetX = (ushort)tileX,
                TargetY = (ushort)tileY,
            };
            foreach (uint packed in _pool.Selected)
            {
                if (cmd.SelectionCount >= GameCommand.MaxSelection)
                    break;
                cmd.Selection.Ids[cmd.SelectionCount++] = packed;
            }
            _host.SubmitCommand(cmd);
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
