using Craftwar.Sim.Pud;

namespace Craftwar.Import.War2
{
    public struct Rgba
    {
        public byte R, G, B, A;

        public bool RgbEquals(in Rgba o) => R == o.R && G == o.G && B == o.B;
    }

    /// <summary>
    /// Era palettes from maindat.war. Ported from war2tools' libwar2 (MIT).
    /// 768-byte entries: 256 x RGB with 6-bit VGA DAC components (0-63),
    /// converted to 8-bit by shifting left 2. Index 0 is transparent.
    /// Team color lives at palette indices 208-211 (a 4-shade ramp, red for
    /// the base palette).
    /// </summary>
    public static class War2Palette
    {
        public const int TeamColorFirstIndex = 208;
        public const int TeamColorRampSize = 4;

        /// <summary>Palette archive entry per era (Forest/Winter/Wasteland/Swamp).</summary>
        public static int EntryForEra(PudEra era) => era switch
        {
            PudEra.Forest => 2,
            PudEra.Winter => 18,
            PudEra.Wasteland => 10,
            PudEra.Swamp => 438,
            _ => 2,
        };

        public static Rgba[] Decode(byte[] entry)
        {
            if (entry == null || entry.Length != 768)
                throw new War2FormatException($"Palette entry must be 768 bytes, got {entry?.Length ?? 0}");

            var palette = new Rgba[256];
            for (int i = 0; i < 256; i++)
            {
                palette[i] = new Rgba
                {
                    R = (byte)(entry[i * 3 + 0] << 2),
                    G = (byte)(entry[i * 3 + 1] << 2),
                    B = (byte)(entry[i * 3 + 2] << 2),
                    A = 0xFF,
                };
            }
            palette[0].A = 0x00; // index 0 always transparent
            return palette;
        }
    }
}
