using Craftwar.Sim.Pud;

namespace Craftwar.Sim
{
    /// <summary>
    /// The mutable per-match rule tables. Starts as a copy of the generated
    /// defaults (BNE data), then PUD UDTA/UGRD sections may override it for
    /// custom-balance maps — exactly the original game's mechanism.
    /// </summary>
    public sealed class RuleSet
    {
        public readonly UnitTypeData[] Units;
        public readonly UpgradeData[] Upgrades;

        RuleSet(UnitTypeData[] units, UpgradeData[] upgrades)
        {
            Units = units;
            Upgrades = upgrades;
        }

        public static RuleSet CreateDefault() =>
            new RuleSet(DefaultData.BuildUnits(), DefaultData.BuildUpgrades());

        public ref UnitTypeData UnitType(UnitTypeId id) => ref Units[(int)id];
        public ref UnitTypeData UnitType(ushort typeId) => ref Units[typeId];

        /// <summary>
        /// Fingerprint of the live tables, taken AFTER <see cref="ApplyMapOverrides"/>
        /// so it covers both generated stat-table drift and a map's own UDTA/UGRD
        /// overrides. Two peers whose rules differ by a single value will diverge
        /// hundreds of turns into a match and present it as a mystery desync;
        /// comparing this at join time turns that into a refused connection.
        /// Provenance only — never mixed into the state hash.
        /// </summary>
        public uint Hash()
        {
            var h = StateHash.Begin();
            h.Add(Units.Length);
            for (int i = 0; i < Units.Length; i++)
                Units[i].HashInto(ref h);
            h.Add(Upgrades.Length);
            for (int i = 0; i < Upgrades.Length; i++)
                Upgrades[i].HashInto(ref h);
            return h.Value;
        }

        /// <summary>
        /// Apply a map's stat overrides. PUD section payloads carry a leading
        /// u16 "use default data" word: nonzero means the map wants defaults
        /// and the rest of the payload is ignored.
        /// </summary>
        public void ApplyMapOverrides(PudFile pud)
        {
            byte[] udta = pud.UnitDataOverride;
            if (udta != null && udta.Length >= 2 && (udta[0] | (udta[1] << 8)) == 0)
            {
                var units = UdtaParser.Parse(udta, hasLeadingWord: true);
                for (int i = 0; i < units.Length; i++)
                    Units[i] = units[i];
            }

            byte[] ugrd = pud.UpgradeDataOverride;
            if (ugrd != null && ugrd.Length >= 2 && (ugrd[0] | (ugrd[1] << 8)) == 0)
            {
                var upgrades = UgrdParser.Parse(ugrd, hasLeadingWord: true);
                for (int i = 0; i < upgrades.Length; i++)
                    Upgrades[i] = upgrades[i];
            }
        }
    }
}
