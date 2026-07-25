using Craftwar.Sim;

namespace Craftwar.View
{
    /// <summary>
    /// Icon index per unit type / per command, into the 196-frame HUD atlas.
    ///
    /// **Transcribed from the original's own icon table**, not guessed. The PSX
    /// source ships `GRAPHICS/UNIT/NEWPORTRAIT/PORTRAIT.HX`, the generated header
    /// naming every frame of the button-portrait bank:
    ///
    ///     #define BTN_H_PEON_1 0      #define BTN_O_PEON_1 1
    ///     #define BTN_H_GRUNT_1 2     #define BTN_O_GRUNT_1 3   ... up to 203
    ///
    /// Indices 0-195 are exactly the Remastered install's
    /// `Art/classic/HUD/Portrait-face` atlas (196 frames x 4 eras) and
    /// `Art/unit/Portrait/portrait.grp` (196 frames of 46x38); 196-203 are the
    /// PSX-only auto-build buttons, which have no art here. The per-command
    /// entries below come from the button tables in the same source (`statbtn.c`
    /// / `OLDSB.C`), so Move/Stop/Attack/Patrol/Repair/Harvest use the icons the
    /// original used, race included.
    ///
    /// Lives in View, not Sim: an icon index is presentation. UpgradeData.Icon
    /// sitting in Sim is a pre-existing wart justified only by it being what
    /// UGRD parsing yields — and it indexes this same bank.
    /// </summary>
    public static class UnitIconTable
    {
        public const int None = -1;

        /// <summary>Atlas index, or <see cref="None"/> to fall back to initials.</summary>
        public static int IconFor(UnitTypeId type) => type switch
        {
            // --- troops (BTN_H_*/BTN_O_* pairs, human first) ---
            UnitTypeId.Peasant => 0,
            UnitTypeId.Peon => 1,
            UnitTypeId.Footman => 2,
            UnitTypeId.Grunt => 3,
            UnitTypeId.Archer => 4,
            UnitTypeId.Axethrower => 5,
            UnitTypeId.Ranger => 6,
            UnitTypeId.Berserker => 7,
            UnitTypeId.Knight => 8,
            UnitTypeId.Ogre => 9,
            UnitTypeId.Paladin => 10,
            UnitTypeId.OgreMage => 11,
            UnitTypeId.Dwarves => 12,
            UnitTypeId.GoblinSapper => 13,
            UnitTypeId.Mage => 14,
            UnitTypeId.DeathKnight => 15,
            UnitTypeId.Ballista => 16,
            UnitTypeId.Catapult => 17,
            // Attack peasants/peons are the same art as the workers.
            UnitTypeId.AttackPeasant => 0,
            UnitTypeId.AttackPeon => 1,

            // --- ships and flyers ---
            UnitTypeId.HumanTanker => 18,
            UnitTypeId.OrcTanker => 19,
            UnitTypeId.HumanTransport => 20,
            UnitTypeId.OrcTransport => 21,
            UnitTypeId.ElvenDestroyer => 22,
            UnitTypeId.TrollDestroyer => 23,
            UnitTypeId.Battleship => 24,
            UnitTypeId.Juggernaught => 25,
            UnitTypeId.GnomishSubmarine => 26,
            UnitTypeId.GiantTurtle => 27,
            UnitTypeId.GnomishFlyingMachine => 28,
            UnitTypeId.GoblinZeppelin => 29,
            UnitTypeId.GryphonRider => 30,
            UnitTypeId.Dragon => 31,

            // --- named characters (BTN_NPC_* and BTN_HERO_*) ---
            UnitTypeId.Lothar => 32,
            UnitTypeId.Guldan => 33,
            UnitTypeId.UtherLightbringer => 34,
            UnitTypeId.Zuljin => 35,
            UnitTypeId.Chogall => 36,
            UnitTypeId.Daemon => 37,
            UnitTypeId.KargathBladefist => 186,
            UnitTypeId.Alleria => 187,
            UnitTypeId.Danath => 188,
            UnitTypeId.TeronGorefiend => 189,
            UnitTypeId.GromHellscream => 190,
            UnitTypeId.KurdranAndSkyree => 191,
            UnitTypeId.Deathwing => 192,
            UnitTypeId.Khadgar => 193,
            UnitTypeId.Dentarg => 194,
            UnitTypeId.Turalyon => 195,
            // No portrait of its own; the raise-dead icon is the skeleton.
            UnitTypeId.Skeleton => 114,
            UnitTypeId.EyeOfKilrogg => 111,

            // --- buildings ---
            UnitTypeId.Farm => 38,
            UnitTypeId.PigFarm => 39,
            UnitTypeId.TownHall => 40,
            UnitTypeId.GreatHall => 41,
            UnitTypeId.HumanBarracks => 42,
            UnitTypeId.OrcBarracks => 43,
            UnitTypeId.ElvenLumberMill => 44,
            UnitTypeId.TrollLumberMill => 45,
            UnitTypeId.HumanBlacksmith => 46,
            UnitTypeId.OrcBlacksmith => 47,
            UnitTypeId.HumanShipyard => 48,
            UnitTypeId.OrcShipyard => 49,
            UnitTypeId.HumanRefinery => 50,
            UnitTypeId.OrcRefinery => 51,
            UnitTypeId.HumanFoundry => 52,
            UnitTypeId.OrcFoundry => 53,
            UnitTypeId.HumanOilWell => 54,
            UnitTypeId.OrcOilWell => 55,
            UnitTypeId.Stables => 56,
            UnitTypeId.OgreMound => 57,
            UnitTypeId.GnomishInventor => 58,
            UnitTypeId.GoblinAlchemist => 59,
            UnitTypeId.HumanScoutTower => 60,
            UnitTypeId.OrcScoutTower => 61,
            UnitTypeId.Church => 62,
            UnitTypeId.AltarOfStorms => 63,
            UnitTypeId.MageTower => 64,
            UnitTypeId.TempleOfTheDamned => 65,
            UnitTypeId.Keep => 66,
            UnitTypeId.Stronghold => 67,
            UnitTypeId.Castle => 68,
            UnitTypeId.Fortress => 69,
            UnitTypeId.GryphonAviary => 72,
            UnitTypeId.DragonRoost => 73,
            UnitTypeId.GoldMine => 74,
            UnitTypeId.HumanGuardTower => 75,
            UnitTypeId.HumanCannonTower => 76,
            UnitTypeId.OrcGuardTower => 77,
            UnitTypeId.OrcCannonTower => 78,
            UnitTypeId.OilPatch => 79,
            UnitTypeId.DarkPortal => 80,
            UnitTypeId.CircleOfPower => 81,
            UnitTypeId.Runestone => 82,
            UnitTypeId.HumanWall => 92,
            UnitTypeId.OrcWall => 93,

            _ => None,
        };

        public static bool Has(UnitTypeId type) => IconFor(type) != None;

        // --- command icons (statbtn.c button tables) ---------------------------

        const int HumanMove = 83, OrcMove = 84;
        const int Repair = 85, Harvest = 86;
        const int BuildBasic = 87, BuildAdvanced = 88;
        const int Cancel = 91;
        const int HumanShield = 164, OrcShield = 167;   // "Stop" is the shield icon
        const int HumanSword = 116, OrcAxe = 119;       // "Attack" is the weapon icon
        const int HumanPatrol = 178, OrcPatrol = 179;
        const int HumanDebark = 162, OrcDebark = 163;   // transport unload

        /// <summary>
        /// Icon for a non-unit command button. Race-dependent where the original
        /// drew a human and an orc version of the same verb.
        /// </summary>
        public static int IconFor(CommandSlotKind kind, Race race)
        {
            bool orc = race == Race.Orc;
            return kind switch
            {
                CommandSlotKind.Move => orc ? OrcMove : HumanMove,
                CommandSlotKind.Stop => orc ? OrcShield : HumanShield,
                CommandSlotKind.Attack => orc ? OrcAxe : HumanSword,
                CommandSlotKind.Patrol => orc ? OrcPatrol : HumanPatrol,
                CommandSlotKind.Harvest => Harvest,
                CommandSlotKind.Repair => Repair,
                CommandSlotKind.Unload => orc ? OrcDebark : HumanDebark,
                CommandSlotKind.BuildBasicMenu => BuildBasic,
                CommandSlotKind.BuildAdvancedMenu => BuildAdvanced,
                CommandSlotKind.BackToActions => Cancel,
                CommandSlotKind.Cancel => Cancel,
                _ => None,
            };
        }
    }
}
