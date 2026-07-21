using Craftwar.Import;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// The single place that turns a (possibly null) <see cref="LocalAssetPaths"/>
    /// into a live WC2 data source. Both bootstraps used to do this dance
    /// independently: prefer the configured install, otherwise auto-detect one so
    /// a dev machine with no LocalAssetPaths.json still runs. Extracted so the
    /// "find an install" fallback lives in exactly one spot.
    /// </summary>
    public static class AssetResolution
    {
        /// <summary>
        /// Build an asset source, preferring the configured <c>dataRoot</c> and
        /// falling back to auto-detecting an install. Returns null when no usable
        /// WC2 data exists — the first-run state, which callers must handle.
        /// <paramref name="dataRoot"/> receives the backing install folder (music
        /// reads whole files straight from it rather than through the source).
        /// </summary>
        public static IAssetSource ResolveAssetSource(LocalAssetPaths paths, out string dataRoot)
        {
            var assets = paths?.CreateAssetSource();
            if (assets != null)
            {
                dataRoot = paths.dataRoot;
                return assets;
            }

            // dataRoot is unset or gone. Try to find an install anyway, so a dev
            // machine with no LocalAssetPaths.json still runs.
            if (TryFindUsableInstall(out dataRoot))
            {
                Debug.Log($"[Craftwar] Auto-detected install: {dataRoot}");
                return new LooseFileAssetSource(dataRoot);
            }

            return null; // dataRoot already null
        }

        /// <summary>
        /// The highest-confidence auto-detected install, if any is usable
        /// (tilesets + sprites present). Used both to resolve a data root for
        /// music and to decide whether the first-run wizard is still needed.
        /// </summary>
        public static bool TryFindUsableInstall(out string dataRoot)
        {
            var found = Wc2InstallLocator.Find();
            if (found.Count > 0 && found[0].IsUsable)
            {
                dataRoot = found[0].DataRoot;
                return true;
            }
            dataRoot = null;
            return false;
        }
    }
}
