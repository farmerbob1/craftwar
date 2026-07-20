using Craftwar.Sim;

namespace Craftwar.View
{
    /// <summary>
    /// Display strings, resolved from the original game's localized table.
    ///
    /// Declared in View and implemented in Import because Craftwar.View does not
    /// reference Craftwar.Import — and must not: Import is the asset/decoding
    /// layer, so a View-to-Import reference would invert the layering and drag
    /// archive code into every UI build. App references both and injects the
    /// implementation, exactly as it already does for IUnitSpriteProvider and
    /// IAudioProvider.
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
