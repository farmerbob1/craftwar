using System.Text;
using Craftwar.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// F3 debug overlay (IMGUI; deliberately not ported to UI Toolkit).
    /// Read-only window
    /// onto sim state for playtesting: player 0 economy, the raw fields of every
    /// selected unit, and the tile under the cursor. Never mutates the sim — it
    /// only reads GameState, so it is safe to leave wired up in dev builds.
    ///
    /// Surfaces the otherwise-silent production gates (food cap, missing tech,
    /// insufficient resources): if a Train click "does nothing", the selected
    /// building shows BuildType/TrainTicks == 0 while Food is at the cap here.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        const byte LocalPlayer = 0;

        ISimHost _host;
        UnitViewPool _pool;
        Camera _camera;
        int _mapHeight;
        bool _visible;

        GUIStyle _style;
        readonly StringBuilder _sb = new StringBuilder(1024);

        public void Init(ISimHost host, UnitViewPool pool, Camera cam, int mapHeight)
        {
            _host = host;
            _pool = pool;
            _camera = cam;
            _mapHeight = mapHeight;
        }

        /// <summary>
        /// Driven by InputRouter's ToggleDebug action. Backquote, not F3: F3 is
        /// the original's second map bookmark.
        /// </summary>
        public void Toggle() => _visible = !_visible;

        void OnGUI()
        {
            if (!_visible || _host?.Sim == null)
                return;

            var state = _host.Sim.State;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false, wordWrap = false };

            _sb.Clear();
            AppendEconomy(state);
            AppendSelection(state);
            AppendHoverTile(state);

            string text = _sb.ToString();
            var content = new GUIContent(text);
            Vector2 size = _style.CalcSize(content);
            var panel = new Rect(6, 30, Mathf.Max(320f, size.x + 16f), size.y + 12f);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 8, panel.y + 6, panel.width - 16, panel.height - 12), content, _style);
        }

        void AppendEconomy(GameState state)
        {
            ref var p = ref state.Players[LocalPlayer];
            _sb.Append("== DEBUG (F3) ==  tick ").Append(state.Tick).Append('\n');
            _sb.Append("Player 0:  Gold ").Append(p.Gold)
               .Append("   Lumber ").Append(p.Lumber)
               .Append("   Oil ").Append(p.Oil)
               .Append("   Food ").Append(p.FoodUsed).Append('/').Append(p.FoodMax);
            if (p.FoodUsed >= p.FoodMax)
                _sb.Append("  <FOOD CAP: training blocked>");
            _sb.Append('\n');
        }

        void AppendSelection(GameState state)
        {
            _sb.Append("-- Selection (").Append(_pool.Selected.Count).Append(") --\n");
            int shown = 0;
            foreach (uint packed in _pool.Selected)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                if (shown++ >= 8)
                {
                    _sb.Append("  ...(more)\n");
                    break;
                }
                ref var u = ref state.Units[idx];
                _sb.Append("  slot ").Append(idx)
                   .Append(" type ").Append(u.TypeId).Append(' ').Append((UnitTypeId)u.TypeId).Append('\n');
                _sb.Append("    Order ").Append(u.Order)
                   .Append("  Harvest ").Append(u.Harvest)
                   .Append("  Carry ").Append(u.Carry)
                   .Append("  Flags ").Append(u.Flags).Append('\n');
                _sb.Append("    Hp ").Append(u.Hp)
                   .Append("  OrderX/Y ").Append(u.OrderX).Append(',').Append(u.OrderY)
                   .Append("  Path ").Append(u.PathCursor).Append('/').Append(u.PathLength)
                   .Append("  StepRem ").Append(u.StepRemaining)
                   .Append("  Wait ").Append(u.WaitTicks).Append('\n');
                _sb.Append("    Timer ").Append(u.Timer)
                   .Append("  TrainTicks ").Append(u.TrainTicks)
                   .Append("  BuildType ").Append(u.BuildType);
                if (u.BuildType != 0)
                    _sb.Append(" (").Append((UnitTypeId)(u.BuildType - 1)).Append(')');
                _sb.Append("  ResearchId ").Append(u.ResearchId);
                if (u.ResearchId != 0)
                    _sb.Append(" (").Append((UpgradeId)(u.ResearchId - 1)).Append(')');
                _sb.Append('\n');
            }
        }

        void AppendHoverTile(GameState state)
        {
            _sb.Append("-- Hover tile --\n");
            var mouse = Mouse.current;
            if (mouse == null || _camera == null || state.Terrain == null)
            {
                _sb.Append("  (unavailable)");
                return;
            }
            // Same screen->tile math as WorldInputController.IssueSmartOrder.
            Vector2 world = _camera.ScreenToWorldPoint(mouse.position.ReadValue());
            int tileX = Mathf.FloorToInt(world.x);
            int tileY = _mapHeight - 1 - Mathf.FloorToInt(world.y);
            if (!state.Terrain.InBounds(tileX, tileY))
            {
                _sb.Append("  tile ").Append(tileX).Append(',').Append(tileY).Append(" (off-map)");
                return;
            }
            int t = tileY * state.Terrain.Width + tileX;
            ushort mtxm = state.Tile(t);
            uint occ = state.OccupancySurface[t];
            _sb.Append("  tile ").Append(tileX).Append(',').Append(tileY)
               .Append("   MTXM 0x").Append(mtxm.ToString("X4"))
               .Append("   Passable(Land) ").Append(state.Terrain.IsPassable(MoveDomain.Land, tileX, tileY))
               .Append("   Wood ").Append(state.Terrain.HasWood(tileX, tileY))
               .Append("   Occ 0x").Append(occ.ToString("X8"));
        }
    }
}
