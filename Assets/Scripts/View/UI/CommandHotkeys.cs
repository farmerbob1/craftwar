using Craftwar.Sim;

namespace Craftwar.View
{
    /// <summary>
    /// The original's per-command shortcut letters. In WC2 a card button's
    /// hotkey is a property of the *command*, not of the slot it happens to
    /// occupy — "A" is Attack on a footman, Archer in the barracks and Ship
    /// Armor in the foundry — so the letter is looked up per command here and
    /// the input router dispatches a pressed letter against the live card.
    ///
    /// Source: the shipped hotkey reference (Keybindings.txt, sections 5.3-5.6).
    /// Two entries are inferred rather than quoted, both flagged below.
    ///
    /// Within any one card face the letters are unique, which is what makes the
    /// scheme work: CommandCardModel asserts that in the editor after every
    /// rebuild.
    /// </summary>
    public static class CommandHotkeys
    {
        /// <summary>No letter — the slot is empty or Escape-driven.</summary>
        public const char None = '\0';

        public static char For(CommandSlotKind kind, ushort param) => kind switch
        {
            CommandSlotKind.Move => 'M',
            CommandSlotKind.Stop => 'S',
            CommandSlotKind.Attack => 'A',
            CommandSlotKind.Patrol => 'P',
            // Return Goods replaces Harvest in the same slot when the
            // worker/tanker is already carrying — same letter, like the
            // Archer/Ranger and Axethrower/Berserker substitutions below.
            CommandSlotKind.Harvest or CommandSlotKind.ReturnGoods => 'H',
            CommandSlotKind.Repair => 'R',
            CommandSlotKind.Unload => 'U',
            CommandSlotKind.BuildBasicMenu => 'B',
            CommandSlotKind.BuildAdvancedMenu => 'V',

            // Cancel and Back are Escape in the original, not letters.
            CommandSlotKind.Cancel => None,
            CommandSlotKind.BackToActions => None,

            CommandSlotKind.Build or CommandSlotKind.Train or CommandSlotKind.UpgradeTo
                => ForUnit((UnitTypeId)param),
            CommandSlotKind.Research or CommandSlotKind.Cast => ForUpgrade((UpgradeId)param),
            _ => None,
        };

        /// <summary>
        /// What the button prints. Escape-driven slots say so rather than going
        /// blank — the original labels them the same way.
        /// </summary>
        public static string LabelFor(CommandSlotKind kind, char hotkey)
        {
            if (hotkey != None)
                return hotkey.ToString();
            return kind is CommandSlotKind.Cancel or CommandSlotKind.BackToActions
                ? "Esc"
                : string.Empty;
        }

        /// <summary>
        /// Build-menu and production letters. One table serves both because a
        /// type is only ever one or the other: a barracks is built (B) and a
        /// ballista is produced (B), and the two never share a card face.
        /// </summary>
        public static char ForUnit(UnitTypeId type) => type switch
        {
            // --- Worker basic build page (identical letters for both races) ---
            UnitTypeId.Farm or UnitTypeId.PigFarm => 'F',
            UnitTypeId.HumanBarracks or UnitTypeId.OrcBarracks => 'B',
            UnitTypeId.TownHall or UnitTypeId.GreatHall => 'H',
            UnitTypeId.ElvenLumberMill or UnitTypeId.TrollLumberMill => 'L',
            UnitTypeId.HumanBlacksmith or UnitTypeId.OrcBlacksmith => 'S',
            UnitTypeId.HumanScoutTower or UnitTypeId.OrcScoutTower => 'T',

            // --- Worker advanced build page ---
            UnitTypeId.HumanShipyard or UnitTypeId.OrcShipyard => 'S',
            UnitTypeId.HumanFoundry or UnitTypeId.OrcFoundry => 'F',
            UnitTypeId.HumanRefinery or UnitTypeId.OrcRefinery => 'R',
            UnitTypeId.GnomishInventor => 'I',
            UnitTypeId.GoblinAlchemist => 'A',
            UnitTypeId.Church => 'C',
            UnitTypeId.AltarOfStorms => 'L',
            UnitTypeId.MageTower => 'M',
            UnitTypeId.TempleOfTheDamned => 'T',
            UnitTypeId.GryphonAviary => 'G',
            UnitTypeId.DragonRoost => 'D',
            UnitTypeId.OgreMound => 'O',
            // INFERRED: the reference omits the human Stables (it lists only the
            // orc Ogre Mound, O). A is the one letter free on the human advanced
            // page, so it takes A.
            UnitTypeId.Stables => 'A',

            // Raised by a tanker, not a worker: "Build Oil Platform - B".
            UnitTypeId.HumanOilWell or UnitTypeId.OrcOilWell => 'B',

            // --- Hall tier upgrades ---
            UnitTypeId.Keep => 'K',
            UnitTypeId.Castle => 'C',
            UnitTypeId.Stronghold => 'S',
            UnitTypeId.Fortress => 'F',

            // --- Tower tier upgrades ---
            UnitTypeId.HumanGuardTower or UnitTypeId.OrcGuardTower => 'G',
            UnitTypeId.HumanCannonTower or UnitTypeId.OrcCannonTower => 'C',

            // --- Hall production ---
            UnitTypeId.Peasant or UnitTypeId.Peon => 'P',

            // --- Barracks ---
            UnitTypeId.Footman => 'F',
            UnitTypeId.Grunt => 'G',
            // The upgraded forms keep the base unit's letter: the button is
            // substituted in place, so the card face is unchanged.
            UnitTypeId.Archer or UnitTypeId.Ranger => 'A',
            UnitTypeId.Axethrower or UnitTypeId.Berserker => 'A',
            UnitTypeId.Ballista => 'B',
            UnitTypeId.Catapult => 'C',
            UnitTypeId.Knight or UnitTypeId.Paladin => 'K',
            UnitTypeId.Ogre or UnitTypeId.OgreMage => 'O',

            // --- Spellcaster towers ---
            UnitTypeId.Mage => 'T',
            UnitTypeId.DeathKnight => 'T',

            // --- Inventor / alchemist ---
            UnitTypeId.Dwarves => 'D',
            UnitTypeId.GnomishFlyingMachine => 'F',
            UnitTypeId.GoblinSapper => 'S',
            UnitTypeId.GoblinZeppelin => 'Z',

            // --- Shipyards ---
            UnitTypeId.HumanTanker or UnitTypeId.OrcTanker => 'O',
            UnitTypeId.ElvenDestroyer or UnitTypeId.TrollDestroyer => 'D',
            UnitTypeId.HumanTransport or UnitTypeId.OrcTransport => 'T',
            UnitTypeId.Battleship => 'B',
            UnitTypeId.Juggernaught => 'J',
            UnitTypeId.GnomishSubmarine => 'S',
            UnitTypeId.GiantTurtle => 'G',

            // --- Aviary / roost ---
            UnitTypeId.GryphonRider => 'G',
            UnitTypeId.Dragon => 'D',

            _ => None,
        };

        /// <summary>
        /// Research letters. The two tiers of a graded upgrade share a letter —
        /// only one of them is ever offered at a time, so the card face stays
        /// unambiguous and the key means the same thing across the whole game.
        /// </summary>
        public static char ForUpgrade(UpgradeId upgrade) => upgrade switch
        {
            // Blacksmiths. The races differ: the human shield line is U,
            // the orc one is H ("Upgrade Shields - H").
            UpgradeId.Sword1 or UpgradeId.Sword2 => 'W',
            UpgradeId.Axe1 or UpgradeId.Axe2 => 'W',
            UpgradeId.HumanShield1 or UpgradeId.HumanShield2 => 'U',
            UpgradeId.OrcShield1 or UpgradeId.OrcShield2 => 'H',
            UpgradeId.Ballista1 or UpgradeId.Ballista2 => 'B',
            UpgradeId.Catapult1 or UpgradeId.Catapult2 => 'C',

            // Lumber mills.
            UpgradeId.Arrow1 or UpgradeId.Arrow2 => 'U',
            UpgradeId.Spear1 or UpgradeId.Spear2 => 'U',
            UpgradeId.TrainRangers => 'R',
            UpgradeId.Longbow => 'L',
            UpgradeId.RangerScouting => 'S',
            UpgradeId.RangerMarksmanship => 'M',
            UpgradeId.TrainBerserkers => 'B',
            UpgradeId.LighterAxes => 'A',
            UpgradeId.BerserkerScouting => 'S',
            UpgradeId.BerserkerRegeneration => 'R',

            // Foundries.
            UpgradeId.HumanShipCannon1 or UpgradeId.HumanShipCannon2 => 'C',
            UpgradeId.OrcShipCannon1 or UpgradeId.OrcShipCannon2 => 'C',
            UpgradeId.HumanShipArmor1 or UpgradeId.HumanShipArmor2 => 'A',
            UpgradeId.OrcShipArmor1 or UpgradeId.OrcShipArmor2 => 'A',

            // Church / altar.
            UpgradeId.TrainPaladins => 'P',
            UpgradeId.Healing => 'H',
            UpgradeId.Exorcism => 'E',
            UpgradeId.TrainOgreMages => 'M',
            UpgradeId.Bloodlust => 'B',
            UpgradeId.Runes => 'R',

            // Mage tower.
            UpgradeId.Slow => 'O',
            UpgradeId.FlameShield => 'L',
            UpgradeId.Invisibility => 'I',
            UpgradeId.Polymorph => 'P',
            UpgradeId.Blizzard => 'B',

            // Temple of the Damned.
            UpgradeId.Haste => 'H',
            UpgradeId.RaiseDead => 'R',
            UpgradeId.Whirlwind => 'W',
            UpgradeId.UnholyArmor => 'U',
            UpgradeId.DeathAndDecay => 'D',

            // Cast, never researched — listed so a data change that starts
            // offering them does not silently produce a letterless button.
            UpgradeId.HolyVision => 'V',
            UpgradeId.Fireball => 'F',
            UpgradeId.DeathCoil => 'C',
            UpgradeId.EyeOfKilrogg => 'E',

            _ => None,
        };
    }
}
