using System.Collections.Generic;
using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>What the view needs from whoever drives the sim.</summary>
    public interface ISimHost
    {
        GameSim Sim { get; }
        float Alpha { get; }
        int[] PrevPixX { get; }
        int[] PrevPixY { get; }
        void SubmitCommand(in GameCommand cmd);
    }

    /// <summary>Resolves unit sprites; implemented by the asset layer.</summary>
    public interface IUnitSpriteProvider
    {
        bool Has(ushort typeId);
        Sprite Get(ushort typeId, byte player, byte facing, out bool flipX);
    }

    /// <summary>
    /// Projects sim units into pooled SpriteRenderers, interpolating between
    /// the previous and current tick positions. Pure view: never mutates sim
    /// state. Units without decoded sprites yet render as colored quads so
    /// nothing is invisible.
    /// </summary>
    public sealed class UnitViewPool : MonoBehaviour
    {
        ISimHost _host;
        IUnitSpriteProvider _sprites;
        int _mapHeight;

        readonly Dictionary<int, SpriteRenderer> _views = new Dictionary<int, SpriteRenderer>();
        readonly HashSet<int> _live = new HashSet<int>();
        readonly List<int> _toRemove = new List<int>();
        Sprite _fallback;

        public readonly HashSet<uint> Selected = new HashSet<uint>();

        public void Init(ISimHost host, IUnitSpriteProvider sprites, int mapHeight)
        {
            _host = host;
            _sprites = sprites;
            _mapHeight = mapHeight;

            var tex = new Texture2D(24, 24, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[24 * 24];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(220, 220, 220, 255);
            tex.SetPixels32(px);
            tex.Apply();
            _fallback = Sprite.Create(tex, new Rect(0, 0, 24, 24), new Vector2(0.5f, 0.5f), 32);
        }

        void LateUpdate()
        {
            if (_host?.Sim == null)
                return;
            var state = _host.Sim.State;
            float alpha = _host.Alpha;
            _live.Clear();

            for (int i = 0; i < state.HighestUnitIndex; i++)
            {
                ref var u = ref state.Units[i];
                if (!u.IsAlive)
                    continue;
                _live.Add(i);

                if (!_views.TryGetValue(i, out var sr))
                {
                    var go = new GameObject($"unit_{i}");
                    go.transform.SetParent(transform, false);
                    sr = go.AddComponent<SpriteRenderer>();
                    _views[i] = sr;
                }

                float pixX = Mathf.Lerp(_host.PrevPixX[i], u.PixX, alpha);
                float pixY = Mathf.Lerp(_host.PrevPixY[i], u.PixY, alpha);
                int footprint = state.Footprint(u.TypeId);
                float half = footprint * 0.5f;
                float worldX = pixX / 32f + half;
                float worldY = _mapHeight - pixY / 32f - half;
                sr.transform.position = new Vector3(worldX, worldY, 0f);
                sr.sortingOrder = Mathf.RoundToInt(pixY) + 1000; // lower on screen draws on top

                bool flipX = false;
                Sprite sprite = _sprites != null && _sprites.Has(u.TypeId)
                    ? _sprites.Get(u.TypeId, u.Player, u.Facing, out flipX)
                    : null;
                sr.sprite = sprite != null ? sprite : _fallback;
                sr.flipX = flipX;

                uint packed = new UnitId((ushort)i, u.Gen).Packed;
                sr.color = Selected.Contains(packed)
                    ? new Color(0.6f, 1f, 0.6f, 1f)
                    : Color.white;
            }

            _toRemove.Clear();
            foreach (var kv in _views)
                if (!_live.Contains(kv.Key))
                    _toRemove.Add(kv.Key);
            foreach (int key in _toRemove)
            {
                Destroy(_views[key].gameObject);
                _views.Remove(key);
            }
        }
    }
}
