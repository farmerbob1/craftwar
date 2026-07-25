using Craftwar.Sim;

namespace Craftwar.View
{
    /// <summary>What a unit leaves behind once its death animation has run.</summary>
    public enum CorpseKind : byte
    {
        /// <summary>Nothing — siege engines, casters, flyers and ships on land
        /// simply stop being drawn.</summary>
        None = 0,
        Human,
        Orc,
        /// <summary>A ring of spreading water where the hull went down.</summary>
        Ship,
    }

    /// <summary>
    /// Which corpse a dying unit leaves, transcribed from the original's
    /// <c>gbUnitDeadTbl</c> (PSX <c>DISPATCH.C</c>). That table is indexed by unit
    /// type in the same order as the PUD ids, so it maps straight onto
    /// <see cref="UnitTypeId"/>.
    ///
    /// The original's rule, from <c>dispatch_die</c>: a unit whose entry is
    /// DSEQ_DEAD is freed the moment its own death frames finish; anything else
    /// turns into the shared "dead guy" unit and plays the matching sequence out
    /// of the corpse bank before disappearing. Catapults, mages, death knights,
    /// demolition squads, sappers, flyers and dragons all leave nothing — that is
    /// the original's choice, not an omission here.
    /// </summary>
    public static class UnitCorpseTable
    {
        public static CorpseKind For(UnitTypeId type) => type switch
        {
            UnitTypeId.Footman => CorpseKind.Human,
            UnitTypeId.Grunt => CorpseKind.Orc,
            UnitTypeId.Peasant => CorpseKind.Human,
            UnitTypeId.Peon => CorpseKind.Orc,
            UnitTypeId.Knight => CorpseKind.Human,
            UnitTypeId.Ogre => CorpseKind.Orc,
            UnitTypeId.Archer => CorpseKind.Human,
            UnitTypeId.Axethrower => CorpseKind.Orc,
            UnitTypeId.Paladin => CorpseKind.Human,
            UnitTypeId.OgreMage => CorpseKind.Orc,
            UnitTypeId.AttackPeasant => CorpseKind.Human,
            UnitTypeId.AttackPeon => CorpseKind.Orc,
            UnitTypeId.Ranger => CorpseKind.Human,
            UnitTypeId.Berserker => CorpseKind.Orc,

            // Heroes are absent from the original table (they sat in its unused
            // slots); each leaves what the unit it is drawn as would leave.
            UnitTypeId.Alleria => CorpseKind.Human,
            UnitTypeId.Danath => CorpseKind.Human,
            UnitTypeId.Turalyon => CorpseKind.Human,
            UnitTypeId.Lothar => CorpseKind.Human,
            UnitTypeId.UtherLightbringer => CorpseKind.Human,
            UnitTypeId.GromHellscream => CorpseKind.Orc,
            UnitTypeId.KargathBladefist => CorpseKind.Orc,
            UnitTypeId.Dentarg => CorpseKind.Orc,
            UnitTypeId.Chogall => CorpseKind.Orc,
            UnitTypeId.Zuljin => CorpseKind.Orc,

            UnitTypeId.HumanTanker or UnitTypeId.OrcTanker
                or UnitTypeId.HumanTransport or UnitTypeId.OrcTransport
                or UnitTypeId.ElvenDestroyer or UnitTypeId.TrollDestroyer
                or UnitTypeId.Battleship or UnitTypeId.Juggernaught
                or UnitTypeId.GnomishSubmarine or UnitTypeId.GiantTurtle
                => CorpseKind.Ship,

            _ => CorpseKind.None,
        };
    }
}
