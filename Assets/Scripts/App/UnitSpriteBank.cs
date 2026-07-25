using System.Collections.Generic;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Runtime unit sprite provider: decodes unit sprite banks out of the
    /// installation on demand, one atlas per (sprite file, player color), and
    /// serves standing frames by facing. WC2 frames are grouped 5 per
    /// animation step (N, NE, E, SE, S); west-side facings mirror the east
    /// sprites.
    /// </summary>
    public sealed class UnitSpriteBank : IUnitSpriteProvider
    {
        readonly IAssetSource _source;
        readonly Rgba[] _palette;
        readonly PudEra _era;
        readonly Dictionary<string, Sprite[]> _cache = new Dictionary<string, Sprite[]>();

        public UnitSpriteBank(IAssetSource source, PudEra era)
        {
            _source = source;
            _era = era;

            string palettePath = $"art/bgs/{War2Palette.FolderForEra(era).ToLowerInvariant()}" +
                                 $"/{War2Palette.StemForEra(era)}.ppl";
            _palette = source.TryRead(palettePath, out var ppl)
                ? War2Palette.Decode(ppl)
                : null;
            if (_palette == null)
                Debug.LogError($"[Craftwar] Palette not found: {palettePath}");
        }

        public bool Has(ushort typeId) => War2Sprites.FileForUnit(typeId, _era) != null;

        /// <summary>Logical path for a sprite file relative to the install's Data folder.</summary>
        static string LogicalPath(string file) => "art/unit/" + file.ToLowerInvariant();

        public Sprite Get(ushort typeId, byte player, byte facing, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId, player);
            if (frames == null || frames.Length == 0)
                return null;

            if (!LayoutFor(typeId, 0).IsValid)
                return frames[0]; // single-pose bank: buildings, scenery

            // Facings: N=0..NW=7; sprite rows store N,NE,E,SE,S (0-4).
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = spriteDir < frames.Length ? spriteDir : 0;
            return frames[index];
        }

        /// <summary>
        /// Whether a bank holds 5-facing animation blocks is decided by the
        /// animation table, not by frame count. A count threshold gets it wrong
        /// at both ends: the critter banks are ten frames (two blocks) and the
        /// eye of Kilrogg is five (one), so a "15 or more frames" rule filed all
        /// three under single-pose art and drew them permanently facing north,
        /// while a three-frame oil platform is genuinely single-pose.
        /// </summary>
        bool IsAnimated(ushort typeId, byte carry = 0) => LayoutFor(typeId, carry).IsValid;

        public int BlockCount(ushort typeId, byte player)
        {
            var frames = GetFrames(typeId, player);
            return frames == null || !IsAnimated(typeId) ? 0 : frames.Length / 5;
        }

        public AnimLayout LayoutFor(ushort typeId, byte carry)
        {
            // Carry art has its own bank (peong/peonl/tankero) with its own
            // layout, so resolve the same file the frames came from.
            string file = carry != 0 ? CarryFileOverride(typeId, carry) : null;
            if (file != null && !_source.Exists(LogicalPath(file)))
                file = null;
            return UnitAnimTable.ForFile(file ?? War2Sprites.FileForUnit(typeId, _era));
        }

        // --- shared banks: the construction site and the corpse ------------------

        /// <summary>
        /// The building-site art every structure passes through before its own
        /// scaffold frame. Winter has its own version; the other three eras
        /// share one. Two frames: broken ground, then stacked timber.
        /// </summary>
        string FoundationFile => _era == PudEra.Winter
            ? "Other/s_build1.grp"
            : "Other/build_1.grp";

        Sprite[] _foundation;
        bool _foundationTried;

        public Sprite GetFoundationFrame(int stage)
        {
            if (!_foundationTried)
            {
                _foundationTried = true;
                _foundation = DecodeShared(FoundationFile, "foundation");
            }
            if (_foundation == null || _foundation.Length == 0)
                return null;
            int i = stage < 0 ? 0 : stage >= _foundation.Length ? _foundation.Length - 1 : stage;
            return _foundation[i];
        }

        Sprite[] _corpse;
        bool _corpseTried;

        Sprite[] Corpse()
        {
            if (!_corpseTried)
            {
                _corpseTried = true;
                _corpse = DecodeShared("Other/death.grp", "corpse");
            }
            return _corpse;
        }

        public int CorpseBlockCount
        {
            get
            {
                var frames = Corpse();
                return frames == null ? 0 : frames.Length / 5;
            }
        }

        public Sprite GetCorpseFrame(int block, byte facing, out bool flipX)
        {
            flipX = false;
            var frames = Corpse();
            if (frames == null || frames.Length == 0)
                return null;
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = block * 5 + spriteDir;
            if (index < 0 || index >= frames.Length)
                index = spriteDir < frames.Length ? spriteDir : 0;
            return frames[index];
        }

        /// <summary>
        /// A bank that belongs to nobody: no team colour, one copy for the
        /// match. Returns null (with a warning) rather than throwing, so a
        /// partial installation still runs.
        /// </summary>
        Sprite[] DecodeShared(string file, string label)
        {
            if (_palette == null)
                return null;
            if (!_source.TryRead(LogicalPath(file), out var data))
            {
                Debug.LogWarning($"[Craftwar] {label} art not found: {file}");
                return null;
            }
            try
            {
                return BuildSprites(War2Sprites.Decode(data), file, playerColor: 0);
            }
            catch (War2FormatException e)
            {
                Debug.LogWarning($"[Craftwar] {label} decode failed: {e.Message}");
                return null;
            }
        }

        public int BuildingFrameCount(ushort typeId, byte player)
        {
            var frames = GetFrames(typeId, player);
            // Animated banks aren't buildings: report 0 so the view uses
            // BlockCount instead. WC2 building GRPs carry 2 frames:
            // [0] completed, [1] half-built construction frame.
            return frames == null || IsAnimated(typeId) ? 0 : frames.Length;
        }

        public Sprite GetBuildingFrame(ushort typeId, byte player, int frameIndex, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId, player);
            if (frames == null || frames.Length == 0)
                return null;
            int idx = frameIndex < 0 ? 0
                : frameIndex >= frames.Length ? frames.Length - 1
                : frameIndex;
            return frames[idx];
        }

        /// <summary>
        /// Art for a unit that is carrying something, or null for the base art.
        ///
        /// The loose filenames say outright what M4 and M7 had to establish by
        /// eye: "g" is gold, "l" is lumber, "o" is oil. That also independently
        /// confirms the laden-tanker banks, which M7 could only identify by
        /// silhouette comparison — Human/tankero.grp decodes pixel-identically to
        /// entry 126 and Orc/tankero.grp to 127, exactly as guessed.
        /// </summary>
        static string CarryFileOverride(ushort typeId, byte carry)
        {
            if (typeId == (ushort)Craftwar.Sim.UnitTypeId.HumanTanker)
                return carry == 3 ? "Human/tankero.grp" : null;
            if (typeId == (ushort)Craftwar.Sim.UnitTypeId.OrcTanker)
                return carry == 3 ? "Orc/tankero.grp" : null;

            bool human = typeId is (ushort)Craftwar.Sim.UnitTypeId.Peasant
                or (ushort)Craftwar.Sim.UnitTypeId.AttackPeasant;
            bool orc = typeId is (ushort)Craftwar.Sim.UnitTypeId.Peon
                or (ushort)Craftwar.Sim.UnitTypeId.AttackPeon;
            if (!human && !orc)
                return null;

            // Race is the folder; the stem is shared between both races.
            string folder = human ? "Human/" : "Orc/";
            return carry switch
            {
                1 => folder + "peong.grp", // gold sack
                2 => folder + "peonl.grp", // lumber bundle
                _ => null,
            };
        }

        public Sprite GetAnimFrame(ushort typeId, byte player, byte facing, int block, byte carry, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId, player, carry);
            if (frames == null || frames.Length == 0)
                return null;
            if (!IsAnimated(typeId, carry))
                return frames[0];
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = block * 5 + spriteDir;
            if (index >= frames.Length)
                index = spriteDir;
            return frames[index];
        }

        Sprite[] GetFrames(ushort typeId, byte player, byte carry = 0)
        {
            // Carry art is optional: fall back to the base bank if it is absent
            // rather than rendering nothing.
            string file = carry != 0 ? CarryFileOverride(typeId, carry) : null;
            if (file != null && !_source.Exists(LogicalPath(file)))
                file = null;
            if (file == null)
                file = War2Sprites.FileForUnit(typeId, _era);
            if (file == null || _palette == null)
                return null;

            byte playerColor = (byte)(player < 8 ? player : 0);
            string key = file + "#" + playerColor;
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            if (!_source.TryRead(LogicalPath(file), out var data))
            {
                Debug.LogWarning($"[Craftwar] Sprite not found: {file}");
                _cache[key] = null;
                return null;
            }

            SpriteBank bank;
            try
            {
                bank = War2Sprites.Decode(data);
            }
            catch (War2FormatException e)
            {
                Debug.LogWarning($"[Craftwar] Sprite decode failed for {file}: {e.Message}");
                _cache[key] = null;
                return null;
            }

            var sprites = BuildSprites(bank, key, playerColor);
            _cache[key] = sprites;
            return sprites;
        }

        /// <summary>Pack a decoded bank into one point-filtered atlas, one sprite
        /// per frame, each sized to the bank's full frame box so a sprite's pivot
        /// is the same place in every frame.</summary>
        Sprite[] BuildSprites(SpriteBank bank, string key, int playerColor)
        {
            int cols = Mathf.CeilToInt(Mathf.Sqrt(bank.FrameCount));
            int rows = (bank.FrameCount + cols - 1) / cols;
            int cw = bank.MaxWidth, ch = bank.MaxHeight;

            var atlas = new Texture2D(cols * cw, rows * ch, TextureFormat.RGBA32, false)
            {
                name = $"unit_{key}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = new Color32[atlas.width * atlas.height];
            atlas.SetPixels32(clear);

            var sprites = new Sprite[bank.FrameCount];
            for (int f = 0; f < bank.FrameCount; f++)
            {
                ref var frame = ref bank.Frames[f];
                byte[] rgba = War2Sprites.ToRgba(frame, _palette, playerColor);

                int cellX = (f % cols) * cw;
                int cellY = (f / cols) * ch;
                // Blit the frame at its box offset; source is top-down,
                // texture is bottom-up.
                var pixels = new Color32[frame.Width * frame.Height];
                for (int y = 0; y < frame.Height; y++)
                {
                    int srcRow = (frame.Height - 1 - y) * frame.Width;
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int s = (srcRow + x) * 4;
                        pixels[y * frame.Width + x] = new Color32(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
                    }
                }
                int px = cellX + frame.OffsetX;
                int py = cellY + (ch - frame.OffsetY - frame.Height);
                if (frame.Width > 0 && frame.Height > 0)
                    atlas.SetPixels32(px, py, frame.Width, frame.Height, pixels);

                sprites[f] = Sprite.Create(atlas,
                    new Rect(cellX, cellY, cw, ch),
                    new Vector2(0.5f, 0.5f),
                    SimConstants.TilePixels, 0, SpriteMeshType.FullRect);
                sprites[f].name = $"{key}_f{f}";
            }
            atlas.Apply(false, false);
            return sprites;
        }
    }
}
