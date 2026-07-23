using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// Draws the local player's fog as a single quad stretched over the map,
    /// masked by a one-texel-per-tile texture. Bilinear filtering on that
    /// texture is what makes the fog edge soft instead of a 32px staircase.
    ///
    /// Pure projection: reads GameState.Visible/Explored and never writes them.
    /// Rows are flipped on upload (texture row 0 = bottom) so the mask lines up
    /// with the tilemap, which flips the same way (TilemapView.LoadMap).
    /// </summary>
    public sealed class FogOfWarView : MonoBehaviour
    {
        /// <summary>Above units (~pixY+1000) and projectiles (20000).</summary>
        const int FogSortingOrder = 25000;

        ISimHost _host;
        byte _player;
        int _width, _height;

        Texture2D _mask;
        Color32[] _pixels;
        Material _material;
        MeshRenderer _renderer;

        public void Init(ISimHost host, byte localPlayer, int width, int height)
        {
            _host = host;
            _player = localPlayer;
            _width = width;
            _height = height;

            _mask = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                // Bilinear is the whole point: it interpolates between tiles.
                filterMode = FilterMode.Bilinear,
                // Clamp so the map border doesn't sample the opposite edge.
                wrapMode = TextureWrapMode.Clamp,
            };
            _pixels = new Color32[width * height];

            var shader = Shader.Find("Craftwar/FogOfWar");
            if (shader == null)
            {
                Debug.LogError("[Craftwar] Craftwar/FogOfWar shader not found; fog disabled.");
                enabled = false;
                return;
            }

            _material = new Material(shader);
            _material.SetTexture("_MaskTex", _mask);

            BuildQuad();
            UploadMask();
        }

        /// <summary>
        /// One quad covering world (0,0)-(width,height). 1 tile = 1 world unit,
        /// matching CameraRig and the tilemap.
        /// </summary>
        void BuildQuad()
        {
            var mesh = new Mesh { name = "FogQuad" };
            mesh.vertices = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(_width, 0, 0),
                new Vector3(0, _height, 0),
                new Vector3(_width, _height, 0),
            };
            mesh.uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 1), new Vector2(1, 1),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.material = _material;
            _renderer.sortingOrder = FogSortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (_host?.Sim == null || _mask == null)
                return;
            // Reveal-map option: the sim's fog keeps computing (hashed state);
            // only the overlay stops drawing.
            bool reveal = GameplaySettings.Current.revealMap;
            if (_renderer != null)
                _renderer.enabled = !reveal;
            if (reveal)
                return;
            UploadMask();
        }

        void UploadMask()
        {
            var state = _host.Sim.State;
            if (state.Visible == null || _player >= SimConstants.MaxPlayers)
                return;
            byte[] visible = state.Visible[_player];
            byte[] explored = state.Explored[_player];
            if (visible == null)
                return;

            for (int y = 0; y < _height; y++)
            {
                int simRow = y * _width;
                // Flip: sim row 0 is the top, texture row 0 is the bottom.
                int texRow = (_height - 1 - y) * _width;
                for (int x = 0; x < _width; x++)
                {
                    byte v = visible[simRow + x];
                    byte e = explored != null ? explored[simRow + x] : (byte)0;
                    _pixels[texRow + x] = new Color32(
                        v != 0 ? (byte)255 : (byte)0,
                        e != 0 ? (byte)255 : (byte)0,
                        0, 255);
                }
            }

            _mask.SetPixels32(_pixels);
            _mask.Apply(updateMipmaps: false);
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            if (_mask != null)
                Destroy(_mask);
        }
    }
}
