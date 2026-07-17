using System.Collections.Generic;
using Craftwar.Sim.Pud;

namespace Craftwar.Import.War2
{
    public struct DecodedTile
    {
        public ushort TileId;   // the raw 16-bit MTXM value — the key everywhere
        public byte[] Pixels;   // 32*32 RGBA bytes, row-major
    }

    /// <summary>
    /// Terrain tileset decoder. Ported from war2tools' libwar2 tileset.c (MIT).
    ///
    /// Each era owns 7 consecutive archive entries; we use the first four:
    /// A = palette, B = minitile info (16 u16 words per 32x32 megatile:
    /// bit0 flip_y, bit1 flip_x, (word &amp; 0xFFFC)*16 = byte offset into C),
    /// C = raw 8x8 minitile pixels (64 bytes each, palette indices),
    /// D = megatile map (42-byte chunk per tile group: 16 u16 variation
    /// megatile numbers + 10 bytes editor data).
    ///
    /// A 16-bit MTXM tile id addresses D directly:
    /// offset = (id >> 4) * 42 + (id &amp; 0xF) * 2.
    /// </summary>
    public sealed class War2Tileset
    {
        public const int TileSize = 32;

        readonly byte[] _info;   // entry B
        readonly byte[] _pixels; // entry C
        readonly byte[] _map;    // entry D
        readonly Rgba[] _palette;

        public War2Tileset(byte[] infoEntry, byte[] pixelsEntry, byte[] mapEntry, Rgba[] palette)
        {
            _info = infoEntry;
            _pixels = pixelsEntry;
            _map = mapEntry;
            _palette = palette;
        }

        /// <summary>First of the era's 7 consecutive entries holding tile info (entry "B").</summary>
        public static int InfoEntryForEra(PudEra era) => War2Palette.EntryForEra(era) + 1;

        public static War2Tileset Load(War2Archive archive, PudEra era)
        {
            int paletteEntry = War2Palette.EntryForEra(era);
            var palette = War2Palette.Decode(archive.ExtractEntry(paletteEntry));
            return new War2Tileset(
                archive.ExtractEntry(paletteEntry + 1),
                archive.ExtractEntry(paletteEntry + 2),
                archive.ExtractEntry(paletteEntry + 3),
                palette);
        }

        /// <summary>
        /// Decode every tile id the format can address, in the same sweep
        /// order as the original tool: solid tiles 0x010-0x0CF, then boundary
        /// tiles 0x100-0x9DF. Unused ids (megatile 0 / black) are skipped.
        /// </summary>
        public List<DecodedTile> DecodeAll()
        {
            var tiles = new List<DecodedTile>(1024);

            for (int j = 0x1; j <= 0xC; j++)
                for (int i = 0x0; i <= 0xF; i++)
                    TryDecode((ushort)((j << 4) | i), tiles);

            for (int j = 0x1; j <= 0x9; j++)
                for (int i = 0x0; i <= 0xD; i++)
                    for (int k = 0x0; k <= 0xF; k++)
                        TryDecode((ushort)((j << 8) | (i << 4) | k), tiles);

            return tiles;
        }

        static readonly int[] Flip = { 7, 6, 5, 4, 3, 2, 1, 0 };

        void TryDecode(ushort tileId, List<DecodedTile> output)
        {
            int mapOffset = ((tileId >> 4) * 42) + ((tileId & 0xF) * 2);
            if (mapOffset + 2 > _map.Length)
                return;

            int megatile = _map[mapOffset] | (_map[mapOffset + 1] << 8);
            if (megatile == 0)
                return; // variation unused
            int infoBase = megatile * 32;
            if (infoBase + 32 > _info.Length)
                return;

            var rgba = new byte[TileSize * TileSize * 4];
            for (int part = 0; part < 16; part++)
            {
                int word = _info[infoBase + part * 2] | (_info[infoBase + part * 2 + 1] << 8);
                bool flipY = (word & 1) != 0;
                bool flipX = (word & 2) != 0;
                int src = (word & 0xFFFC) * 16;

                int baseX = (part % 4) * 8;
                int baseY = (part / 4) * 8;
                for (int y = 0; y < 8; y++)
                {
                    int sy = flipY ? Flip[y] : y;
                    for (int x = 0; x < 8; x++)
                    {
                        int sx = flipX ? Flip[x] : x;
                        Rgba c = _palette[_pixels[src + sx + sy * 8]];
                        int o = ((baseY + y) * TileSize + baseX + x) * 4;
                        rgba[o] = c.R;
                        rgba[o + 1] = c.G;
                        rgba[o + 2] = c.B;
                        rgba[o + 3] = c.A;
                    }
                }
            }

            // libwar2 skips tiles whose first pixel decodes to pure black.
            if (rgba[0] == 0 && rgba[1] == 0 && rgba[2] == 0)
                return;

            output.Add(new DecodedTile { TileId = tileId, Pixels = rgba });
        }
    }
}
