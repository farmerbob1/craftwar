using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// Classic RTS camera: arrow keys + screen-edge scroll + wheel zoom,
    /// clamped to map bounds. View-only — never touches the sim. All the math
    /// is unchanged from M1; only the input source moved to InputRouter, which
    /// also means panning goes dead under a modal along with the rest of the
    /// Camera map. WASD does not pan — the original pans on arrows, and the
    /// letters belong to the command card.
    ///
    /// The camera renders the whole screen, but the HUD paints chrome over its
    /// left and top edges, so the battlefield the player can actually see is a
    /// sub-rect of the render target. Every clamp and every centring works on
    /// that sub-rect (the "view rect"), not the full screen — otherwise the
    /// map's left and top edges sit permanently behind the sidebar and the
    /// resource bar, unreachable on maps small enough to fit on screen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] float tilesPerSecond = 16f;
        [SerializeField] int edgePixels = 8;
        [SerializeField] float minOrthoSize = 4f;
        [SerializeField] float maxOrthoSize = 24f;
        [SerializeField] float zoomStep = 2f;

        Camera _camera;
        Rect _mapBounds = new Rect(0, 0, 128, 128);
        bool _edgeScrollEnabled = true;
        InputRouter _input;

        /// <summary>
        /// Screen-pixel margins the HUD covers, as (left, top, right, bottom).
        /// Polled rather than pushed: the source is UI Toolkit layout, which
        /// only resolves after the first frame and re-resolves on every resize.
        /// </summary>
        Func<Vector4> _chromeInsets;

        /// <summary>Camera centre -> view-rect centre, world units, last frame.</summary>
        Vector2 _viewOffset;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        public void Init(InputRouter input) => _input = input;

        /// <summary>
        /// Supply the HUD chrome margins in screen pixels (left, top, right,
        /// bottom). Null clears them and the camera goes back to treating the
        /// whole screen as battlefield.
        /// </summary>
        public void SetChromeInsetSource(Func<Vector4> insets)
        {
            _chromeInsets = insets;
            SyncViewOffset();
        }

        /// <summary>Map size in tiles (1 tile = 1 world unit).</summary>
        public void SetMapBounds(int widthTiles, int heightTiles)
        {
            _mapBounds = new Rect(0, 0, widthTiles, heightTiles);
            ClampToBounds();
        }

        public void SetEdgeScroll(bool enabled) => _edgeScrollEnabled = enabled;

        void Update()
        {
            if (_input == null)
                return;

            // Chrome size and zoom both move the view rect relative to the
            // camera; absorb that first so the visible centre holds still.
            SyncViewOffset();

            var mouse = Mouse.current;
            Vector2 move = _input.Pan;

            // Application.isFocused keeps the editor's Game view from scrolling
            // while the pointer is off in the Inspector; CameraInputActive stops
            // it from creeping along under a modal, where Pan reads zero anyway.
            if (_edgeScrollEnabled && mouse != null && move == Vector2.zero
                && Application.isFocused && _input.CameraInputActive)
            {
                Vector2 pos = _input.PointerPosition;
                if (pos.x >= 0 && pos.y >= 0 && pos.x <= Screen.width && pos.y <= Screen.height)
                {
                    if (pos.x <= edgePixels) move.x -= 1;
                    else if (pos.x >= Screen.width - edgePixels) move.x += 1;
                    if (pos.y <= edgePixels) move.y -= 1;
                    else if (pos.y >= Screen.height - edgePixels) move.y += 1;
                }
            }

            if (move != Vector2.zero)
            {
                Vector3 delta = new Vector3(move.x, move.y, 0).normalized
                    * (tilesPerSecond * Time.unscaledDeltaTime);
                transform.position += delta;
            }

            float scroll = _input.Zoom;
            if (scroll != 0)
            {
                float size = _camera.orthographicSize - Mathf.Sign(scroll) * zoomStep;
                _camera.orthographicSize = Mathf.Clamp(size, minOrthoSize, maxOrthoSize);
            }

            ClampToBounds();
        }

        /// <summary>
        /// Jump the camera so (worldX, worldY) sits in the middle of the
        /// *visible* battlefield — not the middle of the screen, which the
        /// sidebar owns a slice of. Used by the minimap, the control groups and
        /// the initial start-location centring.
        /// </summary>
        public void CenterOn(float worldX, float worldY)
        {
            var p = transform.position;
            transform.position = new Vector3(worldX - _viewOffset.x, worldY - _viewOffset.y, p.z);
            ClampToBounds();
        }

        /// <summary>
        /// The original's three map bookmarks: Shift+F2..F4 store the current
        /// position, F2..F4 jump back to it. Empty slots ignore the recall
        /// rather than snapping to the map origin.
        /// </summary>
        readonly Vector2[] _bookmarks = new Vector2[InputRouter.ViewportSlots];
        readonly bool[] _bookmarkSet = new bool[InputRouter.ViewportSlots];

        public void HandleViewportKey(int slot, bool save)
        {
            if ((uint)slot >= _bookmarks.Length)
                return;
            if (save)
            {
                _bookmarks[slot] = transform.position;
                _bookmarkSet[slot] = true;
                return;
            }
            if (_bookmarkSet[slot])
                CenterOn(_bookmarks[slot].x, _bookmarks[slot].y);
        }

        /// <summary>Half-height of the unobscured view in world units (= tiles).</summary>
        public float HalfHeightWorld => ViewHalfExtents().y;

        /// <summary>Half-width of the unobscured view in world units (= tiles).</summary>
        public float HalfWidthWorld => ViewHalfExtents().x;

        /// <summary>World point at the middle of the unobscured view.</summary>
        public Vector2 ViewCenterWorld => (Vector2)transform.position + _viewOffset;

        /// <summary>
        /// World units per screen pixel. Orthographic size is half the render
        /// target's height, so this is the same on both axes.
        /// </summary>
        float WorldPerPixel =>
            _camera == null || Screen.height <= 0
                ? 0f
                : _camera.orthographicSize * 2f / Screen.height;

        Vector4 ChromeWorld()
        {
            if (_chromeInsets == null)
                return Vector4.zero;
            return _chromeInsets() * WorldPerPixel;
        }

        /// <summary>
        /// Half-width/height of the battlefield the HUD leaves visible. Floored
        /// well above zero so a HUD that (transiently) covers everything cannot
        /// produce an inverted clamp range.
        /// </summary>
        Vector2 ViewHalfExtents()
        {
            if (_camera == null)
                return Vector2.zero;
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;
            Vector4 c = ChromeWorld();
            return new Vector2(
                Mathf.Max(0.5f, halfW - (c.x + c.z) * 0.5f),
                Mathf.Max(0.5f, halfH - (c.y + c.w) * 0.5f));
        }

        /// <summary>
        /// Recompute the camera-centre -> view-centre offset, shifting the
        /// camera by the delta so whatever the player was looking at stays put
        /// when the chrome resizes or the zoom changes.
        /// </summary>
        void SyncViewOffset()
        {
            Vector4 c = ChromeWorld();
            // Screen Y grows upward, so the top inset pushes the view centre down.
            var offset = new Vector2((c.x - c.z) * 0.5f, (c.w - c.y) * 0.5f);
            if (offset == _viewOffset)
                return;
            transform.position += (Vector3)(_viewOffset - offset);
            _viewOffset = offset;
            ClampToBounds();
        }

        void ClampToBounds()
        {
            Vector2 half = ViewHalfExtents();
            Vector3 p = transform.position;
            Vector2 center = (Vector2)p + _viewOffset;

            // If the view is wider than the map, center instead of clamping.
            center.x = _mapBounds.width <= half.x * 2
                ? _mapBounds.center.x
                : Mathf.Clamp(center.x, _mapBounds.xMin + half.x, _mapBounds.xMax - half.x);
            center.y = _mapBounds.height <= half.y * 2
                ? _mapBounds.center.y
                : Mathf.Clamp(center.y, _mapBounds.yMin + half.y, _mapBounds.yMax - half.y);

            transform.position = new Vector3(
                center.x - _viewOffset.x, center.y - _viewOffset.y, p.z);
        }
    }
}
