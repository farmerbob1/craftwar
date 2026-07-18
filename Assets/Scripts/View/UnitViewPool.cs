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
        /// <summary>Frame at animation block*5 + facing; clamps to available frames.
        /// carry (CarryType) selects cargo sprite variants where they exist.</summary>
        Sprite GetAnimFrame(ushort typeId, byte player, byte facing, int block, byte carry, out bool flipX);
        /// <summary>Number of 5-facing animation blocks (0 for single-pose banks).</summary>
        int BlockCount(ushort typeId, byte player);
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

        readonly Dictionary<int, SpriteRenderer> _projectileViews = new Dictionary<int, SpriteRenderer>();
        readonly List<(SpriteRenderer sr, float diedAt, ushort typeId, byte player, byte facing)> _corpses
            = new List<(SpriteRenderer, float, ushort, byte, byte)>();
        readonly Dictionary<int, (ushort typeId, byte player, byte facing)> _lastPose
            = new Dictionary<int, (ushort, byte, byte)>();
        Sprite _projectileSprite;
        const float CorpseSeconds = 2f;

        /// <summary>
        /// WC2 frame convention: blocks of 5 facings. Blocks 0-4 = walk cycle
        /// (block 0 doubles as the stand pose), then attack blocks, with the
        /// death animation in the last blocks. Returns -1 for single-pose banks.
        /// </summary>
        int PickAnimBlock(ref Unit u, GameState state)
        {
            int blocks = _sprites.BlockCount(u.TypeId, u.Player);
            if (blocks <= 0)
                return -1;

            // Attacking / chopping: play attack blocks while the swing timer
            // is fresh (first ~40% of the cooldown window).
            bool swinging = u.Cooldown > SimConstants.AttackCooldownTicks * 3 / 5
                || u.Harvest == HarvestStage.Chopping;
            if (swinging && blocks > 6)
            {
                int attackStart = 5;
                int attackCount = Mathf.Max(1, Mathf.Min(4, blocks - 8));
                int step = (int)(Time.time * 10f) % attackCount;
                return attackStart + step;
            }

            // Walking: cycle blocks 0..4.
            if (u.IsMoving || u.PathLength > u.PathCursor)
            {
                int walkBlocks = Mathf.Min(5, blocks);
                return (int)(Time.time * 9f) % walkBlocks;
            }

            return 0; // stand
        }

        void LateUpdate()
        {
            if (_host?.Sim == null)
                return;
            var state = _host.Sim.State;
            float alpha = _host.Alpha;
            _live.Clear();
            UpdateProjectiles(state);
            UpdateCorpses();

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
                Sprite sprite = null;
                if (_sprites != null && _sprites.Has(u.TypeId))
                {
                    int block = PickAnimBlock(ref u, state);
                    sprite = block >= 0
                        ? _sprites.GetAnimFrame(u.TypeId, u.Player, u.Facing, block, (byte)u.Carry, out flipX)
                        : _sprites.Get(u.TypeId, u.Player, u.Facing, out flipX);
                }
                sr.sprite = sprite != null ? sprite : _fallback;
                sr.flipX = flipX;
                _lastPose[i] = (u.TypeId, u.Player, u.Facing);

                uint packed = new UnitId((ushort)i, u.Gen).Packed;
                Color baseColor = Selected.Contains(packed)
                    ? new Color(0.6f, 1f, 0.6f, 1f)
                    : Color.white;
                if ((u.Flags & UnitFlags.UnderConstruction) != 0)
                    baseColor.a = 0.55f; // scaffolding look until real stage frames
                if ((u.Flags & UnitFlags.Hidden) != 0)
                    baseColor.a = 0f;    // inside a mine/depot/site
                sr.color = baseColor;
            }

            _toRemove.Clear();
            foreach (var kv in _views)
                if (!_live.Contains(kv.Key))
                    _toRemove.Add(kv.Key);
            foreach (int key in _toRemove)
            {
                // Repurpose the dead unit's renderer to play the death
                // animation, then fade.
                var sr = _views[key];
                _views.Remove(key);
                sr.gameObject.name = "corpse";
                sr.color = Color.white;
                var pose = _lastPose.TryGetValue(key, out var p) ? p : ((ushort)0, (byte)0, (byte)0);
                _lastPose.Remove(key);
                _corpses.Add((sr, Time.time, pose.Item1, pose.Item2, pose.Item3));
            }
        }

        void UpdateCorpses()
        {
            for (int i = _corpses.Count - 1; i >= 0; i--)
            {
                var (sr, diedAt, typeId, player, facing) = _corpses[i];
                float t = Time.time - diedAt;
                if (sr == null || t >= CorpseSeconds)
                {
                    if (sr != null)
                        Destroy(sr.gameObject);
                    _corpses.RemoveAt(i);
                    continue;
                }

                int blocks = _sprites != null ? _sprites.BlockCount(typeId, player) : 0;
                if (blocks > 8 && t < 1f)
                {
                    // Death animation: the last ~3 blocks, once through.
                    int deathCount = Mathf.Min(3, blocks - 5);
                    int deathStart = blocks - deathCount;
                    int step = Mathf.Min(deathCount - 1, (int)(t * deathCount / 0.8f));
                    var sprite = _sprites.GetAnimFrame(typeId, player, facing, deathStart + step, 0, out bool flip);
                    if (sprite != null)
                    {
                        sr.sprite = sprite;
                        sr.flipX = flip;
                    }
                }
                var c = sr.color;
                c.a = Mathf.Clamp01((CorpseSeconds - t) / CorpseSeconds);
                sr.color = c;
            }
        }

        void UpdateProjectiles(GameState state)
        {
            if (_projectileSprite == null)
            {
                var tex = new Texture2D(6, 6, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                var px = new Color32[36];
                for (int i = 0; i < 36; i++)
                    px[i] = new Color32(255, 235, 160, 255);
                tex.SetPixels32(px);
                tex.Apply();
                _projectileSprite = Sprite.Create(tex, new Rect(0, 0, 6, 6), new Vector2(0.5f, 0.5f), 32);
            }

            for (int p = 0; p < state.Projectiles.Length; p++)
            {
                bool active = state.Projectiles[p].Active;
                if (active && !_projectileViews.TryGetValue(p, out _))
                {
                    var go = new GameObject($"projectile_{p}");
                    go.transform.SetParent(transform, false);
                    var psr = go.AddComponent<SpriteRenderer>();
                    psr.sprite = _projectileSprite;
                    psr.sortingOrder = 20000;
                    _projectileViews[p] = psr;
                }
                if (_projectileViews.TryGetValue(p, out var sr))
                {
                    if (!active)
                    {
                        Destroy(sr.gameObject);
                        _projectileViews.Remove(p);
                        continue;
                    }
                    ref var proj = ref state.Projectiles[p];
                    sr.transform.position = new Vector3(
                        proj.PixX / 32f, _mapHeight - proj.PixY / 32f, 0f);
                }
            }
        }
    }
}
