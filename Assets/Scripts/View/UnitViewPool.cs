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

        /// <summary>Single-player pause: the driver stops advancing the sim.
        /// Networked lockstep (M10) cannot pause this way.</summary>
        bool Paused { get; }
        void SetPaused(bool paused);

        /// <summary>False in a networked match, where one peer cannot stop the
        /// world. Screens that pause as a side effect must check this.</summary>
        bool CanPauseLocally { get; }

        /// <summary>One line of connection state for the debug overlay, or null
        /// in single player. A string rather than the driver itself so the view
        /// keeps knowing nothing about the net layer.</summary>
        string NetStatusLine { get; }
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
        /// <summary>Which blocks are walk / attack / death for this bank. Invalid
        /// for single-pose banks; see <see cref="AnimLayout"/>.</summary>
        AnimLayout LayoutFor(ushort typeId, byte carry);
        /// <summary>Raw frame count for a single-pose (building) bank; 0 for
        /// animated unit banks (use BlockCount for those). WC2 building GRPs
        /// hold [0] = completed, [last] = half-built construction frame.</summary>
        int BuildingFrameCount(ushort typeId, byte player);
        /// <summary>Raw frame from a single-pose (building) bank by index,
        /// clamped to range. 0 = completed sprite, higher = construction
        /// stages.</summary>
        Sprite GetBuildingFrame(ushort typeId, byte player, int frameIndex, out bool flipX);
        /// <summary>The shared building-site art (WC2's build_1 bank): stage 0 is
        /// broken ground, stage 1 the stacked timber. Null when unavailable.</summary>
        Sprite GetFoundationFrame(int stage);
        /// <summary>A frame of the shared corpse bank. Null when unavailable.</summary>
        Sprite GetCorpseFrame(int block, byte facing, out bool flipX);
        /// <summary>Number of 5-facing blocks in the corpse bank, 0 if absent.</summary>
        int CorpseBlockCount { get; }
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
        readonly Dictionary<int, SpriteRenderer> _selBoxes = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> _shadows = new Dictionary<int, SpriteRenderer>();

        // Sorting bands. Ground units interleave by screen row; flyers sit in
        // their own band above all of them, still below projectiles (20000)
        // and fog (25000).
        //
        // Scenery sits below every unit but above the terrain (order 0): an oil
        // patch is flat on the water and ships sail *over* it, so it must never
        // win the row tie-break against a hull crossing its top row.
        const int SceneryBand = 500;
        const int GroundBand = 1000;
        const int AirBand = 10000;
        /// <summary>World units a flyer is drawn above its sim position.</summary>
        const float AirLift = 0.55f;
        static readonly Color ShadowTint = new Color(0f, 0f, 0f, 0.35f);
        readonly HashSet<int> _live = new HashSet<int>();
        readonly List<int> _toRemove = new List<int>();

        /// <summary>The local player, until multiplayer picks a slot at M10.</summary>
        /// <summary>The seat this client drives. See <see cref="HudScreen.LocalPlayer"/>.</summary>
        static byte LocalPlayer => HudScreen.LocalPlayer;

        /// <summary>
        /// Last-seen appearance of an enemy building, keyed by the tile index of
        /// its top-left corner. The original keeps showing buildings you have
        /// scouted, so a building destroyed (or newly raised) inside fog must
        /// not update until you look again — which is exactly why this is a
        /// snapshot rather than "keep drawing the live unit".
        /// </summary>
        struct BuildingMemory
        {
            public Sprite Sprite;
            public bool FlipX;
            public Vector3 Position;
            public int SortingOrder;
        }

        readonly Dictionary<int, BuildingMemory> _memory = new Dictionary<int, BuildingMemory>();
        readonly Dictionary<int, SpriteRenderer> _ghosts = new Dictionary<int, SpriteRenderer>();
        readonly HashSet<int> _seenOrigins = new HashSet<int>();
        readonly List<int> _memoryToRemove = new List<int>();
        static readonly Color GhostTint = new Color(1f, 1f, 1f, 0.85f);
        Sprite _fallback;
        Sprite _selBoxSprite;
        static readonly Color SelectionGreen = new Color(0.16f, 0.9f, 0.22f, 1f);
        static readonly Color SelectionRed = new Color(0.9f, 0.2f, 0.18f, 1f);
        static readonly Color SelectionYellow = new Color(0.92f, 0.8f, 0.24f, 1f);

        /// <summary>Shared with input and the UI; the pool only reads it.</summary>
        public SelectionState Selected { get; private set; }

        public void Init(ISimHost host, IUnitSpriteProvider sprites, int mapHeight, SelectionState selection)
        {
            _host = host;
            _sprites = sprites;
            _mapHeight = mapHeight;
            Selected = selection;

            var tex = new Texture2D(24, 24, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[24 * 24];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(220, 220, 220, 255);
            tex.SetPixels32(px);
            tex.Apply();
            _fallback = Sprite.Create(tex, new Rect(0, 0, 24, 24), new Vector2(0.5f, 0.5f), 32);

            // Hollow 1 px frame, 9-sliced so the border stays 1 px at any
            // size — the WC2 selection rectangle.
            var boxTex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var bp = new Color32[16];
            for (int i = 0; i < bp.Length; i++)
            {
                int bx = i % 4, by = i / 4;
                bool edge = bx == 0 || by == 0 || bx == 3 || by == 3;
                bp[i] = edge ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            boxTex.SetPixels32(bp);
            boxTex.Apply();
            _selBoxSprite = Sprite.Create(boxTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f),
                SimConstants.TilePixels, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
        }

        readonly Dictionary<int, SpriteRenderer> _projectileViews = new Dictionary<int, SpriteRenderer>();

        /// <summary>
        /// A unit that has left the sim but is still on screen: first its own
        /// death animation, then — for the types that leave one — a corpse out of
        /// the shared bank, rotting through the decay blocks before it fades.
        /// </summary>
        struct Corpse
        {
            public SpriteRenderer Renderer;
            public float DiedAt;
            public ushort TypeId;
            public byte Player;
            public byte Facing;
            public AnimLayout Layout;
            public CorpseKind Kind;
        }

        readonly List<Corpse> _corpses = new List<Corpse>();
        readonly Dictionary<int, (ushort typeId, byte player, byte facing)> _lastPose
            = new Dictionary<int, (ushort, byte, byte)>();
        Sprite _projectileSprite;

        /// <summary>Seconds per frame of a death animation.</summary>
        const float DeathFrameSeconds = 0.12f;
        /// <summary>Fade for something whose bank has no death frames at all.</summary>
        const float NoDeathFadeSeconds = 0.6f;
        /// <summary>How long a corpse lies there before it has finished rotting.</summary>
        const float CorpseSeconds = 30f;
        /// <summary>Tail of <see cref="CorpseSeconds"/> spent fading out.</summary>
        const float CorpseFadeSeconds = 3f;

        /// <summary>
        /// The corpse bank's blocks: [0] a fresh human body, [1] a fresh orc one,
        /// then four shared stages of decay down to scattered bone, and [6] the
        /// spreading ring of water a hull leaves. A land corpse therefore walks
        /// 0 (or 1) then 2, 3, 4, 5; a wreck holds 6 and just fades.
        /// </summary>
        const int CorpseHumanBlock = 0;
        const int CorpseOrcBlock = 1;
        const int CorpseDecayFirst = 2;
        const int CorpseDecayLast = 5;
        const int CorpseShipBlock = 6;

        /// <summary>
        /// A flyer's shadow: the same sprite flattened onto the deck, tinted
        /// black, drawn just under the ground band so it never covers a unit.
        /// Ground units have none — the original draws them at rest on the tile.
        /// </summary>
        void UpdateShadow(int slot, SpriteRenderer unitSr, bool airborne,
            float worldX, float worldY)
        {
            if (!airborne)
            {
                if (_shadows.TryGetValue(slot, out var stale) && stale != null)
                    stale.enabled = false;
                return;
            }

            if (!_shadows.TryGetValue(slot, out var sh) || sh == null)
            {
                var go = new GameObject($"shadow_{slot}");
                go.transform.SetParent(transform, false);
                sh = go.AddComponent<SpriteRenderer>();
                _shadows[slot] = sh;
            }

            sh.sprite = unitSr.sprite;
            sh.flipX = unitSr.flipX;
            sh.color = ShadowTint;
            sh.transform.position = new Vector3(worldX, worldY, 0f);
            sh.sortingOrder = GroundBand - 1;
            sh.enabled = true;
        }

        /// <summary>
        /// Which frame block to draw this instant, from the bank's real layout
        /// (see <see cref="AnimLayout"/>). Returns -1 for single-pose banks.
        /// </summary>
        int PickAnimBlock(ref Unit u, GameState state, in AnimLayout layout)
        {
            if (!layout.IsValid)
                return _sprites.BlockCount(u.TypeId, u.Player) > 0 ? 0 : -1;

            // Attacking / chopping: play the attack blocks while the swing timer
            // is fresh (first ~40% of the cooldown window). Banks with no attack
            // art (ships, submarines) fall through to the gait.
            bool swinging = u.Cooldown > SimConstants.AttackCooldownTicks * 3 / 5
                || u.Harvest == HarvestStage.Chopping;
            if (swinging && layout.HasAttack)
                return layout.AttackBlock((int)(Time.time * 10f));

            // Walking: only while actually mid-step. A unit that is merely
            // holding a path (blocked, waiting, or between orders) stands;
            // otherwise stuck units tread air.
            if (u.IsMoving)
                return layout.WalkBlock((int)(Time.time * 9f));

            return layout.WalkBlock(0); // stand
        }

        /// <summary>
        /// Construction-site rendering for a building carrying the
        /// UnderConstruction flag, following the original's own three-stage rule
        /// (PSX <c>unit.c</c>, <c>update_frame</c>, where <c>unitMP</c> is percent
        /// complete):
        ///
        ///     &lt; 25%  -> foundation frame 0   (broken ground)
        ///     &lt; 50%  -> foundation frame 1   (stacked timber)
        ///     &lt; 100% -> the building's own frame 1, "almost complete"
        ///     done    -> the building's frame 0
        ///
        /// The foundation frames are the shared `build_1` bank, not the
        /// building's art — that is the small pile of materials every WC2
        /// building starts as, and it is why the site used to look like a
        /// faded copy of the finished structure.
        ///
        /// Nothing here is translucent. The old fade was standing in for the
        /// site art; with the real bank there is nothing left to stand in for,
        /// and a completed building must never be drawn before it is complete.
        /// </summary>
        Sprite ConstructionSprite(ref Unit u, GameState state, Sprite fallbackSprite,
            ref Color color, ref bool flipX)
        {
            ref var row = ref state.Rules.Units[u.TypeId];
            int total = GameSim.BuildTicksFor(row.BuildTime);
            if (total < 1) total = 1;
            float progress = Mathf.Clamp01(1f - (float)u.TrainTicks / total);

            if (progress < 0.5f)
            {
                var site = _sprites?.GetFoundationFrame(progress < 0.25f ? 0 : 1);
                if (site != null)
                {
                    flipX = false;
                    return site;
                }
            }

            // The building's own scaffold frame. WC2 building banks hold
            // [0] completed and [last] under-construction.
            int frames = _sprites != null ? _sprites.BuildingFrameCount(u.TypeId, u.Player) : 0;
            if (frames < 2)
            {
                // Single-frame bank and no site art: a tint is all that is left
                // to say "not finished".
                color.a = 0.55f;
                return fallbackSprite;
            }
            return _sprites.GetBuildingFrame(u.TypeId, u.Player, frames - 1, out flipX);
        }

        void LateUpdate()
        {
            if (_host?.Sim == null)
                return;
            var state = _host.Sim.State;
            float alpha = _host.Alpha;
            _live.Clear();
            _seenOrigins.Clear();
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

                // Flyers ride above the battlefield: lifted a little, drawn in
                // their own band so they never interleave with ground units, and
                // given a shadow on the deck. Purely presentational — the sim
                // has no notion of altitude (and could not: floats are banned).
                bool airborne = state.DomainOf(u.TypeId) == MoveDomain.Air;
                sr.transform.position = new Vector3(
                    worldX, worldY + (airborne ? AirLift : 0f), 0f);
                // Pass-through scenery (the oil patch) uses the same rule as the
                // sim: if it does not block movement, things move over it.
                int band = airborne ? AirBand
                    : state.BlocksMovement(u.TypeId) ? GroundBand
                    : SceneryBand;
                sr.sortingOrder = Mathf.RoundToInt(pixY) + band;

                bool flipX = false;
                Sprite sprite = null;
                if (_sprites != null && _sprites.Has(u.TypeId))
                {
                    var layout = _sprites.LayoutFor(u.TypeId, (byte)u.Carry);
                    int block = PickAnimBlock(ref u, state, layout);
                    sprite = block >= 0
                        ? _sprites.GetAnimFrame(u.TypeId, u.Player, u.Facing, block, (byte)u.Carry, out flipX)
                        : _sprites.Get(u.TypeId, u.Player, u.Facing, out flipX);
                }

                uint packed = new UnitId((ushort)i, u.Gen).Packed;
                Color baseColor = Color.white;
                if ((u.Flags & UnitFlags.UnderConstruction) != 0)
                    sprite = ConstructionSprite(ref u, state, sprite, ref baseColor, ref flipX);
                if ((u.Flags & UnitFlags.Hidden) != 0)
                    baseColor.a = 0f;    // inside a mine/depot/site

                sr.sprite = sprite != null ? sprite : _fallback;
                sr.flipX = flipX;
                sr.color = baseColor;
                UpdateShadow(i, sr, airborne && baseColor.a > 0f, worldX, worldY);
                _lastPose[i] = (u.TypeId, u.Player, u.Facing);

                // Fog: own units are always drawn; everyone else only while in
                // sight. Buildings additionally leave a remembered ghost behind.
                bool inSight = u.Player == LocalPlayer
                    || GameplaySettings.Current.revealMap
                    || _host.Sim.IsUnitVisible(LocalPlayer, ref u);
                sr.enabled = inSight;

                if (u.Player != LocalPlayer && (u.Flags & UnitFlags.Building) != 0)
                {
                    int origin = OriginIndex(state, ref u);
                    if (origin >= 0 && inSight)
                    {
                        _seenOrigins.Add(origin);
                        _memory[origin] = new BuildingMemory
                        {
                            Sprite = sr.sprite,
                            FlipX = flipX,
                            Position = sr.transform.position,
                            SortingOrder = sr.sortingOrder,
                        };
                    }
                }

                UpdateSelectionBox(i, sr, footprint,
                    inSight && Selected.Contains(packed) && (u.Flags & UnitFlags.Hidden) == 0,
                    SelectionColor(u.Player));
            }

            UpdateRememberedBuildings(state);

            // Shadows belong to live flyers only; drop the rest.
            _toRemove.Clear();
            foreach (var kv in _shadows)
                if (!_live.Contains(kv.Key))
                    _toRemove.Add(kv.Key);
            foreach (int key in _toRemove)
            {
                if (_shadows[key] != null)
                    Destroy(_shadows[key].gameObject);
                _shadows.Remove(key);
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
                if (_selBoxes.TryGetValue(key, out var box))
                {
                    if (box != null)
                        Destroy(box.gameObject);
                    _selBoxes.Remove(key);
                }
                if (_shadows.TryGetValue(key, out var shadow))
                {
                    if (shadow != null)
                        Destroy(shadow.gameObject);
                    _shadows.Remove(key);
                }
                sr.gameObject.name = "corpse";
                sr.color = Color.white;
                sr.enabled = true;
                // The living walk over the dead: drop out of the ground band so
                // a body never wins the row tie-break against a unit.
                sr.sortingOrder -= GroundBand;
                var pose = _lastPose.TryGetValue(key, out var p) ? p : ((ushort)0, (byte)0, (byte)0);
                _lastPose.Remove(key);
                _corpses.Add(new Corpse
                {
                    Renderer = sr,
                    DiedAt = Time.time,
                    TypeId = pose.Item1,
                    Player = pose.Item2,
                    Facing = pose.Item3,
                    Layout = _sprites != null
                        ? _sprites.LayoutFor(pose.Item1, 0) : default,
                    Kind = UnitCorpseTable.For((UnitTypeId)pose.Item1),
                });
            }
        }

        /// <summary>Tile index of a unit's top-left corner, or -1 without a map.</summary>
        static int OriginIndex(GameState state, ref Unit u)
        {
            if (state.Terrain == null)
                return -1;
            return u.TileY * state.Terrain.Width + u.TileX;
        }

        /// <summary>
        /// Draws the last-seen sprite of enemy buildings on explored-but-fogged
        /// ground, and forgets a building the moment we look at its tile again
        /// and find it gone.
        /// </summary>
        void UpdateRememberedBuildings(GameState state)
        {
            if (state.Terrain == null)
                return;
            var sim = _host.Sim;
            int width = state.Terrain.Width;

            _memoryToRemove.Clear();
            foreach (var kv in _memory)
            {
                int origin = kv.Key;
                int tx = origin % width;
                int ty = origin / width;
                bool visible = GameplaySettings.Current.revealMap
                    || sim.IsVisible(LocalPlayer, tx, ty);

                // We can see that tile and no building reported itself there
                // this frame: whatever we remembered is gone.
                if (visible && !_seenOrigins.Contains(origin))
                {
                    _memoryToRemove.Add(origin);
                    continue;
                }

                // The real renderer takes over while the building is in sight.
                bool showGhost = !visible && sim.IsExplored(LocalPlayer, tx, ty);
                if (!_ghosts.TryGetValue(origin, out var ghost) || ghost == null)
                {
                    if (!showGhost)
                        continue;
                    var go = new GameObject("remembered_building");
                    go.transform.SetParent(transform, false);
                    ghost = go.AddComponent<SpriteRenderer>();
                    _ghosts[origin] = ghost;
                }

                ghost.enabled = showGhost;
                if (!showGhost)
                    continue;
                var mem = kv.Value;
                ghost.sprite = mem.Sprite;
                ghost.flipX = mem.FlipX;
                ghost.transform.position = mem.Position;
                ghost.sortingOrder = mem.SortingOrder;
                ghost.color = GhostTint;
            }

            foreach (int origin in _memoryToRemove)
            {
                _memory.Remove(origin);
                if (_ghosts.TryGetValue(origin, out var ghost))
                {
                    if (ghost != null)
                        Destroy(ghost.gameObject);
                    _ghosts.Remove(origin);
                }
            }
        }

        /// <summary>
        /// Green for ours, yellow for neutral, red for everyone else — the same
        /// three-way split the minimap makes, so an inspected enemy reads as
        /// "not yours" at a glance.
        /// </summary>
        static Color SelectionColor(byte player) =>
            player == LocalPlayer ? SelectionGreen
            : player >= SimConstants.MaxPlayers ? SelectionYellow
            : SelectionRed;

        void UpdateSelectionBox(int slot, SpriteRenderer unitSr, int footprint,
            bool selected, Color color)
        {
            if (!selected)
            {
                if (_selBoxes.TryGetValue(slot, out var existing) && existing != null)
                    existing.enabled = false;
                return;
            }

            if (!_selBoxes.TryGetValue(slot, out var box) || box == null)
            {
                var go = new GameObject("selection");
                go.transform.SetParent(unitSr.transform, false);
                box = go.AddComponent<SpriteRenderer>();
                box.sprite = _selBoxSprite;
                box.drawMode = SpriteDrawMode.Sliced;
                _selBoxes[slot] = box;
            }
            box.color = color;
            box.enabled = true;
            box.size = new Vector2(footprint, footprint);
            box.sortingOrder = unitSr.sortingOrder - 1; // frame sits under the sprite
        }

        /// <summary>
        /// Two phases, as in the original: the unit's own death frames play once
        /// at a fixed rate, and then — only for the types that leave a body —
        /// the shared corpse bank rots on the spot for half a minute before it
        /// fades. Everything else is gone the moment its death animation ends.
        /// </summary>
        void UpdateCorpses()
        {
            for (int i = _corpses.Count - 1; i >= 0; i--)
            {
                var corpse = _corpses[i];
                var sr = corpse.Renderer;
                float t = Time.time - corpse.DiedAt;
                float deathSeconds = corpse.Layout.DieSteps * DeathFrameSeconds;
                // Banks with no death frames at all — buildings, siege engines —
                // still get a moment to fade rather than blinking out.
                if (deathSeconds <= 0f && corpse.Kind == CorpseKind.None)
                    deathSeconds = NoDeathFadeSeconds;

                bool expired = corpse.Kind == CorpseKind.None
                    ? t >= deathSeconds
                    : t >= deathSeconds + CorpseSeconds;
                if (sr == null || expired)
                {
                    if (sr != null)
                        Destroy(sr.gameObject);
                    _corpses.RemoveAt(i);
                    continue;
                }

                float alpha = 1f;
                if (t < deathSeconds)
                {
                    // Death throes: the bank's own die blocks, once through.
                    if (_sprites != null && corpse.Layout.HasDeath)
                    {
                        int step = (int)(t / DeathFrameSeconds);
                        var sprite = _sprites.GetAnimFrame(corpse.TypeId, corpse.Player,
                            corpse.Facing, corpse.Layout.DieBlock(step), 0, out bool flip);
                        if (sprite != null)
                        {
                            sr.sprite = sprite;
                            sr.flipX = flip;
                        }
                    }
                    // A unit that leaves nothing fades out over its last moments
                    // rather than blinking away.
                    if (corpse.Kind == CorpseKind.None)
                        alpha = Mathf.Clamp01((deathSeconds - t)
                            / Mathf.Min(deathSeconds, NoDeathFadeSeconds));
                }
                else
                {
                    float rot = t - deathSeconds;
                    var sprite = CorpseSprite(ref corpse, rot, out bool flip);
                    if (sprite != null)
                    {
                        sr.sprite = sprite;
                        sr.flipX = flip;
                    }
                    alpha = Mathf.Clamp01((CorpseSeconds - rot) / CorpseFadeSeconds);
                }

                var c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
        }

        /// <summary>
        /// The corpse block for a body that has been lying there
        /// <paramref name="rot"/> seconds: the fresh body for the first fifth of
        /// its life, then evenly through the shared decay stages. A wreck holds
        /// its single water block and only fades.
        /// </summary>
        Sprite CorpseSprite(ref Corpse corpse, float rot, out bool flipX)
        {
            flipX = false;
            if (_sprites == null || _sprites.CorpseBlockCount <= CorpseShipBlock)
                return null;

            if (corpse.Kind == CorpseKind.Ship)
                return _sprites.GetCorpseFrame(CorpseShipBlock, corpse.Facing, out flipX);

            int fresh = corpse.Kind == CorpseKind.Orc ? CorpseOrcBlock : CorpseHumanBlock;
            const int stages = CorpseDecayLast - CorpseDecayFirst + 2; // fresh + decay
            int stage = Mathf.Clamp((int)(rot / CorpseSeconds * stages), 0, stages - 1);
            int block = stage == 0 ? fresh : CorpseDecayFirst + stage - 1;
            return _sprites.GetCorpseFrame(block, corpse.Facing, out flipX);
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
