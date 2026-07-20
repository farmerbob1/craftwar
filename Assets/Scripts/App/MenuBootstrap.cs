using Craftwar.Import;
using Craftwar.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// Entry point for the menu scene: the counterpart to GameBootstrap, and the
    /// reason UIManager had to stop hardcoding HudScreen as its root.
    ///
    /// Deliberately thin. It owns no sim, so it needs no lockstep driver, no
    /// tile catalog and no sprite bank — which is what makes it safe to load
    /// before any game data has been found. When the first-run import flow lands
    /// (Phase 8), the "no data yet" branch below is where the wizard goes.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuBootstrap : MonoBehaviour
    {
        public const string GameSceneName = "Game";

        UIManager _ui;
        UIState _uiState;

        void Start()
        {
            _uiState = new UIState();
            _ui = gameObject.AddComponent<UIManager>();
            _ui.Init(_uiState);

            ShowMainMenu();
        }

        void ShowMainMenu()
        {
            var paths = LocalAssetPaths.Load();

            // Nothing configured and nothing findable: the wizard is the whole
            // first-run experience, and it must come before the menu rather
            // than behind a dead "Single Player" button.
            if (paths == null || !paths.HasData)
            {
                var found = Wc2InstallLocator.Find();
                bool anyUsable = found.Count > 0 && found[0].IsUsable;
                if (!anyUsable || paths == null)
                {
                    _ui.SetRoot(new ImportWizardScreen(_ui, OnImportComplete));
                    return;
                }
            }

            _ui.SetRoot(new MainMenuScreen(_ui, paths, StartMatch, ShowImportWizard));
            StartMenuMusic(paths);
        }

        /// <summary>Rebuild the root screen now that data has been located.</summary>
        void OnImportComplete()
        {
            var paths = LocalAssetPaths.Load();
            _ui.SetRoot(new MainMenuScreen(_ui, paths, StartMatch, ShowImportWizard));
            StartMenuMusic(paths);
        }

        void ShowImportWizard() =>
            _ui.SetRoot(new ImportWizardScreen(_ui, OnImportComplete));

        void StartMenuMusic(LocalAssetPaths paths)
        {
            string dataRoot = paths?.dataRoot;
            if (string.IsNullOrEmpty(dataRoot))
            {
                var found = Wc2InstallLocator.Find();
                if (found.Count > 0 && found[0].IsUsable)
                    dataRoot = found[0].DataRoot;
            }
            var music = MusicLibrary.Create(paths, dataRoot);
            if (music != null)
                MusicDirector.Ensure(music).Play(MusicCue.Menu);
        }

        /// <summary>Hand the config over and switch scenes. GameBootstrap consumes it in Start().</summary>
        public static void StartMatch(MatchConfig config)
        {
            MatchSession.Pending = config;
            SceneManager.LoadScene(GameSceneName);
        }
    }
}
