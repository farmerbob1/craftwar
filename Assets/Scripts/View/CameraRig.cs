using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// Classic RTS camera: arrow keys + screen-edge scroll + wheel zoom,
    /// clamped to map bounds. View-only — never touches the sim. All the math
    /// is unchanged from M1; only the input source moved to InputRouter, which
    /// also means panning goes dead under a modal along with the rest of the
    /// Camera map. WASD no longer pans — those keys are command-card hotkeys.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] float tilesPerSecond = 16f;
        [SerializeField] int edgePixels = 6;
        [SerializeField] float minOrthoSize = 4f;
        [SerializeField] float maxOrthoSize = 24f;
        [SerializeField] float zoomStep = 2f;

        Camera _camera;
        Rect _mapBounds = new Rect(0, 0, 128, 128);
        bool _edgeScrollEnabled = true;
        InputRouter _input;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        public void Init(InputRouter input) => _input = input;

        /// <summary>Map size in tiles (1 tile = 1 world unit).</summary>
        public void SetMapBounds(int widthTiles, int heightTiles)
        {
            _mapBounds = new Rect(0, 0, widthTiles, heightTiles);
            ClampToBounds();
        }

        // In the editor, edge scroll fights with the mouse leaving the Game
        // view; runtime builds keep it on.
        public void SetEdgeScroll(bool enabled) => _edgeScrollEnabled = enabled;

        void Update()
        {
            if (_input == null)
                return;

            var mouse = Mouse.current;
            Vector2 move = _input.Pan;

            if (_edgeScrollEnabled && mouse != null && move == Vector2.zero)
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
        /// Jump the camera so (worldX, worldY) is centered, clamped to the map.
        /// Used by the minimap; the initial start-location centering in
        /// GameBootstrap does the same thing by hand.
        /// </summary>
        public void CenterOn(float worldX, float worldY)
        {
            var p = transform.position;
            transform.position = new Vector3(worldX, worldY, p.z);
            ClampToBounds();
        }

        /// <summary>Half-height of the view in world units (= tiles).</summary>
        public float HalfHeightWorld => _camera != null ? _camera.orthographicSize : 0f;

        /// <summary>Half-width of the view in world units (= tiles).</summary>
        public float HalfWidthWorld =>
            _camera != null ? _camera.orthographicSize * _camera.aspect : 0f;

        void ClampToBounds()
        {
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;
            Vector3 p = transform.position;

            // If the view is wider than the map, center instead of clamping.
            p.x = _mapBounds.width <= halfW * 2
                ? _mapBounds.center.x
                : Mathf.Clamp(p.x, _mapBounds.xMin + halfW, _mapBounds.xMax - halfW);
            p.y = _mapBounds.height <= halfH * 2
                ? _mapBounds.center.y
                : Mathf.Clamp(p.y, _mapBounds.yMin + halfH, _mapBounds.yMax - halfH);
            transform.position = p;
        }
    }
}
