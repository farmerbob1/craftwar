using System.Collections.Generic;
using Craftwar.Import.War2;
using Craftwar.Sim;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Runtime unit sprite provider: decodes unit sprite banks out of the
    /// archive on demand, one atlas per (unit entry, player color), and
    /// serves standing frames by facing. WC2 frames are grouped 5 per
    /// animation step (N, NE, E, SE, S); west-side facings mirror the east
    /// sprites.
    /// </summary>
    public sealed class UnitSpriteBank : IUnitSpriteProvider
    {
        readonly War2Archive _archive;
        readonly Rgba[] _palette;
        readonly PudEra _era;
        readonly Dictionary<uint, Sprite[]> _cache = new Dictionary<uint, Sprite[]>();

        public UnitSpriteBank(War2Archive archive, PudEra era)
        {
            _archive = archive;
            _era = era;
            _palette = War2Palette.Decode(archive.ExtractEntry(War2Palette.EntryForEra(era)));
        }

        public bool Has(ushort typeId) => War2Sprites.EntryForUnit(typeId, _era) != 0;

        public Sprite Get(ushort typeId, byte player, byte facing, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId, player);
            if (frames == null || frames.Length == 0)
                return null;

            // Buildings and other single-pose banks have only a handful of
            // frames (completed + construction stages); units carry 5-facing
            // animation blocks (25+ frames).
            if (frames.Length < 15)
                return frames[0];

            // Facings: N=0..NW=7; sprite rows store N,NE,E,SE,S (0-4).
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = spriteDir < frames.Length ? spriteDir : 0;
            return frames[index];
        }

        public int BlockCount(ushort typeId, byte player)
        {
            var frames = GetFrames(typeId, player);
            return frames == null || frames.Length < 15 ? 0 : frames.Length / 5;
        }

        /// <summary>
        /// Cargo sprite bank overrides (worker carrying gold/wood). Falls back
        /// to the base bank if the entry doesn't decode as a unit bank.
        /// </summary>
        static int CarryEntryOverride(ushort typeId, byte carry)
        {
            bool human = typeId is (ushort)Craftwar.Sim.UnitTypeId.Peasant
                or (ushort)Craftwar.Sim.UnitTypeId.AttackPeasant;
            bool orc = typeId is (ushort)Craftwar.Sim.UnitTypeId.Peon
                or (ushort)Craftwar.Sim.UnitTypeId.AttackPeon;
            if (!human && !orc)
                return 0;
            // Verified visually from maindat.war: 122/123 = wood-carrying
            // peasant/peon, 124/125 = gold-carrying peasant/peon.
            return carry switch
            {
                1 => human ? 124 : 125, // gold sack
                2 => human ? 122 : 123, // lumber bundle
                _ => 0,
            };
        }

        public Sprite GetAnimFrame(ushort typeId, byte player, byte facing, int block, byte carry, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId, player, carry);
            if (frames == null || frames.Length == 0)
                return null;
            if (frames.Length < 15)
                return frames[0];
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = block * 5 + spriteDir;
            if (index >= frames.Length)
                index = spriteDir;
            return frames[index];
        }

        readonly Dictionary<int, bool> _bankValidity = new Dictionary<int, bool>();

        /// <summary>Sniff whether an archive entry decodes as a multi-frame unit bank.</summary>
        bool LooksLikeUnitBank(int entry)
        {
            if (_bankValidity.TryGetValue(entry, out bool valid))
                return valid;
            valid = false;
            try
            {
                var data = _archive.ExtractEntry(entry);
                if (data != null && data.Length > 6)
                {
                    var bank = War2Sprites.Decode(data);
                    valid = bank.FrameCount >= 15 && bank.MaxWidth >= 16 && bank.MaxWidth <= 96;
                }
            }
            catch (War2FormatException) { }
            catch (System.IndexOutOfRangeException) { }
            _bankValidity[entry] = valid;
            return valid;
        }

        Sprite[] GetFrames(ushort typeId, byte player, byte carry = 0)
        {
            int entry = 0;
            if (carry != 0)
            {
                entry = CarryEntryOverride(typeId, carry);
                if (entry != 0 && !LooksLikeUnitBank(entry))
                    entry = 0; // wrong guess or absent: use base art
            }
            if (entry == 0)
                entry = War2Sprites.EntryForUnit(typeId, _era);
            if (entry == 0)
                return null;
            byte playerColor = (byte)(player < 8 ? player : 0);
            uint key = ((uint)entry << 8) | playerColor;
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var bank = War2Sprites.Decode(_archive.ExtractEntry(entry));
            int cols = Mathf.CeilToInt(Mathf.Sqrt(bank.FrameCount));
            int rows = (bank.FrameCount + cols - 1) / cols;
            int cw = bank.MaxWidth, ch = bank.MaxHeight;

            var atlas = new Texture2D(cols * cw, rows * ch, TextureFormat.RGBA32, false)
            {
                name = $"unit_{entry}_p{playerColor}",
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
                sprites[f].name = $"u{entry}_p{playerColor}_f{f}";
            }
            atlas.Apply(false, false);

            _cache[key] = sprites;
            return sprites;
        }
    }
}
