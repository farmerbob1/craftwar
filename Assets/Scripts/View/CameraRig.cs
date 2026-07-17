using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// Classic RTS camera: WASD/arrow keys + screen-edge scroll + wheel zoom,
    /// clamped to map bounds. View-only — reads input devices directly and
    /// never touches the sim.
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

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

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
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            Vector2 move = Vector2.zero;

            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1;
            }

            if (_edgeScrollEnabled && mouse != null && move == Vector2.zero)
            {
                Vector2 pos = mouse.position.ReadValue();
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

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0)
                {
                    float size = _camera.orthographicSize - Mathf.Sign(scroll) * zoomStep;
                    _camera.orthographicSize = Mathf.Clamp(size, minOrthoSize, maxOrthoSize);
                }
            }

            ClampToBounds();
        }

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
