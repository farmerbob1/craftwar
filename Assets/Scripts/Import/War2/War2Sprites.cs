using System.Collections.Generic;

namespace Craftwar.Import.War2
{
    public struct SpriteFrame
    {
        public byte OffsetX;    // draw offset within the maxW x maxH box
        public byte OffsetY;
        public byte Width;
        public byte Height;
        public byte[] Indices;  // Width*Height palette indices, 0xFF = transparent
    }

    public struct SpriteBank
    {
        public int FrameCount;
        public int MaxWidth;
        public int MaxHeight;
        public SpriteFrame[] Frames;
    }

    /// <summary>
    /// Unit/building sprite decoder, ported from war2tools' libwar2
    /// sprites.c (MIT). Entry: u16 frame count, u16 maxW, u16 maxH, then
    /// 8-byte frame headers (x,y,w,h,u32 dataStart). Frame rows are RLE:
    /// 0x80|n = n transparent, 0x40|n = repeat next byte n times, else n
    /// literal palette indices.
    /// Team color: palette indices 208-211 carry the 4-shade red ramp; other
    /// player colors substitute those indices before palette conversion.
    /// </summary>
    public static class War2Sprites
    {
        public const byte Transparent = 0xFF;

        // Era-independent land unit archive entries (sprites.c switch).
        public static int EntryForUnit(ushort typeId) => typeId switch
        {
            0x00 => 45, // footman
            0x01 => 46, // grunt
            0x02 => 47, // peasant
            0x03 => 48, // peon
            _ => 0,
        };

        /// <summary>The per-player 4-shade team color ramps (8-bit RGB), red first.</summary>
        public static readonly byte[,] TeamRamps =
        {
            { 0x44, 0x04, 0x00, 0x5C, 0x04, 0x00, 0x7C, 0x00, 0x00, 0xA4, 0x00, 0x00 }, // red
            { 0x00, 0x04, 0x4C, 0x00, 0x14, 0x6C, 0x00, 0x24, 0x94, 0x00, 0x3C, 0xC0 }, // blue
            { 0x00, 0x28, 0x0C, 0x04, 0x54, 0x2C, 0x14, 0x84, 0x5C, 0x2C, 0xB4, 0x94 }, // green
            { 0x2C, 0x08, 0x2C, 0x50, 0x10, 0x4C, 0x74, 0x30, 0x84, 0x98, 0x48, 0xB0 }, // violet
            { 0x6E, 0x20, 0x0C, 0x98, 0x38, 0x10, 0xC4, 0x58, 0x10, 0xF0, 0x84, 0x14 }, // orange
            { 0x0C, 0x0C, 0x14, 0x14, 0x14, 0x20, 0x1C, 0x1C, 0x2C, 0x28, 0x28, 0x3C }, // black
            { 0x24, 0x28, 0x4C, 0x54, 0x54, 0x80, 0x98, 0x98, 0xB4, 0xE0, 0xE0, 0xE0 }, // white
            { 0xB4, 0x74, 0x00, 0xCC, 0xA0, 0x10, 0xE4, 0xCC, 0x28, 0xFC, 0xFC, 0x48 }, // yellow
        };

        public static SpriteBank Decode(byte[] entry)
        {
            int count = entry[0] | (entry[1] << 8);
            var bank = new SpriteBank
            {
                FrameCount = count,
                MaxWidth = entry[2] | (entry[3] << 8),
                MaxHeight = entry[4] | (entry[5] << 8),
                Frames = new SpriteFrame[count],
            };

            for (int f = 0; f < count; f++)
            {
                int h = 6 + f * 8;
                var frame = new SpriteFrame
                {
                    OffsetX = entry[h],
                    OffsetY = entry[h + 1],
                    Width = entry[h + 2],
                    Height = entry[h + 3],
                };
                int dstart = entry[h + 4] | (entry[h + 5] << 8) | (entry[h + 6] << 16) | (entry[h + 7] << 24);

                frame.Indices = new byte[frame.Width * frame.Height];
                for (int i = 0; i < frame.Indices.Length; i++)
                    frame.Indices[i] = Transparent;

                for (int row = 0; row < frame.Height; row++)
                {
                    int rowOff = entry[dstart + row * 2] | (entry[dstart + row * 2 + 1] << 8);
                    int o = dstart + rowOff;
                    int x = 0;
                    while (x < frame.Width)
                    {
                        byte c = entry[o++];
                        if ((c & 0x80) != 0)
                        {
                            x += c & 0x7F; // transparent run
                        }
                        else if ((c & 0x40) != 0)
                        {
                            int n = c & 0x3F;
                            byte v = entry[o++];
                            for (int k = 0; k < n && x < frame.Width; k++)
                                frame.Indices[row * frame.Width + x++] = v;
                        }
                        else
                        {
                            for (int k = 0; k < c && x < frame.Width; k++)
                                frame.Indices[row * frame.Width + x++] = entry[o++];
                        }
                    }
                }
                bank.Frames[f] = frame;
            }
            return bank;
        }

        /// <summary>
        /// Convert a frame to RGBA (row-major, top-down) using the era
        /// palette, substituting the team ramp (palette 208-211) for the
        /// given player color (0-7).
        /// </summary>
        public static byte[] ToRgba(in SpriteFrame frame, Rgba[] palette, int playerColor)
        {
            var rgba = new byte[frame.Width * frame.Height * 4];
            for (int i = 0; i < frame.Indices.Length; i++)
            {
                byte idx = frame.Indices[i];
                if (idx == Transparent)
                    continue; // alpha stays 0
                Rgba c;
                if (idx >= War2Palette.TeamColorFirstIndex &&
                    idx < War2Palette.TeamColorFirstIndex + War2Palette.TeamColorRampSize &&
                    playerColor >= 0 && playerColor < 8)
                {
                    int shade = idx - War2Palette.TeamColorFirstIndex;
                    c = new Rgba
                    {
                        R = TeamRamps[playerColor, shade * 3],
                        G = TeamRamps[playerColor, shade * 3 + 1],
                        B = TeamRamps[playerColor, shade * 3 + 2],
                        A = 0xFF,
                    };
                }
                else
                {
                    c = palette[idx];
                }
                rgba[i * 4] = c.R;
                rgba[i * 4 + 1] = c.G;
                rgba[i * 4 + 2] = c.B;
                rgba[i * 4 + 3] = c.A;
            }
            return rgba;
        }
    }
}
