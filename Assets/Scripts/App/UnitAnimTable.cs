using System.Collections.Generic;
using Craftwar.View;

namespace Craftwar.App
{
    /// <summary>
    /// Per-sprite-bank animation layout, transcribed from the original's own
    /// sequence headers.
    ///
    /// The PSX source tree ships one `.SEQ` per sprite bank next to the art
    /// (`GRAPHICS/UNIT/{HUMAN,ORC,OTHER,MONSTER}/*.SEQ`) — generated headers that
    /// name the first frame of every animation step:
    ///
    ///     ;** WALK Sequence          ;** ATTACK Sequence
    ///     WALK_1 EQU 0               ATTACK_1 EQU 25
    ///     WALK_2 EQU 5               ATTACK_2 EQU 30
    ///
    /// Frame index / 5 is the block, and every entry below cross-checks against
    /// the installed `.grp`: the highest block named by the header is exactly one
    /// less than the bank's frame count / 5, for all 39 banks. That is what makes
    /// this a transcription rather than a reading of the art — the two sources
    /// agree independently.
    ///
    /// Two things the old heuristic (walk 0-4, attack 5+, death last 3) got
    /// wrong and this fixes:
    ///  * the catapult and ballista have only FOUR blocks — two of rolling and
    ///    two of firing — so cycling "walk" over blocks 0-4 played the firing
    ///    frames every time the machine moved;
    ///  * the demolition squad, goblin sappers and skeleton INTERLEAVE their
    ///    animations (dwarves walk on 0, 2, 5, 8, 11; die on 1, 4, 7, 10, 12).
    ///
    /// Era-prefixed banks (`s_`, `l_`, `x_`) reuse the base bank's layout, which
    /// the lookup handles by stripping the prefix — `Human/x_sub.grp` has no
    /// header of its own but is frame-for-frame `Human/sub.grp`.
    /// </summary>
    public static class UnitAnimTable
    {
        static readonly Dictionary<string, AnimLayout> Layouts =
            new Dictionary<string, AnimLayout>
        {
            ["human/battlshp.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["human/catapult.grp"] = new AnimLayout(new byte[] { 0, 1 }, new byte[] { 2, 3 }, null),
            ["human/destroy.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["human/dwarves.grp"] = new AnimLayout(new byte[] { 0, 2, 5, 8, 11 }, new byte[] { 3, 6, 9 }, new byte[] { 1, 4, 7, 10, 12 }),
            ["human/griffon.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3 }, new byte[] { 4, 5, 6 }, new byte[] { 7, 8, 9, 10, 11, 12 }),
            ["human/grunt.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }, new byte[] { 9, 10, 11 }),
            ["human/knight.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }, new byte[] { 9, 10, 11, 12, 13 }),
            ["human/l_sub.grp"] = new AnimLayout(new byte[] { 0, 1, 2 }, null, null),
            ["human/orn.grp"] = new AnimLayout(new byte[] { 0, 1 }, null, new byte[] { 2, 3 }),
            ["human/peon.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12 }),
            ["human/peong.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12 }),
            ["human/peonl.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12 }),
            ["human/spear.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6 }, new byte[] { 7, 8, 9 }),
            ["human/sub.grp"] = new AnimLayout(new byte[] { 0, 1, 2 }, null, null),
            ["human/tanker.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["human/tankero.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["human/transp.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["human/wizard.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }, new byte[] { 9, 10, 11, 12, 13, 14, 15 }),
            ["orc/battlshp.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["orc/catapult.grp"] = new AnimLayout(new byte[] { 0, 1 }, new byte[] { 2, 3 }, null),
            ["orc/destroy.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["orc/dknight.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }, new byte[] { 9, 10, 11, 12 }),
            ["orc/dragon.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3 }, new byte[] { 4 }, new byte[] { 5, 6, 7, 8, 9 }),
            ["orc/eyeofkil.grp"] = new AnimLayout(new byte[] { 0 }, null, null),
            ["orc/goblins.grp"] = new AnimLayout(new byte[] { 0, 2, 5, 8, 11, 13 }, new byte[] { 3, 6, 9 }, new byte[] { 1, 4, 7, 10, 12, 14 }),
            ["orc/grunt.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }, new byte[] { 9, 10, 11 }),
            ["orc/knight.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }, new byte[] { 9, 10, 11, 12, 13 }),
            ["orc/l_sub.grp"] = new AnimLayout(new byte[] { 0, 1, 2 }, null, null),
            ["orc/peon.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12 }),
            ["orc/peong.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12 }),
            ["orc/peonl.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12 }),
            ["orc/skeleton.grp"] = new AnimLayout(new byte[] { 0, 2, 5, 8, 11 }, new byte[] { 3, 6, 9, 12 }, new byte[] { 1, 4, 7, 10, 13 }),
            ["orc/spear.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }, new byte[] { 9, 10, 11 }),
            ["orc/sub.grp"] = new AnimLayout(new byte[] { 0, 1, 2 }, null, null),
            ["orc/tanker.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["orc/tankero.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["orc/transp.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1, 2 }),
            ["orc/zep.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1 }),
            ["monster/demon.grp"] = new AnimLayout(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12, 13, 14 }),

            // Critters. Their PSX headers name only WALK, and every installed
            // critter bank holds ten frames — but the second block is NOT the
            // other half of the gait: it is the gore splat the animal leaves
            // when it dies, non-directional (all five frames identical, checked
            // in all four banks). Walking them through it made a wandering sheep
            // flicker between sheep and giblets. Critters have no gait at all;
            // they slide, exactly as in the original.
            ["monster/sheep.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1 }),
            ["monster/boar.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1 }),
            ["monster/seal.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1 }),
            ["monster/hellhog.grp"] = new AnimLayout(new byte[] { 0 }, null, new byte[] { 1 }),
        };

        /// <summary>
        /// Layout for a bank path as <see cref="Craftwar.Import.War2.War2Sprites.FileForUnit"/>
        /// returns it (e.g. "Human/grunt.grp"). Default (invalid) when the bank
        /// has no animation — every building, and any art with no header.
        /// </summary>
        public static AnimLayout ForFile(string file)
        {
            if (string.IsNullOrEmpty(file))
                return default;
            string key = file.Replace('\\', '/').ToLowerInvariant();
            if (Layouts.TryGetValue(key, out var layout))
                return layout;

            // Era variants share the base bank's layout.
            int slash = key.LastIndexOf('/');
            string dir = slash >= 0 ? key.Substring(0, slash + 1) : string.Empty;
            string stem = key.Substring(slash + 1);
            if (stem.Length > 2 && stem[1] == '_'
                && (stem[0] == 's' || stem[0] == 'l' || stem[0] == 'x')
                && Layouts.TryGetValue(dir + stem.Substring(2), out layout))
                return layout;

            return default;
        }
    }
}
