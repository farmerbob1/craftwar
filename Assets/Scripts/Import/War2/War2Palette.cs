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

        /// <summary>
        /// Loose-install folder and file stem per era, under Art/bgs.
        ///
        /// **The names disagree with the eras.** The folder called "Swamp" is the
        /// *Wasteland* era, and "Iceland" is Winter; the expansion's actual swamp
        /// is "XSwamp". This is not a guess — Phase 0d decoded all four folders
        /// and diffed them against the archive, producing a clean diagonal
        /// (Forest 387/387, Iceland→Winter 393/393, Swamp→Wasteland 393/393,
        /// XSwamp→Swamp). It also matches EntryForEra, where Swamp sits at 438,
        /// far above the others because the expansion added it last.
        ///
        /// Note this is the opposite convention to the sprite prefixes, where
        /// l_ = Wasteland and x_ = Swamp. Two naming schemes, one era axis.
        /// </summary>
        public static string FolderForEra(PudEra era) => era switch
        {
            PudEra.Forest => "Forest",
            PudEra.Winter => "Iceland",
            PudEra.Wasteland => "Swamp",
            PudEra.Swamp => "XSwamp",
            _ => "Forest",
        };

        /// <summary>File stem inside <see cref="FolderForEra"/>, e.g. "forest" → forest.ppl/.vr4/.vx4/.cv4.</summary>
        public static string StemForEra(PudEra era) => era switch
        {
            PudEra.Forest => "forest",
            PudEra.Winter => "iceland",
            PudEra.Wasteland => "swamp",
            PudEra.Swamp => "xswamp",
            _ => "forest",
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
