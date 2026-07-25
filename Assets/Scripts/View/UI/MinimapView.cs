using Craftwar.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// The sidebar minimap: baked terrain, live unit dots, fog mask, and a
    /// viewport rectangle. Left-click (or drag) jumps the camera, right-click
    /// issues the same smart order the battlefield would.
    ///
    /// Terrain is averaged to one pixel per tile once at load and patched from
    /// TileChanges, so the per-frame cost is just the dot pass. The dots refresh
    /// at ~8 Hz like the original rather than every frame — the texture upload
    /// is the expensive part, not the scan.
    ///
    /// Rows are flipped on write (texture row 0 = bottom) to match the tilemap
    /// and the fog mask.
    /// </summary>
    public sealed class MinimapView
    {
        const float RefreshInterval = 1f / 8f;

        static readonly Color32 OwnColor = new Color32(60, 220, 70, 255);
        static readonly Color32 NeutralColor = new Color32(230, 200, 60, 255);

        /// <summary>
        /// One dot colour per player slot, in PUD slot order — red, blue, teal,
        /// violet, orange, black, white, yellow. The original's minimap uses
        /// `gbUnitTeamColorTbl` (PSX <c>unitdraw.c</c>), a table of one palette
        /// index per slot picked to stay legible against the terrain: slot 1
        /// takes a brighter blue than the sprite ramp's base because "Base Blue
        /// team color blends in with water too much", and slot 7 likewise. These
        /// are the on-screen colours of those entries.
        ///
        /// Black (slot 5) is lifted off pure black so it still reads on the
        /// unexplored mask, exactly as the original's index does.
        /// </summary>
        static readonly Color32[] PlayerColors =
        {
            new Color32(200, 0, 0, 255),      // 0 red
            new Color32(40, 80, 255, 255),    // 1 blue
            new Color32(44, 180, 148, 255),   // 2 teal
            new Color32(152, 72, 176, 255),   // 3 violet
            new Color32(240, 132, 20, 255),   // 4 orange
            new Color32(64, 64, 76, 255),     // 5 black
            new Color32(232, 232, 232, 255),  // 6 white
            new Color32(252, 252, 72, 255),   // 7 yellow
        };
        /// <summary>Explored but not currently in sight: dimmed, as on the map.</summary>
        const float ExploredDim = 0.5f;

        readonly ISimHost _host;
        readonly CameraRig _camera;
        readonly WorldInputController _world;
        readonly IMinimapPalette _palette;
        readonly byte _player;
        readonly int _width, _height;

        readonly Texture2D _texture;
        readonly Color32[] _terrain;   // baked, fog- and unit-free
        readonly Color32[] _pixels;    // per-refresh composite
        readonly Image _image;
        readonly VisualElement _viewport;

        float _nextRefresh;
        bool _dragging;

        public MinimapView(MinimapFrameView frame, ISimHost host, CameraRig camera,
            WorldInputController world, IMinimapPalette palette, byte localPlayer,
            int width, int height)
        {
            _host = host;
            _camera = camera;
            _world = world;
            _palette = palette;
            _player = localPlayer;
            _width = width;
            _height = height;

            _texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _terrain = new Color32[width * height];
            _pixels = new Color32[width * height];

            BakeTerrain();

            _image = new Image
            {
                image = _texture,
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
            };
            _image.style.position = Position.Absolute;
            _image.style.left = 0;
            _image.style.top = 0;
            _image.style.right = 0;
            _image.style.bottom = 0;
            frame.Content.Add(_image);

            // Drawn as an element rather than into the texture so it stays a
            // crisp 1px frame regardless of map size.
            _viewport = new VisualElement { name = "minimap-viewport", pickingMode = PickingMode.Ignore };
            _viewport.style.position = Position.Absolute;
            _viewport.style.borderTopWidth = 1;
            _viewport.style.borderBottomWidth = 1;
            _viewport.style.borderLeftWidth = 1;
            _viewport.style.borderRightWidth = 1;
            var white = new Color(1f, 1f, 1f, 0.8f);
            _viewport.style.borderTopColor = white;
            _viewport.style.borderBottomColor = white;
            _viewport.style.borderLeftColor = white;
            _viewport.style.borderRightColor = white;
            frame.Content.Add(_viewport);

            // The frame is the pickable element (content is Ignore), so input
            // is registered there.
            var target = frame.Root ?? frame.Content;
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        void BakeTerrain()
        {
            var state = _host?.Sim?.State;
            if (state?.Tiles == null || _palette == null)
                return;
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    _terrain[TexIndex(x, y)] = _palette.ColorFor(state.Tiles[y * _width + x]);
        }

        /// <summary>Repaint tiles the sim mutated (trees felled, walls broken).</summary>
        public void ApplyTileChanges(System.Collections.Generic.List<(ushort x, ushort y, ushort tile)> changes)
        {
            if (_palette == null)
                return;
            for (int i = 0; i < changes.Count; i++)
            {
                var (x, y, tile) = changes[i];
                if (x < _width && y < _height)
                    _terrain[TexIndex(x, y)] = _palette.ColorFor(tile);
            }
        }

        int TexIndex(int simX, int simY) => (_height - 1 - simY) * _width + simX;

        public void Tick()
        {
            UpdateViewportRect();

            if (Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Redraw();
        }

        void Redraw()
        {
            var sim = _host?.Sim;
            if (sim == null)
                return;
            var state = sim.State;

            bool reveal = GameplaySettings.Current.revealMap;
            byte[] visible = !reveal && state.Visible != null && _player < SimConstants.MaxPlayers
                ? state.Visible[_player] : null;
            byte[] explored = !reveal && state.Explored != null && _player < SimConstants.MaxPlayers
                ? state.Explored[_player] : null;

            // Terrain, masked by fog.
            for (int y = 0; y < _height; y++)
            {
                int simRow = y * _width;
                for (int x = 0; x < _width; x++)
                {
                    int t = TexIndex(x, y);
                    Color32 c = _terrain[t];
                    if (explored != null)
                    {
                        if (explored[simRow + x] == 0)
                            c = new Color32(0, 0, 0, 255);           // never seen
                        else if (visible != null && visible[simRow + x] == 0)
                            c = new Color32((byte)(c.r * ExploredDim),
                                            (byte)(c.g * ExploredDim),
                                            (byte)(c.b * ExploredDim), 255);
                    }
                    _pixels[t] = c;
                }
            }

            // Unit dots on top, fogged enemies omitted.
            for (int i = 0; i < state.HighestUnitIndex; i++)
            {
                ref Unit u = ref state.Units[i];
                if (!u.IsAlive || (u.Flags & UnitFlags.Hidden) != 0)
                    continue;

                bool own = u.Player == _player;
                if (!own && !reveal && !sim.IsUnitVisible(_player, ref u))
                    continue;

                // The original: your own units are green whatever your colour,
                // everyone else wears their slot's colour, neutral is yellow.
                Color32 dot = own ? OwnColor
                    : u.Player >= SimConstants.MaxPlayers ? NeutralColor
                    : PlayerColors[u.Player & 7];

                int size = state.Footprint(u.TypeId);
                for (int dy = 0; dy < size; dy++)
                {
                    int y = u.TileY + dy;
                    if (y < 0 || y >= _height)
                        continue;
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x = u.TileX + dx;
                        if (x < 0 || x >= _width)
                            continue;
                        _pixels[TexIndex(x, y)] = dot;
                    }
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(updateMipmaps: false);
        }

        void UpdateViewportRect()
        {
            if (_camera == null)
                return;
            Rect box = _image.contentRect;
            if (box.width <= 0f || box.height <= 0f)
                return;

            float halfW = _camera.HalfWidthWorld;
            float halfH = _camera.HalfHeightWorld;
            Vector3 c = _camera.transform.position;
            // World Y is flipped relative to sim rows.
            float simCenterY = _height - c.y;

            float px = box.width / _width;
            float py = box.height / _height;

            _viewport.style.left = (c.x - halfW) * px;
            _viewport.style.top = (simCenterY - halfH) * py;
            _viewport.style.width = halfW * 2f * px;
            _viewport.style.height = halfH * 2f * py;
        }

        /// <summary>
        /// Panel position -> sim tile, clamped to the map. Resolved against the
        /// image rather than the frame: the frame carries a 1px border, so its
        /// local origin is offset from the drawn map.
        /// </summary>
        bool TryTileAt(Vector2 panelPosition, out int tileX, out int tileY)
        {
            tileX = tileY = 0;
            Rect box = _image.contentRect;
            if (box.width <= 0f || box.height <= 0f)
                return false;
            Vector2 local = _image.WorldToLocal(panelPosition);
            // UI Y already grows downward, exactly like sim rows.
            tileX = Mathf.Clamp(Mathf.FloorToInt(local.x / box.width * _width), 0, _width - 1);
            tileY = Mathf.Clamp(Mathf.FloorToInt(local.y / box.height * _height), 0, _height - 1);
            return true;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            var target = (VisualElement)evt.currentTarget;
            if (!TryTileAt(evt.position, out int tx, out int ty))
                return;

            if (evt.button == 1) // right: command
            {
                _world?.IssueSmartOrderAtTile(tx, ty, attackMove: false);
            }
            else if (evt.button == 0)
            {
                _dragging = true;
                target.CapturePointer(evt.pointerId);
                JumpTo(tx, ty);
            }
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging)
                return;
            if (TryTileAt(evt.position, out int tx, out int ty))
                JumpTo(tx, ty);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging)
                return;
            _dragging = false;
            ((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void JumpTo(int tileX, int tileY) =>
            // +0.5 centres on the tile; world Y is the flipped row.
            _camera?.CenterOn(tileX + 0.5f, _height - tileY - 0.5f);
    }
}
