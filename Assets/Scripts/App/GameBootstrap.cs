using System.IO;
using Craftwar.Import;
using Craftwar.Import.War2;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// M1 bootstrap: resolve local WC2 data, parse a PUD, decode the era
    /// tileset and hand everything to the view. Grows into full match setup
    /// (lobby, lockstep driver, sim) in later milestones.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] TilemapView tilemapView;
        [SerializeField] CameraRig cameraRig;
        [Tooltip("Optional override; otherwise LocalAssetPaths.defaultMap is used.")]
        [SerializeField] string mapOverridePath = "";

        public PudFile CurrentMap { get; private set; }

        void Start()
        {
            var paths = LocalAssetPaths.Load();
            if (paths == null || string.IsNullOrEmpty(paths.maindatWar) || !File.Exists(paths.maindatWar))
            {
                Debug.LogError(
                    "[Craftwar] LocalAssetPaths.json missing or maindatWar not found. " +
                    $"Create {LocalAssetPaths.ProjectRootPath} pointing at your maindat.war.");
                return;
            }

            string mapPath = !string.IsNullOrEmpty(mapOverridePath)
                ? mapOverridePath
                : Path.Combine(paths.mapsDir ?? "", paths.defaultMap ?? "");
            if (!File.Exists(mapPath))
            {
                Debug.LogError($"[Craftwar] Map not found: {mapPath}");
                return;
            }

            var pud = PudFile.Parse(File.ReadAllBytes(mapPath));
            CurrentMap = pud;

            var archive = new War2Archive(File.ReadAllBytes(paths.maindatWar));
            var catalog = RuntimeTileCatalog.Build(archive, pud.Era);

            tilemapView.LoadMap(pud, catalog);
            cameraRig.SetMapBounds(pud.Width, pud.Height);
#if UNITY_EDITOR
            cameraRig.SetEdgeScroll(false);
#endif

            Debug.Log($"[Craftwar] Loaded '{Path.GetFileName(mapPath)}' " +
                      $"{pud.Width}x{pud.Height} {pud.Era}, {pud.Units.Count} units, " +
                      $"{catalog.TileCount} tiles decoded.");
        }
    }
}
