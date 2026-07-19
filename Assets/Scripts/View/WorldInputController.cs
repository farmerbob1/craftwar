using System.Collections.Generic;
using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// Drag-box selection + right-click smart orders + building placement.
    /// Replaces SelectionController: the order/selection logic is unchanged,
    /// but input now arrives from InputRouter and the drag rectangle is drawn
    /// by the UI layer. Terminates exclusively in GameCommands handed to the
    /// sim host — the view never touches state. Local player is always slot 0.
    /// </summary>
    public sealed class WorldInputController : MonoBehaviour
    {
        const byte LocalPlayer = 0;

        ISimHost _host;
        SelectionState _selection;
        UIState _ui;
        Camera _camera;
        int _mapHeight;
        InputRouter _input;
        DragSelectOverlayView _dragView;
        AudioDirector _audio;

        /// <summary>
        /// Order acknowledgements are a local-client reaction to a click, not a
        /// sim event: in lockstep, sim-driven acks would play every player's
        /// clicks on every machine.
        /// </summary>
        public void SetAudio(AudioDirector audio) => _audio = audio;

        Vector2 _dragStartScreen;
        bool _dragging;

        public void Init(ISimHost host, UIState ui, Camera cam, int mapHeight,
            InputRouter input, DragSelectOverlayView dragView)
        {
            _host = host;
            _ui = ui;
            _selection = ui.Selection;
            _camera = cam;
            _mapHeight = mapHeight;
            _input = input;
            _dragView = dragView;

            _input.OnSelectPressed += HandleSelectPressed;
            _input.OnSelectReleased += HandleSelectReleased;
            _input.OnOrderPressed += HandleOrderPressed;
        }

        void OnDestroy()
        {
            if (_input == null)
                return;
            _input.OnSelectPressed -= HandleSelectPressed;
            _input.OnSelectReleased -= HandleSelectReleased;
            _input.OnOrderPressed -= HandleOrderPressed;
        }

        void HandleSelectPressed()
        {
            if (_host?.Sim == null)
                return;

            // An armed card order swallows the click entirely.
            if (_ui.HasPendingOrder)
            {
                if (!_ui.PointerOverUI)
                    ResolvePendingOrder(_input.PointerPosition);
                return;
            }

            // A press that lands on the HUD belongs to the HUD.
            if (_ui.PointerOverUI)
                return;

            if (_input.AttackMove && _selection.Count > 0)
            {
                IssueSmartOrder(_input.PointerPosition, attackMove: true);
                return;
            }

            _dragStartScreen = _input.PointerPosition;
            _dragging = true;
        }

        void HandleSelectReleased()
        {
            if (!_dragging || _host?.Sim == null)
                return;
            _dragging = false;
            _dragView?.Hide();
            // A drag that began in the world completes in the world, even if
            // the pointer wandered over the sidebar on the way up.
            SelectInRect(_dragStartScreen, _input.PointerPosition, _input.Additive);
        }

        void HandleOrderPressed()
        {
            if (_host?.Sim == null)
                return;

            if (_ui.HasPendingOrder)
            {
                _ui.ClearPendingOrder(); // right-click cancels a card order
                return;
            }
            if (_ui.PointerOverUI || _selection.Count == 0)
                return;

            if (TryIssueRally(_input.PointerPosition))
                return;
            IssueSmartOrder(_input.PointerPosition, attackMove: false);
        }

        void Update()
        {
            if (_dragging)
                _dragView?.Show(_dragStartScreen, _input.PointerPosition);
        }

        /// <summary>
        /// Resolve an order armed from the command card against the clicked
        /// tile. Move/Patrol take a destination; Attack, Harvest and Repair take
        /// whatever is under the cursor and fall back sensibly when it is empty.
        /// The order is cleared either way — a click always ends targeting.
        /// </summary>
        unsafe void ResolvePendingOrder(Vector2 screenPos)
        {
            if (_ui.PendingOrder == PendingOrderKind.Build)
            {
                PlaceBuilding(screenPos); // clears the pending order itself
                return;
            }

            var state = _host.Sim.State;
            Vector2 world = _camera.ScreenToWorldPoint(screenPos);
            int tileX = Mathf.FloorToInt(world.x);
            int tileY = _mapHeight - 1 - Mathf.FloorToInt(world.y);
            if (state.Terrain == null || !state.Terrain.InBounds(tileX, tileY))
            {
                _ui.ClearPendingOrder();
                return;
            }

            uint occ = state.OccupancySurface[tileY * state.Terrain.Width + tileX];
            if (occ == 0)
                occ = state.OccupancyAir[tileY * state.Terrain.Width + tileX];

            var op = CommandOp.Move;
            uint targetPacked = 0;
            switch (_ui.PendingOrder)
            {
                case PendingOrderKind.Move:
                    op = CommandOp.Move;
                    break;

                case PendingOrderKind.Patrol:
                    op = CommandOp.Patrol;
                    break;

                case PendingOrderKind.Attack:
                    // On a unit: explicit attack. On empty ground: attack-move,
                    // which is what a ground-targeted attack means in WC2.
                    if (occ != 0)
                    {
                        op = CommandOp.Attack;
                        targetPacked = occ;
                    }
                    else
                    {
                        op = CommandOp.AttackMove;
                    }
                    break;

                case PendingOrderKind.Harvest:
                    op = CommandOp.Harvest;
                    // A mine under the cursor is the target; bare ground with
                    // wood harvests wood (TargetUnit stays 0).
                    if (occ != 0 && state.TryGetUnitIndex(UnitId.FromPacked(occ), out int mi)
                        && state.Rules.Units[state.Units[mi].TypeId]
                            .Is(UnitTypeFlags.GoldMine | UnitTypeFlags.OilSource))
                        targetPacked = occ;
                    else if (!state.Terrain.HasWood(tileX, tileY))
                    {
                        _ui.ClearPendingOrder(); // nothing harvestable there
                        return;
                    }
                    break;

                case PendingOrderKind.Repair:
                    if (occ == 0 || !state.TryGetUnitIndex(UnitId.FromPacked(occ), out int bi)
                        || state.Units[bi].Player != LocalPlayer
                        || (state.Units[bi].Flags & UnitFlags.Building) == 0)
                    {
                        _ui.ClearPendingOrder(); // not one of our buildings
                        return;
                    }
                    op = CommandOp.Repair;
                    targetPacked = occ;
                    break;

                case PendingOrderKind.Unload:
                    op = CommandOp.Unload;
                    break;
            }

            var cmd = new GameCommand
            {
                Op = op,
                Player = LocalPlayer,
                TargetX = (ushort)tileX,
                TargetY = (ushort)tileY,
                TargetUnit = targetPacked,
            };
            foreach (uint packed in _selection)
            {
                if (cmd.SelectionCount >= GameCommand.MaxSelection)
                    break;
                cmd.Selection.Ids[cmd.SelectionCount++] = packed;
            }
            if (cmd.SelectionCount > 0)
                _host.SubmitCommand(cmd);
            _ui.ClearPendingOrder();
        }

        /// <summary>
        /// A single completed building of ours selected: right-click sets its
        /// rally point instead of issuing a move order it could never obey.
        /// </summary>
        unsafe bool TryIssueRally(Vector2 screenPos)
        {
            if (_selection.Count != 1)
                return false;
            var state = _host.Sim.State;
            int idx = -1;
            foreach (uint packed in _selection)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out idx))
                    return false;
                break;
            }
            if (idx < 0)
                return false;
            ref var b = ref state.Units[idx];
            if (b.Player != LocalPlayer
                || (b.Flags & UnitFlags.Building) == 0
                || (b.Flags & UnitFlags.UnderConstruction) != 0)
                return false;

            Vector2 world = _camera.ScreenToWorldPoint(screenPos);
            int tileX = Mathf.FloorToInt(world.x);
            int tileY = _mapHeight - 1 - Mathf.FloorToInt(world.y);
            if (state.Terrain == null || !state.Terrain.InBounds(tileX, tileY))
                return false;

            var cmd = new GameCommand
            {
                Op = CommandOp.SetRally,
                Player = LocalPlayer,
                TargetX = (ushort)tileX,
                TargetY = (ushort)tileY,
                SelectionCount = 1,
            };
            cmd.Selection.Ids[0] = new UnitId((ushort)idx, b.Gen).Packed;
            _host.SubmitCommand(cmd);
            return true;
        }

        /// <summary>
        /// Context order: enemy -> Attack, gold mine -> Harvest, tree ->
        /// Harvest wood, otherwise Move/AttackMove.
        /// </summary>
        void IssueSmartOrder(Vector2 screenPos, bool attackMove)
        {
            Vector2 world = _camera.ScreenToWorldPoint(screenPos);
            IssueSmartOrderAtTile(
                Mathf.FloorToInt(world.x),
                _mapHeight - 1 - Mathf.FloorToInt(world.y),
                attackMove);
        }

        /// <summary>
        /// The smart right-click, addressed by tile so the minimap can command
        /// through exactly the same path as the battlefield.
        /// </summary>
        public unsafe void IssueSmartOrderAtTile(int tileX, int tileY, bool attackMove)
        {
            var state = _host.Sim.State;
            if (state.Terrain == null || !state.Terrain.InBounds(tileX, tileY))
                return;

            uint targetPacked = 0;
            var op = attackMove ? CommandOp.AttackMove : CommandOp.Move;
            if (!attackMove)
            {
                uint occ = state.OccupancySurface[tileY * state.Terrain.Width + tileX];
                if (occ == 0)
                    occ = state.OccupancyAir[tileY * state.Terrain.Width + tileX];
                if (occ != 0 && state.TryGetUnitIndex(UnitId.FromPacked(occ), out int ti)
                    // Fog: you cannot right-click what you cannot see, or the
                    // cursor becomes a probe for hidden units. Own units always
                    // resolve. Falls through to a plain Move.
                    && (state.Units[ti].Player == LocalPlayer
                        || _host.Sim.IsUnitVisible(LocalPlayer, ref state.Units[ti])))
                {
                    ref var target = ref state.Units[ti];
                    // A completed oil platform is a harvest target for tankers,
                    // exactly as a gold mine is for workers.
                    if (state.Rules.Units[target.TypeId].Is(UnitTypeFlags.GoldMine)
                        || (state.Rules.Units[target.TypeId].Is(UnitTypeFlags.OilSource)
                            && (target.Flags & UnitFlags.UnderConstruction) == 0))
                    {
                        op = CommandOp.Harvest;
                        targetPacked = occ;
                    }
                    // Our own transport with ground troops selected: climb in.
                    else if (target.Player == LocalPlayer
                        && state.Rules.Units[target.TypeId].Is(UnitTypeFlags.Transport)
                        && SelectionHasGroundUnit(state))
                    {
                        op = CommandOp.Board;
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
                else if (SelectionHasLoadedTransport(state)
                    && state.Terrain.IsPassable(MoveDomain.Land, tileX, tileY))
                {
                    // Right-clicking dry land with a laden transport unloads there.
                    op = CommandOp.Unload;
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
            foreach (uint packed in _selection)
            {
                if (cmd.SelectionCount >= GameCommand.MaxSelection)
                    break;
                cmd.Selection.Ids[cmd.SelectionCount++] = packed;
            }
            if (cmd.SelectionCount > 0)
                _audio?.Play(op == CommandOp.Attack || op == CommandOp.AttackMove
                    ? SoundId.OrderAttack
                    : SoundId.OrderMove);
            _host.SubmitCommand(cmd);
        }

        bool SelectionHasWorker(GameState state)
        {
            foreach (uint packed in _selection)
                if (state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx)
                    && state.Rules.Units[state.Units[idx].TypeId].Is(UnitTypeFlags.Peon))
                    return true;
            return false;
        }

        bool SelectionHasGroundUnit(GameState state)
        {
            foreach (uint packed in _selection)
                if (state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx)
                    && (state.Units[idx].Flags & UnitFlags.Building) == 0
                    && state.DomainOf(state.Units[idx].TypeId) == MoveDomain.Land)
                    return true;
            return false;
        }

        bool SelectionHasLoadedTransport(GameState state)
        {
            foreach (uint packed in _selection)
                if (state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx)
                    && state.Units[idx].CargoCount > 0)
                    return true;
            return false;
        }

        void SelectInRect(Vector2 a, Vector2 b, bool additive)
        {
            if (!additive)
                _selection.Clear();

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
                    _selection.Add(new UnitId((ushort)i, u.Gen).Packed);
                    if (_selection.Count >= GameCommand.MaxSelection)
                        break;
                }
            }

            // Click with no mobile units hit: select the building under the
            // cursor. A building selection is always exclusive — the original
            // never mixes buildings with units, and the command card has no
            // sensible card for a mixed selection.
            if (_selection.Count == 0)
            {
                int tileX = Mathf.FloorToInt(rect.center.x);
                int tileY = _mapHeight - 1 - Mathf.FloorToInt(rect.center.y);
                if (state.Terrain != null && state.Terrain.InBounds(tileX, tileY))
                {
                    uint occ = state.OccupancySurface[tileY * state.Terrain.Width + tileX];
                    if (occ != 0 && state.TryGetUnitIndex(UnitId.FromPacked(occ), out int bi)
                        && state.Units[bi].Player == LocalPlayer
                        && (state.Units[bi].Flags & UnitFlags.Building) != 0)
                        _selection.SetSingle(occ);
                }
                return;
            }

            // Units were selected: evict any building a previous shift-click
            // left in the set, so the two can never coexist.
            DropBuildings(state);
            if (_selection.Count > 0)
                _audio?.Play(SoundId.OrderSelect);
        }

        readonly List<uint> _evict = new List<uint>();

        void DropBuildings(GameState state)
        {
            _evict.Clear();
            foreach (uint packed in _selection)
                if (state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx)
                    && (state.Units[idx].Flags & UnitFlags.Building) != 0)
                    _evict.Add(packed);
            for (int i = 0; i < _evict.Count; i++)
                _selection.Remove(_evict[i]);
        }

        /// <summary>
        /// Where a building would go for a cursor at `world`: the footprint is
        /// centred on the cursor, then an oil platform snaps onto whichever
        /// patch it overlaps. The placement ghost and the Build command MUST
        /// call this same helper — when they each did their own arithmetic the
        /// preview sat off the site it was previewing.
        /// </summary>
        public static bool BuildTileUnderCursor(GameState state, int mapHeight,
            Vector2 world, ushort buildType, out int tileX, out int tileY)
        {
            int size = state.Footprint(buildType);
            tileX = Mathf.FloorToInt(world.x) - (size - 1) / 2;
            tileY = mapHeight - 1 - Mathf.FloorToInt(world.y) - (size - 1) / 2;

            if (state.Rules.Units[buildType].Is(UnitTypeFlags.OilSource)
                && BuildSite.TrySnapToPatch(state, tileX, tileY, size, out int sx, out int sy))
            {
                tileX = sx;
                tileY = sy;
            }
            return state.Terrain != null && state.Terrain.InBounds(tileX, tileY);
        }

        unsafe void PlaceBuilding(Vector2 screenPos)
        {
            var state = _host.Sim.State;
            Vector2 world = _camera.ScreenToWorldPoint(screenPos);
            if (!BuildTileUnderCursor(state, _mapHeight, world, _ui.PendingBuildType,
                    out int tileX, out int tileY))
                return;

            var cmd = new GameCommand
            {
                Op = CommandOp.Build,
                Player = LocalPlayer,
                TargetX = (ushort)tileX,
                TargetY = (ushort)tileY,
                Param = _ui.PendingBuildType,
            };
            // Workers erect land structures, tankers raise oil platforms — the
            // sim applies the same split (GameSim.CanBuild).
            bool wantsTanker = state.Rules.Units[_ui.PendingBuildType].Is(UnitTypeFlags.OilSource);
            foreach (uint packed in _selection)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(packed), out int idx))
                    continue;
                ref var row = ref state.Rules.Units[state.Units[idx].TypeId];
                if (!row.Is(wantsTanker ? UnitTypeFlags.Tanker : UnitTypeFlags.Peon))
                    continue;
                cmd.Selection.Ids[cmd.SelectionCount++] = packed;
                break; // one builder
            }
            if (cmd.SelectionCount > 0)
                _host.SubmitCommand(cmd);
            _ui.ClearPendingOrder();
        }
    }
}
