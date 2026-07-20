using Craftwar.Sim;

namespace Craftwar.View
{
    /// <summary>
    /// Display strings, resolved from the original game's localized table.
    ///
    /// Declared here and implemented in App. View must not reference Import —
    /// that would invert the layering and drag asset-decoding code into every UI
    /// build — and Import cannot reference View either, since they are siblings.
    /// So the implementation belongs in App, which references both, exactly as
    /// it already does for IUnitSpriteProvider and IAudioProvider.
    ///
    /// Every method must degrade gracefully: a player with no game data
    /// installed still gets a usable UI from <see cref="UnitNames"/>'s
    /// reflection-derived fallback.
    /// </summary>
    public interface IStringTable
    {
        /// <summary>Localized unit/building name, or null if unknown.</summary>
        string UnitName(UnitTypeId type);

        /// <summary>Raw lookup by key, or null if absent.</summary>
        string Get(string key);
    }
}
