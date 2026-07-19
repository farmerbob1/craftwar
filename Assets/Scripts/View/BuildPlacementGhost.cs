using Craftwar.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// View-only placement preview shown while UIState.PendingBuildType is
    /// set. Renders the building's sprite following the mouse, snapped to
    /// the tile grid with the SAME footprint-center -> top-left math as
    /// WorldInputController.PlaceBuilding, tinted translucent green when the
    /// footprint is a legal build spot and red when it isn't. Never touches
    /// sim state — validity mirrors the sim's own check in TickBuilderWalk
    /// (in-bounds + land-passable + unoccupied), treating any occupied tile as
    /// invalid (the hidden builder peasant is irrelevant to the preview).
    /// </summary>
    public sealed class BuildPlacementGhost : MonoBehaviour
    {
        const byte LocalPlayer = 0;
        static readonly Color ValidTint = new Color(0.4f, 1f, 0.4f, 0.5f);
        static readonly Color InvalidTint = new Color(1f, 0.35f, 0.35f, 0.5f);

        ISimHost _host;
        IUnitSpriteProvider _sprites;
        Camera _camera;
        UIState _ui;
        int _mapHeight;

        SpriteRenderer _ghost;
        Sprite _blank;

        public void Init(ISimHost host, IUnitSpriteProvider sprites, Camera cam, int mapHeight, UIState ui)
        {
            _host = host;
            _sprites = sprites;
            _camera = cam;
            _mapHeight = mapHeight;
            _ui = ui;
        }

        void EnsureGhost()
        {
            if (_ghost != null)
                return;
            var go = new GameObject("build_ghost");
            go.transform.SetParent(transform, false);
            _ghost = go.AddComponent<SpriteRenderer>();
            _ghost.sortingOrder = 30000; // above units and tiles
            _ghost.enabled = false;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _blank = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        void LateUpdate()
        {
            if (_host?.Sim == null || _ui == null)
                return;

            ushort type = _ui.PendingBuildType;
            if (type == 0)
            {
                if (_ghost != null && _ghost.enabled)
                    _ghost.enabled = false;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            EnsureGhost();

            var state = _host.Sim.State;
            int size = state.Footprint(type);
            Vector2 world = _camera.ScreenToWorldPoint(mouse.position.ReadValue());
            // Footprint center under the cursor -> top-left tile (mirrors
            // WorldInputController.PlaceBuilding, including the map Y flip).
            int tileX = Mathf.FloorToInt(world.x) - (size - 1) / 2;
            int tileY = _mapHeight - 1 - Mathf.FloorToInt(world.y) - (size - 1) / 2;

            // Building sprite (completed frame). Fall back to a solid quad so
            // the footprint is always visible even if art fails to resolve.
            bool flipX = false;
            Sprite sprite = _sprites != null && _sprites.Has(type)
                ? _sprites.Get(type, LocalPlayer, 0, out flipX)
                : null;
            bool haveArt = sprite != null;
            _ghost.sprite = haveArt ? sprite : _blank;
            _ghost.flipX = flipX;
            if (!haveArt)
                _ghost.transform.localScale = new Vector3(size, size, 1f); // quad = footprint
            else
                _ghost.transform.localScale = Vector3.one;

            // Position exactly where the finished building will render: sprite
            // pivot (0.5,0.5) centered on the footprint.
            float halfW = size * 0.5f;
            _ghost.transform.position = new Vector3(tileX + halfW, _mapHeight - tileY - halfW, 0f);
            // One rule, shared with the sim (BuildSite) — the ghost must never
            // promise a site TickBuilderWalk will then reject.
            _ghost.color = BuildSite.IsValid(state, type, tileX, tileY)
                ? ValidTint : InvalidTint;
            _ghost.enabled = true;
        }
    }
}
