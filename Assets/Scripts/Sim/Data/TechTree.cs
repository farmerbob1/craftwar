namespace Craftwar.Sim
{
    /// <summary>
    /// The WC2 land tech tree as static data: what each worker can erect,
    /// what each building trains/researches/upgrades into, and the
    /// prerequisite buildings for each of those, plus the mapping onto the
    /// PUD ALOW restriction bits. Facts follow the original tech tree; the
    /// upgrade slot order is the UGRD table (Appendix B).
    /// </summary>
    public static class TechTree
    {
        static readonly UnitTypeId[] NoBuildings = System.Array.Empty<UnitTypeId>();
        static readonly UpgradeId[] NoUpgrades = System.Array.Empty<UpgradeId>();

        // ------------------------------------------------------------------
        // Worker build menus (basic entries have no prereqs; advanced ones
        // gate through Prereqs()).
        //
        // Ordered basic-first: the original's peasant card splits these across
        // "Build Basic Structure" and "Build Advanced Structure", and the first
        // BasicBuildingCount entries are the basic page for both races.
        // ------------------------------------------------------------------
        public static readonly UnitTypeId[] HumanBuildings =
        {
            UnitTypeId.Farm, UnitTypeId.HumanBarracks, UnitTypeId.TownHall,
            UnitTypeId.ElvenLumberMill, UnitTypeId.HumanBlacksmith, UnitTypeId.HumanScoutTower,
            UnitTypeId.HumanShipyard, UnitTypeId.HumanFoundry, UnitTypeId.HumanRefinery,
            UnitTypeId.Stables, UnitTypeId.Church, UnitTypeId.GnomishInventor,
            UnitTypeId.GryphonAviary, UnitTypeId.MageTower,
        };

        public static readonly UnitTypeId[] OrcBuildings =
        {
            UnitTypeId.PigFarm, UnitTypeId.OrcBarracks, UnitTypeId.GreatHall,
            UnitTypeId.TrollLumberMill, UnitTypeId.OrcBlacksmith, UnitTypeId.OrcScoutTower,
            UnitTypeId.OrcShipyard, UnitTypeId.OrcFoundry, UnitTypeId.OrcRefinery,
            UnitTypeId.OgreMound, UnitTypeId.AltarOfStorms, UnitTypeId.GoblinAlchemist,
            UnitTypeId.DragonRoost, UnitTypeId.TempleOfTheDamned,
        };

        /// <summary>
        /// Oil platforms are raised by *tankers*, not workers — they never
        /// appear on the peasant card (the original puts them on the tanker's).
        /// </summary>
        static readonly UnitTypeId[] HumanTankerBuildings = { UnitTypeId.HumanOilWell };
        static readonly UnitTypeId[] OrcTankerBuildings = { UnitTypeId.OrcOilWell };

        public static UnitTypeId[] TankerBuildings(Race race) =>
            race == Race.Orc ? OrcTankerBuildings : HumanTankerBuildings;

        /// <summary>
        /// Where the basic/advanced boundary falls in the arrays above.
        /// Basic: farm, barracks, hall, lumber mill, blacksmith, scout tower.
        /// Advanced: stables/mound, church/altar, mage tower/temple, inventor/alchemist.
        /// </summary>
        public const int BasicBuildingCount = 6;

        /// <summary>True if `type` sits on the worker's basic build page.</summary>
        public static bool IsBasicBuilding(Race race, UnitTypeId type)
        {
            var menu = WorkerBuildings(race);
            for (int i = 0; i < menu.Length && i < BasicBuildingCount; i++)
                if (menu[i] == type)
                    return true;
            return false;
        }

        public static UnitTypeId[] WorkerBuildings(Race race) =>
            race == Race.Orc ? OrcBuildings : HumanBuildings;

        // ------------------------------------------------------------------
        // Prerequisite buildings (all must be owned, alive and complete).
        // A more advanced hall satisfies a lesser one via Satisfies().
        // ------------------------------------------------------------------
        static readonly UnitTypeId[] NeedKeep = { UnitTypeId.Keep };
        static readonly UnitTypeId[] NeedCastle = { UnitTypeId.Castle };
        static readonly UnitTypeId[] NeedStronghold = { UnitTypeId.Stronghold };
        static readonly UnitTypeId[] NeedFortress = { UnitTypeId.Fortress };
        static readonly UnitTypeId[] NeedHBarracks = { UnitTypeId.HumanBarracks };
        static readonly UnitTypeId[] NeedOBarracks = { UnitTypeId.OrcBarracks };
        static readonly UnitTypeId[] NeedElvenMill = { UnitTypeId.ElvenLumberMill };
        static readonly UnitTypeId[] NeedTrollMill = { UnitTypeId.TrollLumberMill };
        static readonly UnitTypeId[] NeedStables = { UnitTypeId.Stables };
        static readonly UnitTypeId[] NeedMound = { UnitTypeId.OgreMound };
        static readonly UnitTypeId[] NeedHSmithAndMill =
            { UnitTypeId.HumanBlacksmith, UnitTypeId.ElvenLumberMill };
        static readonly UnitTypeId[] NeedOSmithAndMill =
            { UnitTypeId.OrcBlacksmith, UnitTypeId.TrollLumberMill };
        static readonly UnitTypeId[] NeedCastleParts =
            { UnitTypeId.Stables, UnitTypeId.HumanBlacksmith, UnitTypeId.ElvenLumberMill };
        static readonly UnitTypeId[] NeedFortressParts =
            { UnitTypeId.OgreMound, UnitTypeId.OrcBlacksmith, UnitTypeId.TrollLumberMill };
        static readonly UnitTypeId[] NeedHSmith = { UnitTypeId.HumanBlacksmith };
        static readonly UnitTypeId[] NeedOSmith = { UnitTypeId.OrcBlacksmith };
        // Naval + air
        static readonly UnitTypeId[] NeedHShipyard = { UnitTypeId.HumanShipyard };
        static readonly UnitTypeId[] NeedOShipyard = { UnitTypeId.OrcShipyard };
        static readonly UnitTypeId[] NeedHFoundry = { UnitTypeId.HumanFoundry };
        static readonly UnitTypeId[] NeedOFoundry = { UnitTypeId.OrcFoundry };
        static readonly UnitTypeId[] NeedHAviary = { UnitTypeId.GryphonAviary };
        static readonly UnitTypeId[] NeedODragonRoost = { UnitTypeId.DragonRoost };
        static readonly UnitTypeId[] NeedHInventor = { UnitTypeId.GnomishInventor };
        static readonly UnitTypeId[] NeedOAlchemist = { UnitTypeId.GoblinAlchemist };
        static readonly UnitTypeId[] NeedHMillAndInventor =
            { UnitTypeId.ElvenLumberMill, UnitTypeId.GnomishInventor };
        static readonly UnitTypeId[] NeedOMillAndAlchemist =
            { UnitTypeId.TrollLumberMill, UnitTypeId.GoblinAlchemist };

        /// <summary>Buildings that must exist before `type` can be built or trained.</summary>
        public static UnitTypeId[] Prereqs(UnitTypeId type) => type switch
        {
            // Advanced structures
            UnitTypeId.Stables => NeedKeep,
            UnitTypeId.OgreMound => NeedStronghold,
            UnitTypeId.Church => NeedCastle,
            UnitTypeId.MageTower => NeedCastle,
            // can_build_inventor: keep OR castle — Satisfies() lets a castle
            // stand in for the keep, so requiring the keep is the correct rule.
            UnitTypeId.GnomishInventor => NeedKeep,
            UnitTypeId.AltarOfStorms => NeedFortress,
            UnitTypeId.TempleOfTheDamned => NeedFortress,
            UnitTypeId.GoblinAlchemist => NeedStronghold,

            // Hall tiers (as self-upgrade targets)
            UnitTypeId.Keep => NeedHBarracks,
            UnitTypeId.Stronghold => NeedOBarracks,
            UnitTypeId.Castle => NeedCastleParts,
            UnitTypeId.Fortress => NeedFortressParts,

            // Tower tiers
            UnitTypeId.HumanGuardTower => NeedElvenMill,
            UnitTypeId.OrcGuardTower => NeedTrollMill,
            UnitTypeId.HumanCannonTower => NeedHSmith,
            UnitTypeId.OrcCannonTower => NeedOSmith,

            // Units
            UnitTypeId.Archer => NeedElvenMill,
            UnitTypeId.Axethrower => NeedTrollMill,
            UnitTypeId.Ranger => NeedElvenMill,
            UnitTypeId.Berserker => NeedTrollMill,
            UnitTypeId.Ballista => NeedHSmithAndMill,
            UnitTypeId.Catapult => NeedOSmithAndMill,
            UnitTypeId.Knight => NeedStables,
            UnitTypeId.Ogre => NeedMound,
            UnitTypeId.Paladin => NeedStables,
            UnitTypeId.OgreMage => NeedMound,

            // Naval + air structures. Verbatim from the original's per-building
            // gate table (PEON.C fnCanBuild -> OLDSB.C can_build_*): a shipyard
            // needs a lumber mill, foundry and refinery need a shipyard, and an
            // oil rig is can_build_always — no prerequisite at all, since owning
            // a tanker to raise it already implies a shipyard.
            UnitTypeId.HumanShipyard => NeedElvenMill,
            UnitTypeId.OrcShipyard => NeedTrollMill,
            UnitTypeId.HumanFoundry => NeedHShipyard,
            UnitTypeId.OrcFoundry => NeedOShipyard,
            UnitTypeId.HumanRefinery => NeedHShipyard,
            UnitTypeId.OrcRefinery => NeedOShipyard,
            UnitTypeId.HumanOilWell => NoBuildings,
            UnitTypeId.OrcOilWell => NoBuildings,
            UnitTypeId.GryphonAviary => NeedCastle,
            UnitTypeId.DragonRoost => NeedFortress,

            // Naval units, from the shipyard card gates (OLDSB.C bf_*_ok).
            // Tanker and destroyer have no gate beyond the shipyard that hosts
            // the button; transport and battleship both need a FOUNDRY; the
            // submarine/turtle needs the inventor/alchemist.
            UnitTypeId.HumanTransport => NeedHFoundry,
            UnitTypeId.OrcTransport => NeedOFoundry,
            UnitTypeId.Battleship => NeedHFoundry,
            UnitTypeId.Juggernaught => NeedOFoundry,
            UnitTypeId.GnomishSubmarine => NeedHInventor,
            UnitTypeId.GiantTurtle => NeedOAlchemist,

            // Air units. The scouting flyers need mill + inventor (bf_flyer_ok);
            // gryphons/dragons need their roost (bf_dragon_ok).
            UnitTypeId.GnomishFlyingMachine => NeedHMillAndInventor,
            UnitTypeId.GoblinZeppelin => NeedOMillAndAlchemist,
            UnitTypeId.GryphonRider => NeedHAviary,
            UnitTypeId.Dragon => NeedODragonRoost,

            _ => NoBuildings,
        };

        /// <summary>Does owning `owned` satisfy a requirement for `required`?
        /// Upgraded halls stand in for their earlier tiers.</summary>
        public static bool Satisfies(UnitTypeId owned, UnitTypeId required)
        {
            if (owned == required)
                return true;
            return required switch
            {
                UnitTypeId.TownHall => owned is UnitTypeId.Keep or UnitTypeId.Castle,
                UnitTypeId.Keep => owned == UnitTypeId.Castle,
                UnitTypeId.GreatHall => owned is UnitTypeId.Stronghold or UnitTypeId.Fortress,
                UnitTypeId.Stronghold => owned == UnitTypeId.Fortress,
                _ => false,
            };
        }

        // ------------------------------------------------------------------
        // Training
        // ------------------------------------------------------------------
        static readonly UnitTypeId[] TrainsPeasant = { UnitTypeId.Peasant };
        static readonly UnitTypeId[] TrainsPeon = { UnitTypeId.Peon };
        static readonly UnitTypeId[] TrainsHBarracks =
            { UnitTypeId.Footman, UnitTypeId.Archer, UnitTypeId.Ballista, UnitTypeId.Knight };
        static readonly UnitTypeId[] TrainsOBarracks =
            { UnitTypeId.Grunt, UnitTypeId.Axethrower, UnitTypeId.Catapult, UnitTypeId.Ogre };
        static readonly UnitTypeId[] TrainsMage = { UnitTypeId.Mage };
        static readonly UnitTypeId[] TrainsDeathKnight = { UnitTypeId.DeathKnight };
        // The inventor/alchemist also turn out the scouting flyers.
        static readonly UnitTypeId[] TrainsDwarves =
            { UnitTypeId.Dwarves, UnitTypeId.GnomishFlyingMachine };
        static readonly UnitTypeId[] TrainsSappers =
            { UnitTypeId.GoblinSapper, UnitTypeId.GoblinZeppelin };
        static readonly UnitTypeId[] TrainsHShipyard =
        {
            UnitTypeId.HumanTanker, UnitTypeId.HumanTransport, UnitTypeId.ElvenDestroyer,
            UnitTypeId.GnomishSubmarine, UnitTypeId.Battleship,
        };
        static readonly UnitTypeId[] TrainsOShipyard =
        {
            UnitTypeId.OrcTanker, UnitTypeId.OrcTransport, UnitTypeId.TrollDestroyer,
            UnitTypeId.GiantTurtle, UnitTypeId.Juggernaught,
        };
        static readonly UnitTypeId[] TrainsAviary = { UnitTypeId.GryphonRider };
        static readonly UnitTypeId[] TrainsRoost = { UnitTypeId.Dragon };

        /// <summary>Base production list per building (before research
        /// substitutions like archer→ranger).</summary>
        public static UnitTypeId[] Trains(UnitTypeId building) => building switch
        {
            UnitTypeId.TownHall or UnitTypeId.Keep or UnitTypeId.Castle => TrainsPeasant,
            UnitTypeId.GreatHall or UnitTypeId.Stronghold or UnitTypeId.Fortress => TrainsPeon,
            UnitTypeId.HumanBarracks => TrainsHBarracks,
            UnitTypeId.OrcBarracks => TrainsOBarracks,
            UnitTypeId.MageTower => TrainsMage,
            UnitTypeId.TempleOfTheDamned => TrainsDeathKnight,
            UnitTypeId.GnomishInventor => TrainsDwarves,
            UnitTypeId.GoblinAlchemist => TrainsSappers,
            UnitTypeId.HumanShipyard => TrainsHShipyard,
            UnitTypeId.OrcShipyard => TrainsOShipyard,
            UnitTypeId.GryphonAviary => TrainsAviary,
            UnitTypeId.DragonRoost => TrainsRoost,
            _ => NoBuildings,
        };

        /// <summary>Research-driven production substitution: once the unlock
        /// is researched, the base unit is trained as its upgraded form.</summary>
        public static UnitTypeId TrainSubstitute(UnitTypeId unit, ulong researchedMask)
        {
            bool Has(UpgradeId u) => (researchedMask & (1ul << (int)u)) != 0;
            return unit switch
            {
                UnitTypeId.Archer when Has(UpgradeId.TrainRangers) => UnitTypeId.Ranger,
                UnitTypeId.Axethrower when Has(UpgradeId.TrainBerserkers) => UnitTypeId.Berserker,
                UnitTypeId.Knight when Has(UpgradeId.TrainPaladins) => UnitTypeId.Paladin,
                UnitTypeId.Ogre when Has(UpgradeId.TrainOgreMages) => UnitTypeId.OgreMage,
                _ => unit,
            };
        }

        /// <summary>Instant unit conversion applied when an unlock completes
        /// (existing archers become rangers, etc.). None = no transform.</summary>
        public static void TransformFor(UpgradeId upgrade, out UnitTypeId from, out UnitTypeId to)
        {
            (from, to) = upgrade switch
            {
                UpgradeId.TrainRangers => (UnitTypeId.Archer, UnitTypeId.Ranger),
                UpgradeId.TrainBerserkers => (UnitTypeId.Axethrower, UnitTypeId.Berserker),
                UpgradeId.TrainPaladins => (UnitTypeId.Knight, UnitTypeId.Paladin),
                UpgradeId.TrainOgreMages => (UnitTypeId.Ogre, UnitTypeId.OgreMage),
                _ => (UnitTypeId.None, UnitTypeId.None),
            };
        }

        // ------------------------------------------------------------------
        // Building self-upgrades (ordered: HUD shows them in this order)
        // ------------------------------------------------------------------
        static readonly UnitTypeId[] ToKeep = { UnitTypeId.Keep };
        static readonly UnitTypeId[] ToCastle = { UnitTypeId.Castle };
        static readonly UnitTypeId[] ToStronghold = { UnitTypeId.Stronghold };
        static readonly UnitTypeId[] ToFortress = { UnitTypeId.Fortress };
        static readonly UnitTypeId[] ToHumanTowers =
            { UnitTypeId.HumanGuardTower, UnitTypeId.HumanCannonTower };
        static readonly UnitTypeId[] ToOrcTowers =
            { UnitTypeId.OrcGuardTower, UnitTypeId.OrcCannonTower };

        public static UnitTypeId[] UpgradesTo(UnitTypeId building) => building switch
        {
            UnitTypeId.TownHall => ToKeep,
            UnitTypeId.Keep => ToCastle,
            UnitTypeId.GreatHall => ToStronghold,
            UnitTypeId.Stronghold => ToFortress,
            UnitTypeId.HumanScoutTower => ToHumanTowers,
            UnitTypeId.OrcScoutTower => ToOrcTowers,
            _ => NoBuildings,
        };

        // ------------------------------------------------------------------
        // Research
        // ------------------------------------------------------------------
        static readonly UpgradeId[] HSmithResearch =
            { UpgradeId.Sword1, UpgradeId.Sword2, UpgradeId.HumanShield1, UpgradeId.HumanShield2,
              UpgradeId.Ballista1, UpgradeId.Ballista2 };
        static readonly UpgradeId[] OSmithResearch =
            { UpgradeId.Axe1, UpgradeId.Axe2, UpgradeId.OrcShield1, UpgradeId.OrcShield2,
              UpgradeId.Catapult1, UpgradeId.Catapult2 };
        static readonly UpgradeId[] ElvenMillResearch =
            { UpgradeId.Arrow1, UpgradeId.Arrow2, UpgradeId.TrainRangers, UpgradeId.Longbow,
              UpgradeId.RangerScouting, UpgradeId.RangerMarksmanship };
        static readonly UpgradeId[] TrollMillResearch =
            { UpgradeId.Spear1, UpgradeId.Spear2, UpgradeId.TrainBerserkers, UpgradeId.LighterAxes,
              UpgradeId.BerserkerScouting, UpgradeId.BerserkerRegeneration };
        static readonly UpgradeId[] ChurchResearch =
            { UpgradeId.TrainPaladins, UpgradeId.Healing, UpgradeId.Exorcism };
        static readonly UpgradeId[] AltarResearch =
            { UpgradeId.TrainOgreMages, UpgradeId.Bloodlust, UpgradeId.Runes };
        static readonly UpgradeId[] MageTowerResearch =
            { UpgradeId.Slow, UpgradeId.FlameShield, UpgradeId.Invisibility,
              UpgradeId.Polymorph, UpgradeId.Blizzard };
        static readonly UpgradeId[] TempleResearch =
            { UpgradeId.Haste, UpgradeId.RaiseDead, UpgradeId.Whirlwind,
              UpgradeId.UnholyArmor, UpgradeId.DeathAndDecay };

        // The foundry is the naval smithy — these magnitudes already apply in
        // GameSim.Tech (the MoveDomain == 2 branches); it had no provider until now.
        static readonly UpgradeId[] HFoundryResearch =
            { UpgradeId.HumanShipCannon1, UpgradeId.HumanShipCannon2,
              UpgradeId.HumanShipArmor1, UpgradeId.HumanShipArmor2 };
        static readonly UpgradeId[] OFoundryResearch =
            { UpgradeId.OrcShipCannon1, UpgradeId.OrcShipCannon2,
              UpgradeId.OrcShipArmor1, UpgradeId.OrcShipArmor2 };

        public static UpgradeId[] Research(UnitTypeId building) => building switch
        {
            UnitTypeId.HumanFoundry => HFoundryResearch,
            UnitTypeId.OrcFoundry => OFoundryResearch,
            UnitTypeId.HumanBlacksmith => HSmithResearch,
            UnitTypeId.OrcBlacksmith => OSmithResearch,
            UnitTypeId.ElvenLumberMill => ElvenMillResearch,
            UnitTypeId.TrollLumberMill => TrollMillResearch,
            UnitTypeId.Church => ChurchResearch,
            UnitTypeId.AltarOfStorms => AltarResearch,
            UnitTypeId.MageTower => MageTowerResearch,
            UnitTypeId.TempleOfTheDamned => TempleResearch,
            _ => NoUpgrades,
        };

        /// <summary>Upgrade that must already be researched first
        /// (level 2 needs level 1; elite upgrades need the unlock).</summary>
        public static UpgradeId ResearchPrior(UpgradeId u) => u switch
        {
            UpgradeId.Sword2 => UpgradeId.Sword1,
            UpgradeId.Axe2 => UpgradeId.Axe1,
            UpgradeId.Arrow2 => UpgradeId.Arrow1,
            UpgradeId.Spear2 => UpgradeId.Spear1,
            UpgradeId.HumanShield2 => UpgradeId.HumanShield1,
            UpgradeId.OrcShield2 => UpgradeId.OrcShield1,
            UpgradeId.HumanShipCannon2 => UpgradeId.HumanShipCannon1,
            UpgradeId.OrcShipCannon2 => UpgradeId.OrcShipCannon1,
            UpgradeId.HumanShipArmor2 => UpgradeId.HumanShipArmor1,
            UpgradeId.OrcShipArmor2 => UpgradeId.OrcShipArmor1,
            UpgradeId.Catapult2 => UpgradeId.Catapult1,
            UpgradeId.Ballista2 => UpgradeId.Ballista1,
            UpgradeId.Longbow => UpgradeId.TrainRangers,
            UpgradeId.RangerScouting => UpgradeId.TrainRangers,
            UpgradeId.RangerMarksmanship => UpgradeId.TrainRangers,
            UpgradeId.LighterAxes => UpgradeId.TrainBerserkers,
            UpgradeId.BerserkerScouting => UpgradeId.TrainBerserkers,
            UpgradeId.BerserkerRegeneration => UpgradeId.TrainBerserkers,
            UpgradeId.Healing => UpgradeId.TrainPaladins,
            UpgradeId.Exorcism => UpgradeId.TrainPaladins,
            UpgradeId.Bloodlust => UpgradeId.TrainOgreMages,
            UpgradeId.Runes => UpgradeId.TrainOgreMages,
            _ => UpgradeId.None,
        };

        static readonly UnitTypeId[] RangerUnlockNeeds = { UnitTypeId.Keep };
        static readonly UnitTypeId[] RangerEliteNeeds = { UnitTypeId.Castle };
        static readonly UnitTypeId[] BerserkerUnlockNeeds = { UnitTypeId.Stronghold };
        static readonly UnitTypeId[] BerserkerEliteNeeds = { UnitTypeId.Fortress };

        /// <summary>Extra building prereqs for a research beyond its provider.</summary>
        public static UnitTypeId[] ResearchPrereqBuildings(UpgradeId u) => u switch
        {
            UpgradeId.TrainRangers => RangerUnlockNeeds,
            UpgradeId.Longbow or UpgradeId.RangerScouting or UpgradeId.RangerMarksmanship
                => RangerEliteNeeds,
            UpgradeId.TrainBerserkers => BerserkerUnlockNeeds,
            UpgradeId.LighterAxes or UpgradeId.BerserkerScouting or UpgradeId.BerserkerRegeneration
                => BerserkerEliteNeeds,
            _ => NoBuildings,
        };

        // ------------------------------------------------------------------
        // ALOW restriction-bit mappings (PUD spec section 7)
        // ------------------------------------------------------------------

        /// <summary>ALOW units/buildings bit for a unit type; -1 = never restricted.</summary>
        public static int AlowUnitBit(UnitTypeId t) => t switch
        {
            UnitTypeId.Footman or UnitTypeId.Grunt => 0,
            UnitTypeId.Peasant or UnitTypeId.Peon => 1,
            UnitTypeId.Ballista or UnitTypeId.Catapult => 2,
            UnitTypeId.Knight or UnitTypeId.Ogre
                or UnitTypeId.Paladin or UnitTypeId.OgreMage => 3,
            UnitTypeId.Archer or UnitTypeId.Axethrower
                or UnitTypeId.Ranger or UnitTypeId.Berserker => 4,
            UnitTypeId.Mage or UnitTypeId.DeathKnight => 5,
            UnitTypeId.HumanTanker or UnitTypeId.OrcTanker => 6,
            UnitTypeId.ElvenDestroyer or UnitTypeId.TrollDestroyer => 7,
            UnitTypeId.HumanTransport or UnitTypeId.OrcTransport => 8,
            UnitTypeId.Battleship or UnitTypeId.Juggernaught => 9,
            UnitTypeId.GnomishSubmarine or UnitTypeId.GiantTurtle => 10,
            UnitTypeId.GnomishFlyingMachine or UnitTypeId.GoblinZeppelin => 11,
            UnitTypeId.GryphonRider or UnitTypeId.Dragon => 12,
            UnitTypeId.Dwarves or UnitTypeId.GoblinSapper => 14,
            UnitTypeId.GryphonAviary or UnitTypeId.DragonRoost => 15,
            UnitTypeId.Farm or UnitTypeId.PigFarm => 16,
            UnitTypeId.HumanBarracks or UnitTypeId.OrcBarracks => 17,
            UnitTypeId.ElvenLumberMill or UnitTypeId.TrollLumberMill => 18,
            UnitTypeId.Stables or UnitTypeId.OgreMound => 19,
            UnitTypeId.MageTower or UnitTypeId.TempleOfTheDamned => 20,
            UnitTypeId.HumanFoundry or UnitTypeId.OrcFoundry => 21,
            UnitTypeId.HumanRefinery or UnitTypeId.OrcRefinery => 22,
            UnitTypeId.GnomishInventor or UnitTypeId.GoblinAlchemist => 23,
            UnitTypeId.Church or UnitTypeId.AltarOfStorms => 24,
            UnitTypeId.HumanScoutTower or UnitTypeId.OrcScoutTower
                or UnitTypeId.HumanGuardTower or UnitTypeId.OrcGuardTower
                or UnitTypeId.HumanCannonTower or UnitTypeId.OrcCannonTower => 25,
            UnitTypeId.TownHall or UnitTypeId.GreatHall => 26,
            UnitTypeId.Keep or UnitTypeId.Stronghold => 27,
            UnitTypeId.Castle or UnitTypeId.Fortress => 28,
            UnitTypeId.HumanBlacksmith or UnitTypeId.OrcBlacksmith => 29,
            UnitTypeId.HumanShipyard or UnitTypeId.OrcShipyard => 30,
            _ => -1,
        };

        /// <summary>ALOW upgrade bit for combat upgrades; -1 = never restricted.</summary>
        public static int AlowUpgradeBit(UpgradeId u) => u switch
        {
            UpgradeId.Arrow1 or UpgradeId.Spear1 => 0,
            UpgradeId.Arrow2 or UpgradeId.Spear2 => 1,
            UpgradeId.Sword1 or UpgradeId.Axe1 => 2,
            UpgradeId.Sword2 or UpgradeId.Axe2 => 3,
            UpgradeId.HumanShield1 or UpgradeId.OrcShield1 => 4,
            UpgradeId.HumanShield2 or UpgradeId.OrcShield2 => 5,
            UpgradeId.HumanShipCannon1 or UpgradeId.OrcShipCannon1 => 6,
            UpgradeId.HumanShipCannon2 or UpgradeId.OrcShipCannon2 => 7,
            UpgradeId.HumanShipArmor1 or UpgradeId.OrcShipArmor1 => 8,
            UpgradeId.HumanShipArmor2 or UpgradeId.OrcShipArmor2 => 9,
            UpgradeId.Ballista1 or UpgradeId.Catapult1 => 12,
            UpgradeId.Ballista2 or UpgradeId.Catapult2 => 13,
            UpgradeId.TrainRangers or UpgradeId.TrainBerserkers => 16,
            UpgradeId.Longbow or UpgradeId.LighterAxes => 17,
            UpgradeId.RangerScouting or UpgradeId.BerserkerScouting => 18,
            UpgradeId.RangerMarksmanship or UpgradeId.BerserkerRegeneration => 19,
            _ => -1,
        };

        /// <summary>ALOW spell bit for spell upgrades; -1 = never restricted.</summary>
        public static int AlowSpellBit(UpgradeId u) => u switch
        {
            UpgradeId.HolyVision => 0,
            UpgradeId.Healing => 1,
            UpgradeId.Exorcism => 3,
            UpgradeId.FlameShield => 4,
            UpgradeId.Fireball => 5,
            UpgradeId.Slow => 6,
            UpgradeId.Invisibility => 7,
            UpgradeId.Polymorph => 8,
            UpgradeId.Blizzard => 9,
            UpgradeId.EyeOfKilrogg => 10,
            UpgradeId.Bloodlust => 11,
            UpgradeId.RaiseDead => 13,
            UpgradeId.DeathCoil => 14,
            UpgradeId.Whirlwind => 15,
            UpgradeId.Haste => 16,
            UpgradeId.UnholyArmor => 17,
            UpgradeId.Runes => 18,
            UpgradeId.DeathAndDecay => 19,
            _ => -1,
        };
    }
}
