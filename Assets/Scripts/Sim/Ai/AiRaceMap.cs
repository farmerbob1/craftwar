namespace Craftwar.Sim.Ai
{
    /// <summary>Race-neutral unit roles the AI script is written in. Resolved
    /// to concrete type ids per race at command time via <see cref="AiRaceMap"/>.</summary>
    public enum AiUnit : byte
    {
        Worker,
        Soldier,
        Archer,
        Cavalry,
        Siege,
        Hall,
        Keep,
        Castle,
        Farm,
        Barracks,
        LumberMill,
        Blacksmith,
        ScoutTower,
        GuardTower,
        CannonTower,
        CavalryHall,   // Stables / Ogre Mound
        Church,        // Church / Altar of Storms
        MageHall,      // Mage Tower / Temple of the Damned
        AirHall,       // Gryphon Aviary / Dragon Roost
    }

    /// <summary>Race-neutral research goals.</summary>
    public enum AiUpgrade : byte
    {
        Weapon1,        // Sword / Battle Axe 1
        Weapon2,
        Armor1,         // Shield 1
        Armor2,
        Missile1,       // Arrow / Throwing Axe 1
        Missile2,
        RangedUnlock,   // Rangers / Berserkers
        CavalryUnlock,  // Paladins / Ogre-Mages
    }

    /// <summary>
    /// Human/orc columns for the role enums. The sim has no cross-race pairing
    /// table anywhere (TechTree keeps parallel per-race arrays), so this is
    /// the one place the AI's race-neutral script meets concrete type ids.
    /// </summary>
    public static class AiRaceMap
    {
        public static UnitTypeId Unit(AiUnit u, Race race) =>
            race == Race.Orc ? Orc(u) : Human(u);

        static UnitTypeId Human(AiUnit u) => u switch
        {
            AiUnit.Worker => UnitTypeId.Peasant,
            AiUnit.Soldier => UnitTypeId.Footman,
            AiUnit.Archer => UnitTypeId.Archer,
            AiUnit.Cavalry => UnitTypeId.Knight,
            AiUnit.Siege => UnitTypeId.Ballista,
            AiUnit.Hall => UnitTypeId.TownHall,
            AiUnit.Keep => UnitTypeId.Keep,
            AiUnit.Castle => UnitTypeId.Castle,
            AiUnit.Farm => UnitTypeId.Farm,
            AiUnit.Barracks => UnitTypeId.HumanBarracks,
            AiUnit.LumberMill => UnitTypeId.ElvenLumberMill,
            AiUnit.Blacksmith => UnitTypeId.HumanBlacksmith,
            AiUnit.ScoutTower => UnitTypeId.HumanScoutTower,
            AiUnit.GuardTower => UnitTypeId.HumanGuardTower,
            AiUnit.CannonTower => UnitTypeId.HumanCannonTower,
            AiUnit.CavalryHall => UnitTypeId.Stables,
            AiUnit.Church => UnitTypeId.Church,
            AiUnit.MageHall => UnitTypeId.MageTower,
            AiUnit.AirHall => UnitTypeId.GryphonAviary,
            _ => UnitTypeId.None,
        };

        static UnitTypeId Orc(AiUnit u) => u switch
        {
            AiUnit.Worker => UnitTypeId.Peon,
            AiUnit.Soldier => UnitTypeId.Grunt,
            AiUnit.Archer => UnitTypeId.Axethrower,
            AiUnit.Cavalry => UnitTypeId.Ogre,
            AiUnit.Siege => UnitTypeId.Catapult,
            AiUnit.Hall => UnitTypeId.GreatHall,
            AiUnit.Keep => UnitTypeId.Stronghold,
            AiUnit.Castle => UnitTypeId.Fortress,
            AiUnit.Farm => UnitTypeId.PigFarm,
            AiUnit.Barracks => UnitTypeId.OrcBarracks,
            AiUnit.LumberMill => UnitTypeId.TrollLumberMill,
            AiUnit.Blacksmith => UnitTypeId.OrcBlacksmith,
            AiUnit.ScoutTower => UnitTypeId.OrcScoutTower,
            AiUnit.GuardTower => UnitTypeId.OrcGuardTower,
            AiUnit.CannonTower => UnitTypeId.OrcCannonTower,
            AiUnit.CavalryHall => UnitTypeId.OgreMound,
            AiUnit.Church => UnitTypeId.AltarOfStorms,
            AiUnit.MageHall => UnitTypeId.TempleOfTheDamned,
            AiUnit.AirHall => UnitTypeId.DragonRoost,
            _ => UnitTypeId.None,
        };

        public static UpgradeId Upgrade(AiUpgrade u, Race race) => race == Race.Orc
            ? u switch
            {
                AiUpgrade.Weapon1 => UpgradeId.Axe1,
                AiUpgrade.Weapon2 => UpgradeId.Axe2,
                AiUpgrade.Armor1 => UpgradeId.OrcShield1,
                AiUpgrade.Armor2 => UpgradeId.OrcShield2,
                AiUpgrade.Missile1 => UpgradeId.Spear1,
                AiUpgrade.Missile2 => UpgradeId.Spear2,
                AiUpgrade.RangedUnlock => UpgradeId.TrainBerserkers,
                AiUpgrade.CavalryUnlock => UpgradeId.TrainOgreMages,
                _ => UpgradeId.None,
            }
            : u switch
            {
                AiUpgrade.Weapon1 => UpgradeId.Sword1,
                AiUpgrade.Weapon2 => UpgradeId.Sword2,
                AiUpgrade.Armor1 => UpgradeId.HumanShield1,
                AiUpgrade.Armor2 => UpgradeId.HumanShield2,
                AiUpgrade.Missile1 => UpgradeId.Arrow1,
                AiUpgrade.Missile2 => UpgradeId.Arrow2,
                AiUpgrade.RangedUnlock => UpgradeId.TrainRangers,
                AiUpgrade.CavalryUnlock => UpgradeId.TrainPaladins,
                _ => UpgradeId.None,
            };
    }
}
