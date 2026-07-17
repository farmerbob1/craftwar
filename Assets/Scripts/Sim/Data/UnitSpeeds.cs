namespace Craftwar.Sim
{
    /// <summary>
    /// Movement speed per unit type. Speed is NOT stored in UDTA — the
    /// original hardcodes it. Values follow the community-documented WC2
    /// table (also used by Wargus): Speed/10 = tiles per second at 100%.
    /// Buildings and immobile types are 0.
    /// </summary>
    public static class UnitSpeeds
    {
        public const int DefaultSpeed = 10;

        static readonly byte[] Table = BuildTable();

        public static int Get(ushort typeId) =>
            typeId < Table.Length ? Table[typeId] : 0;

        static byte[] BuildTable()
        {
            var t = new byte[UdtaParser.UnitCount];

            void Set(UnitTypeId id, byte speed) => t[(int)id] = speed;

            // Infantry & workers
            Set(UnitTypeId.Footman, 10);
            Set(UnitTypeId.Grunt, 10);
            Set(UnitTypeId.Peasant, 10);
            Set(UnitTypeId.Peon, 10);
            Set(UnitTypeId.AttackPeasant, 10);
            Set(UnitTypeId.AttackPeon, 10);
            Set(UnitTypeId.Archer, 10);
            Set(UnitTypeId.Axethrower, 10);
            Set(UnitTypeId.Ranger, 10);
            Set(UnitTypeId.Berserker, 10);
            Set(UnitTypeId.Knight, 13);
            Set(UnitTypeId.Ogre, 13);
            Set(UnitTypeId.Paladin, 13);
            Set(UnitTypeId.OgreMage, 13);
            Set(UnitTypeId.Mage, 8);
            Set(UnitTypeId.DeathKnight, 8);
            Set(UnitTypeId.Dwarves, 11);
            Set(UnitTypeId.GoblinSapper, 11);
            Set(UnitTypeId.Ballista, 5);
            Set(UnitTypeId.Catapult, 5);
            Set(UnitTypeId.Skeleton, 8);
            Set(UnitTypeId.Critter, 3);
            Set(UnitTypeId.CritterSheep, 3);
            Set(UnitTypeId.CritterPig, 3);
            Set(UnitTypeId.CritterSeal, 3);
            Set(UnitTypeId.CritterRedPig, 3);

            // Heroes inherit their class speeds
            Set(UnitTypeId.Alleria, 10);
            Set(UnitTypeId.TeronGorefiend, 8);
            Set(UnitTypeId.KurdranAndSkyree, 14);
            Set(UnitTypeId.Dentarg, 13);
            Set(UnitTypeId.Khadgar, 8);
            Set(UnitTypeId.GromHellscream, 10);
            Set(UnitTypeId.Turalyon, 13);
            Set(UnitTypeId.Danath, 10);
            Set(UnitTypeId.KargathBladefist, 10);
            Set(UnitTypeId.Chogall, 13);
            Set(UnitTypeId.Lothar, 13);
            Set(UnitTypeId.Guldan, 8);
            Set(UnitTypeId.UtherLightbringer, 13);
            Set(UnitTypeId.Zuljin, 10);
            Set(UnitTypeId.EyeOfKilrogg, 42);
            Set(UnitTypeId.Daemon, 14);

            // Ships
            Set(UnitTypeId.HumanTanker, 10);
            Set(UnitTypeId.OrcTanker, 10);
            Set(UnitTypeId.HumanTransport, 10);
            Set(UnitTypeId.OrcTransport, 10);
            Set(UnitTypeId.ElvenDestroyer, 10);
            Set(UnitTypeId.TrollDestroyer, 10);
            Set(UnitTypeId.Battleship, 6);
            Set(UnitTypeId.Juggernaught, 6);
            Set(UnitTypeId.GnomishSubmarine, 7);
            Set(UnitTypeId.GiantTurtle, 7);

            // Air
            Set(UnitTypeId.GnomishFlyingMachine, 17);
            Set(UnitTypeId.GoblinZeppelin, 17);
            Set(UnitTypeId.GryphonRider, 14);
            Set(UnitTypeId.Dragon, 14);
            Set(UnitTypeId.Deathwing, 14);

            return t;
        }
    }
}
