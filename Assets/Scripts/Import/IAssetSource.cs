using System.Collections.Generic;

namespace Craftwar.Import
{
    /// <summary>
    /// Where original game data comes from. One abstraction over every asset
    /// class — sprites, tilesets, palettes, audio, icons, strings — so consumers
    /// never learn whether a loose install directory or an archive answered.
    ///
    /// Paths are *logical*: lowercase, forward-slashed, and relative to the
    /// install's Data folder, e.g. "art/bgs/forest/forest.ppl" or
    /// "gamesfx/bldg/mine.wav". Implementations map those onto whatever their
    /// backing store actually uses, which for the archive is an integer entry
    /// index with no name at all.
    /// </summary>
    public interface IAssetSource
    {
        /// <summary>False rather than throwing when the asset is absent —
        /// missing art is a normal, recoverable state (placeholder or fallback).</summary>
        bool TryRead(string logicalPath, out byte[] data);

        bool Exists(string logicalPath);

        /// <summary>Logical paths under a prefix. Used to discover variant sets
        /// (Hwhat1..5) rather than hardcoding how many exist.</summary>
        IEnumerable<string> List(string prefix);

        /// <summary>For diagnostics and the import UI, e.g. the install root.</summary>
        string Describe();
    }
}
