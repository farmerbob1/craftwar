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

        /// <summary>
        /// Archive entry for a unit/building's sprite bank (sprites.c entry
        /// switch). Era-dependent where the original art varies per tileset.
        /// Heroes and upgraded classes map onto their base art. 0 = no art.
        /// </summary>
        public static int EntryForUnit(ushort typeId, Craftwar.Sim.Pud.PudEra era)
        {
            int E(int forest, int winter, int wasteland, int swamp) => era switch
            {
                Craftwar.Sim.Pud.PudEra.Winter => winter,
                Craftwar.Sim.Pud.PudEra.Wasteland => wasteland,
                Craftwar.Sim.Pud.PudEra.Swamp => swamp,
                _ => forest,
            };

            return typeId switch
            {
                // Units
                0x00 => 45,  // footman
                0x01 => 46,  // grunt
                0x02 => 47,  // peasant
                0x03 => 48,  // peon
                0x04 => 49,  // ballista
                0x05 => 50,  // catapult
                0x06 => 51,  // knight
                0x07 => 52,  // ogre
                0x08 => 53,  // archer
                0x09 => 54,  // axethrower
                0x0a => 55,  // mage
                0x0b => 58,  // death knight
                0x0c => 51,  // paladin -> knight art
                0x0d => 52,  // ogre mage -> ogre art
                0x0e => 33,  // dwarves
                0x0f => 34,  // goblin sapper
                0x10 => 47,  // attack peasant
                0x11 => 48,  // attack peon
                0x12 => 53,  // ranger -> archer art
                0x13 => 54,  // berserker -> axethrower art
                0x14 => 53,  // Alleria
                0x15 => 58,  // Teron Gorefiend
                0x16 => 35,  // Kurdran
                0x17 => 52,  // Dentarg
                0x18 => 55,  // Khadgar
                0x19 => 46,  // Grom Hellscream
                0x1a => 59,  // human tanker
                0x1b => 60,  // orc tanker
                0x1c => 39,  // human transport
                0x1d => 40,  // orc transport
                0x1e => 61,  // elven destroyer
                0x1f => 62,  // troll destroyer
                0x20 => 41,  // battleship
                0x21 => 42,  // juggernaught
                0x23 => 36,  // Deathwing -> dragon art
                0x26 => E(43, 43, 182, 526),  // gnomish submarine
                0x27 => E(44, 44, 183, 527),  // giant turtle
                0x28 => 38,  // gnomish flying machine
                0x29 => 63,  // goblin zeppelin
                0x2a => 35,  // gryphon rider
                0x2b => 36,  // dragon
                0x2c => 51,  // Turalyon
                0x2d => 37,  // eye of kilrogg
                0x2e => 45,  // Danath
                0x2f => 46,  // Kargath Bladefist
                0x31 => 52,  // Cho'gall
                0x32 => 51,  // Lothar
                0x33 => 58,  // Gul'dan
                0x34 => 51,  // Uther Lightbringer
                0x35 => 54,  // Zul'jin
                0x37 => 69,  // skeleton
                0x38 => 70,  // daemon
                0x39 => E(64, 66, 65, 470), // critter per tileset
                0x69 => 64,  // sheep
                0x6a => 65,  // pig
                0x6b => 66,  // seal
                0x6c => 470, // red pig

                // Buildings (forest, winter, wasteland, swamp)
                0x3a => E(92, 134, 173, 479),   // farm
                0x3b => E(93, 135, 174, 480),   // pig farm
                0x3c => E(94, 136, 94, 481),    // human barracks
                0x3d => E(95, 137, 95, 482),    // orc barracks
                0x3e => E(96, 138, 96, 483),    // church
                0x3f => E(97, 139, 97, 484),    // altar of storms
                0x40 => E(98, 140, 98, 485),    // human scout tower
                0x41 => E(99, 141, 99, 486),    // orc scout tower
                0x42 => E(104, 146, 104, 491),  // stables
                0x43 => E(105, 147, 105, 492),  // ogre mound
                0x44 => E(90, 132, 90, 477),    // gnomish inventor
                0x45 => E(91, 133, 91, 478),    // goblin alchemist
                0x46 => E(88, 130, 88, 475),    // gryphon aviary
                0x47 => E(89, 131, 89, 476),    // dragon roost
                0x48 => E(108, 150, 108, 495),  // human shipyard
                0x49 => E(109, 151, 109, 496),  // orc shipyard
                0x4a => E(100, 142, 100, 487),  // town hall
                0x4b => E(101, 143, 101, 488),  // great hall
                0x4c => E(102, 144, 175, 489),  // elven lumber mill
                0x4d => E(103, 145, 176, 490),  // troll lumber mill
                0x4e => E(110, 152, 110, 497),  // human foundry
                0x4f => E(111, 153, 111, 498),  // orc foundry
                0x50 => E(84, 160, 84, 505),    // mage tower
                0x51 => E(85, 161, 85, 506),    // temple of the damned
                0x52 => E(106, 148, 106, 493),  // human blacksmith
                0x53 => E(107, 149, 107, 494),  // orc blacksmith
                0x54 => E(112, 154, 112, 499),  // human refinery
                0x55 => E(113, 155, 113, 500),  // orc refinery
                0x56 => E(114, 156, 177, 501),  // human oil well
                0x57 => E(115, 157, 178, 502),  // orc oil well
                0x58 => E(86, 128, 86, 473),    // keep
                0x59 => E(87, 129, 87, 474),    // stronghold
                0x5a => E(116, 158, 116, 503),  // castle
                0x5b => E(117, 159, 117, 504),  // fortress
                0x5c => E(119, 162, 179, 511),  // gold mine
                0x5d => E(118, 118, 180, 515),  // oil patch
                0x60 => E(80, 169, 80, 507),    // human guard tower
                0x61 => E(81, 170, 81, 508),    // orc guard tower
                0x62 => E(82, 171, 82, 509),    // human cannon tower
                0x63 => E(83, 172, 83, 510),    // orc cannon tower
                0x64 => E(166, 166, 166, 525),  // circle of power
                0x65 => E(167, 184, 185, 513),  // dark portal
                0x66 => E(181, 186, 181, 514),  // runestone

                _ => 0,
            };
        }

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
