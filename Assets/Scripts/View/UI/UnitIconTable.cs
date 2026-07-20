using Craftwar.Sim;

namespace Craftwar.View
{
    /// <summary>
    /// Icon index per unit type, into the 196-frame HUD atlas.
    ///
    /// Hand-authored, because there is no source for it: UDTA carries 26 fields
    /// and none is an icon (UGRD does have one, which is why upgrade icons are
    /// data-driven and these are not). Built by rendering the atlas as a
    /// labelled contact sheet and reading it.
    ///
    /// Lives in View, not Sim: an icon index is presentation. UpgradeData.Icon
    /// sitting in Sim is a pre-existing wart justified only by it being what
    /// UGRD parsing yields.
    ///
    /// **Deliberately partial, and unverified in play.** Anything absent falls
    /// back to the initials box, which is the same readable placeholder the card
    /// has used since M4 — a missing icon costs polish, a wrong icon is a
    /// visible defect. Entries below the confident line should be checked
    /// against the real game before more are added.
    /// </summary>
    public static class UnitIconTable
    {
        public const int None = -1;

        /// <summary>Atlas index, or <see cref="None"/> to fall back to initials.</summary>
        public static int IconFor(UnitTypeId type) => type switch
        {
            // --- Confident: distinctive art, unambiguous subject ---
            UnitTypeId.Footman => 0,
            UnitTypeId.Grunt => 1,
            UnitTypeId.Ballista => 16,
            UnitTypeId.Catapult => 17,
            UnitTypeId.EyeOfKilrogg => 29,
            UnitTypeId.GryphonRider => 30,
            UnitTypeId.Dragon => 31,
            UnitTypeId.Farm => 38,
            UnitTypeId.PigFarm => 39,
            UnitTypeId.HumanBarracks => 40,
            UnitTypeId.OrcBarracks => 41,

            // Upgraded forms share their base unit's art in the original too.
            UnitTypeId.Deathwing => 31,

            _ => None,
        };

        public static bool Has(UnitTypeId type) => IconFor(type) != None;
    }
}
