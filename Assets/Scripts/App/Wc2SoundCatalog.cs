using System;
using System.Collections.Generic;
using Craftwar.Import;
using Craftwar.Sim;
using Craftwar.View;

namespace Craftwar.App
{
    /// <summary>
    /// Resolves unit voice lines out of the installation's Gamesfx tree.
    ///
    /// Files are found by scanning rather than by constructing names, because the
    /// naming is not consistent enough to construct: the same verb appears as
    /// yessr/yessir, pissd/pissed/pisd/piss, ready/redy and wrkdon/wrkdone
    /// depending on which folder you are in. Scanning also means the variant
    /// count is discovered — how many "what" lines a unit has is data, and
    /// hardcoding 3 would silently drop the fourth.
    ///
    /// Lives in App rather than Import because it needs IAssetSource (Import)
    /// and UnitSoundKind (View), which are sibling assemblies that cannot see
    /// each other. UnitSpriteBank sits here for exactly the same reason.
    ///
    /// Deliberately partial. A unit with no folder falls back to its race's
    /// generic voice, and a kind with no files returns empty; AudioDirector
    /// already tolerates a null clip, so missing lines degrade to silence rather
    /// than blocking the phase.
    /// </summary>
    public static class Wc2SoundCatalog
    {
        public const string Root = "gamesfx/";

        /// <summary>
        /// Spelling variants per kind, matched as substrings of the file stem.
        /// Ordered longest-first where one token would otherwise swallow another.
        /// </summary>
        static readonly Dictionary<UnitSoundKind, string[]> Tokens = new()
        {
            [UnitSoundKind.Selected] = new[] { "what", "wht" },
            [UnitSoundKind.Acknowledge] = new[] { "yessir", "yessr", "yes" },
            [UnitSoundKind.Annoyed] = new[] { "pissed", "pissd", "pisd", "piss" },
            [UnitSoundKind.Ready] = new[] { "ready", "redy" },
            [UnitSoundKind.WorkComplete] = new[] { "wrkdone", "wrkdon" },
            [UnitSoundKind.Help] = new[] { "help" },
            [UnitSoundKind.Death] = new[] { "dead", "death" },
            [UnitSoundKind.Attack] = new[] { "atak" },
        };

        /// <summary>
        /// Units with their own voice folder. Everything else uses the race's
        /// generic lines, which is what the original does too — a footman and a
        /// ballista share the human voice.
        /// </summary>
        static readonly Dictionary<UnitTypeId, string> Folders = new()
        {
            [UnitTypeId.Peasant] = "peasant",
            [UnitTypeId.AttackPeasant] = "peasant",
            [UnitTypeId.Peon] = "peon",
            [UnitTypeId.AttackPeon] = "peon",
            [UnitTypeId.Knight] = "knight",
            [UnitTypeId.Paladin] = "paladin",
            [UnitTypeId.Ogre] = "ogre",
            [UnitTypeId.OgreMage] = "ogremage",
            [UnitTypeId.Dragon] = "dragon",
            [UnitTypeId.Deathwing] = "dragon",
            [UnitTypeId.GryphonRider] = "griffon",
            [UnitTypeId.Mage] = "wizard",
            [UnitTypeId.Khadgar] = "khadgar",
            [UnitTypeId.DeathKnight] = "deathknt",
            [UnitTypeId.TeronGorefiend] = "teron",
            [UnitTypeId.Archer] = "elves",
            [UnitTypeId.Ranger] = "elves",
            [UnitTypeId.Alleria] = "aleria",
            [UnitTypeId.Axethrower] = "troll",
            [UnitTypeId.Berserker] = "troll",
            [UnitTypeId.Zuljin] = "troll",
            [UnitTypeId.Dwarves] = "dwarf",
            [UnitTypeId.GoblinSapper] = "goblin",
            [UnitTypeId.GoblinZeppelin] = "zeppelin",
            [UnitTypeId.GnomishFlyingMachine] = "gnome",
            [UnitTypeId.Danath] = "danath",
            [UnitTypeId.KargathBladefist] = "kargath",
            [UnitTypeId.KurdranAndSkyree] = "kurdran",
            [UnitTypeId.Turalyon] = "turalyon",
            [UnitTypeId.GromHellscream] = "grom",
            [UnitTypeId.Dentarg] = "dentarg",
        };

        /// <summary>Ships share one voice per race rather than one per hull.</summary>
        static bool IsShip(UnitTypeId t) => t is UnitTypeId.HumanTanker or UnitTypeId.OrcTanker
            or UnitTypeId.HumanTransport or UnitTypeId.OrcTransport
            or UnitTypeId.ElvenDestroyer or UnitTypeId.TrollDestroyer
            or UnitTypeId.Battleship or UnitTypeId.Juggernaught
            or UnitTypeId.GnomishSubmarine or UnitTypeId.GiantTurtle;

        /// <summary>
        /// Logical paths for one unit and one kind, sorted so the variant order
        /// is stable across machines. Empty when the unit has no such line.
        /// </summary>
        public static List<string> Find(IAssetSource source, UnitTypeId type, Race race, UnitSoundKind kind)
        {
            var results = new List<string>();
            if (source == null || !Tokens.TryGetValue(kind, out var tokens))
                return results;

            foreach (string folder in FoldersFor(type, race))
            {
                Collect(source, folder, tokens, IsGenericRace(folder), results);
                if (results.Count > 0)
                    return results; // a unit's own lines win over the race's
            }
            return results;
        }

        /// <summary>The unit's own folder first, then its race's generic voice.</summary>
        static IEnumerable<string> FoldersFor(UnitTypeId type, Race race)
        {
            if (Folders.TryGetValue(type, out string own))
                yield return own;
            else if (IsShip(type))
                yield return "ships";

            yield return race == Race.Orc ? "orc" : "human";
        }

        static bool IsGenericRace(string folder) => folder == "human" || folder == "orc";

        static void Collect(IAssetSource source, string folder, string[] tokens,
                            bool generic, List<string> results)
        {
            foreach (string path in source.List(Root + folder + "/"))
            {
                if (!path.EndsWith(".wav", StringComparison.Ordinal))
                    continue;

                int slash = path.LastIndexOf('/');
                string stem = path.Substring(slash + 1, path.Length - slash - 5);

                // "Hdempis4" is the demolition squad's annoyed line parked in the
                // human folder; it is not the generic human voice and would
                // otherwise be served to every footman.
                if (generic && stem.Contains("dempis"))
                    continue;

                foreach (string token in tokens)
                {
                    if (stem.Contains(token, StringComparison.Ordinal))
                    {
                        results.Add(path);
                        break;
                    }
                }
            }
            results.Sort(StringComparer.Ordinal);
        }

        /// <summary>Interface UI sounds, which are not unit voices.</summary>
        public const string SfxButton = "sfx/button.wav";
        public const string SfxError = "sfx/error.wav";
        public const string SfxMenu = "sfx/menu.wav";

        /// <summary>Shared world sounds under Gamesfx/Misc and Gamesfx/Bldg.</summary>
        public const string MiscConstruct = "gamesfx/misc/constrct.wav";
        public const string MiscExplode = "gamesfx/misc/explode.wav";
        public const string MiscBuildingExplode = "gamesfx/misc/bldexpl1.wav";
        public const string MiscSword = "gamesfx/misc/sword1.wav";
        public const string MiscBowFire = "gamesfx/misc/bowfire.wav";
        public const string MiscBowHit = "gamesfx/misc/bowhit.wav";
        public const string MiscCatapult = "gamesfx/misc/catapult.wav";
        public const string MiscFireball = "gamesfx/misc/fireball.wav";
        public const string MiscTreeChop = "gamesfx/misc/tree1.wav";
        public const string MiscDock = "gamesfx/misc/dock.wav";
        public const string BldgMineCollapse = "gamesfx/bldg/mine.wav";
        public const string ShipsSink = "gamesfx/ships/shipsink.wav";

        /// <summary>Church/Altar/Mage-Tower/Temple cast sounds, under
        /// Gamesfx/Spells except Runes (Gamesfx/Misc — the original files it
        /// as a "misc" effect, not a Spells/ one, oddly enough).</summary>
        public const string SpellHeal = "gamesfx/spells/heal.wav";
        public const string SpellExorcism = "gamesfx/spells/exorcism.wav";
        public const string SpellBloodlust = "gamesfx/spells/blodlust.wav";
        public const string SpellRunes = "gamesfx/misc/runes.wav";
        public const string SpellSlow = "gamesfx/spells/slow.wav";
        public const string SpellHaste = "gamesfx/spells/haste.wav";
        public const string SpellInvisibility = "gamesfx/spells/invisibl.wav";
        public const string SpellPolymorph = "gamesfx/spells/morph.wav";
        public const string SpellFlameShield = "gamesfx/spells/flamshld.wav";
        public const string SpellUnholyArmor = "gamesfx/spells/unhlyarm.wav";
        public const string SpellRaiseDead = "gamesfx/spells/thunder.wav";
        public const string SpellBlizzard = "gamesfx/spells/icestorm.wav";
        public const string SpellWhirlwind = "gamesfx/spells/whrlwind.wav";
        public const string SpellDeathAndDecay = "gamesfx/spells/decay.wav";
    }
}
