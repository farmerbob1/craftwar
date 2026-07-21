using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using Craftwar.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Craftwar.App
{
    /// <summary>
    /// The menu scene's one driver. The layout lives in the scene — MainMenu.uxml
    /// assigned to this GameObject's UIDocument — so this only wires behaviour:
    /// shows one panel at a time (main / skirmish setup / locate-data), fills the
    /// dynamic bits, and hands a chosen match over to the game scene. It owns no
    /// sim, which is what lets the menu load before any game data has been found.
    /// Replaces MenuBootstrap and the three code-built menu UIScreens.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        public const string GameSceneName = "Game";

        LocalAssetPaths _paths;
        VisualElement _panelMain, _panelSetup, _panelWizard;
        bool _musicStarted;

        // Setup panel
        List<MapEntry> _maps;
        int _mapSel;
        Label _mapLabel, _setupWarn;
        VisualElement _mapRow;
        Button _setupStart;

        // Wizard panel
        List<InstallCandidate> _candidates;
        int _candSel;
        Label _wizardDetail;
        VisualElement _candidateList;
        TextField _manualPath;

        void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null || root.Q("panel-main") == null)
            {
                Debug.LogError("[Craftwar] MainMenu.uxml is not assigned to the menu " +
                               "UIDocument's Source Asset. Run Craftwar/Setup/Ensure Menu Scene.");
                return;
            }

            _panelMain = root.Q("panel-main");
            _panelSetup = root.Q("panel-setup");
            _panelWizard = root.Q("panel-wizard");

            // Main panel
            root.Q<Button>("single-player").clicked += ShowSetup;
            root.Q<Button>("locate").clicked += ShowWizard;
            root.Q<Button>("quit").clicked += Quit;

            // Setup panel
            _mapLabel = root.Q<Label>("map-label");
            _setupWarn = root.Q<Label>("setup-warn");
            _mapRow = root.Q("map-row");
            _setupStart = root.Q<Button>("setup-start");
            root.Q<Button>("map-prev").clicked += () => Step(-1);
            root.Q<Button>("map-next").clicked += () => Step(1);
            _setupStart.clicked += StartSkirmish;
            root.Q<Button>("setup-back").clicked += ShowMain;

            // Wizard panel
            _candidateList = root.Q("candidates");
            _wizardDetail = root.Q<Label>("wizard-detail");
            _manualPath = root.Q<TextField>("manual-path");
            root.Q<Button>("wizard-check").clicked += CheckManual;
            root.Q<Button>("wizard-use").clicked += AcceptInstall;
            root.Q<Button>("wizard-rescan").clicked += Rescan;
            root.Q<Button>("wizard-back").clicked += ShowMain;

            _paths = LocalAssetPaths.Load();

            // Nothing configured and nothing findable: the wizard is the whole
            // first-run experience, and it must come before the menu rather than
            // behind a dead "Single Player" button.
            bool needWizard = _paths == null || !_paths.HasData;
            if (needWizard && AssetResolution.TryFindUsableInstall(out _) && _paths != null)
                needWizard = false;

            if (needWizard)
                ShowWizard();
            else
                ShowMain();
        }

        // --- Panel switching ---------------------------------------------------

        static void Show(VisualElement panel, bool visible) =>
            panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        void ShowMain()
        {
            Show(_panelMain, true);
            Show(_panelSetup, false);
            Show(_panelWizard, false);

            bool haveData = _paths != null && _paths.HasData;
            _panelMain.Q<Button>("single-player").SetEnabled(haveData);
            Show(_panelMain.Q("main-warn"), !haveData);

            StartMenuMusic();
        }

        void ShowSetup()
        {
            _maps = MapList.Find(_paths);
            _mapSel = 0;
            bool haveMaps = _maps.Count > 0;
            Show(_setupWarn, !haveMaps);
            Show(_mapRow, haveMaps);
            _setupStart.SetEnabled(haveMaps);
            if (haveMaps)
                _mapLabel.text = _maps[0].Label;

            Show(_panelMain, false);
            Show(_panelSetup, true);
            Show(_panelWizard, false);
        }

        void ShowWizard()
        {
            Show(_panelMain, false);
            Show(_panelSetup, false);
            Show(_panelWizard, true);
            Rescan();
        }

        // --- Skirmish setup ----------------------------------------------------

        void Step(int delta)
        {
            if (_maps == null || _maps.Count == 0)
                return;
            _mapSel = (_mapSel + delta + _maps.Count) % _maps.Count;
            _mapLabel.text = _maps[_mapSel].Label;
        }

        void StartSkirmish()
        {
            if (_maps == null || _maps.Count == 0)
                return;
            // Slots stay null: GameSim.Setup derives controllers, races and teams
            // from the PUD's own OWNR/SIDE, exactly as before.
            StartMatch(MatchConfig.FromMapDefaults(_maps[_mapSel].Value));
        }

        /// <summary>Hand the config over and switch scenes. GameLoopRunner consumes it in Start().</summary>
        public static void StartMatch(MatchConfig config)
        {
            MatchSession.Pending = config;
            SceneManager.LoadScene(GameSceneName);
        }

        // --- Locate data (import wizard) ---------------------------------------

        void Rescan()
        {
            _candidates = Wc2InstallLocator.Find();
            _candSel = 0;
            RebuildCandidates();
        }

        void CheckManual()
        {
            string path = _manualPath?.value?.Trim();
            if (string.IsNullOrEmpty(path))
                return;
            if (!Directory.Exists(path))
            {
                _wizardDetail.text = "That folder does not exist.";
                return;
            }

            _candidates.Insert(0, Wc2InstallLocator.Inspect(path, "chosen by you"));
            _candSel = 0;
            RebuildCandidates();
        }

        void RebuildCandidates()
        {
            _candidateList.Clear();
            if (_candidates.Count == 0)
            {
                _wizardDetail.text = "No installation found. Enter the folder containing " +
                                     "your Warcraft II data (the one holding Art and Gamesfx).";
                return;
            }

            for (int i = 0; i < _candidates.Count && i < 4; i++)
            {
                int index = i; // capture
                var b = new Button(() => { _candSel = index; ShowCandidateDetail(); })
                {
                    name = $"cand{i}",
                    text = _candidates[i].Origin,
                };
                b.AddToClassList("menu__button");
                _candidateList.Add(b);
            }
            ShowCandidateDetail();
        }

        void ShowCandidateDetail()
        {
            if (_candidates.Count == 0)
                return;
            var c = _candidates[_candSel];

            // Name the parts rather than a bare pass/fail: a partial copy is
            // playable, and the player should see what they will miss.
            string parts = string.Join("   ",
                Mark(c.HasTilesets) + " terrain",
                Mark(c.HasSprites) + " units",
                Mark(c.HasSounds) + " sound",
                Mark(c.HasIcons) + " icons",
                Mark(c.HasStrings) + " names");

            _wizardDetail.text = c.DataRoot + "\n" + parts
                + (c.IsUsable ? string.Empty : "\n\nTerrain and units are required.");
        }

        static string Mark(bool ok) => ok ? "+" : "-";

        void AcceptInstall()
        {
            if (_candidates.Count == 0)
                return;
            var c = _candidates[_candSel];
            if (!c.IsUsable)
            {
                _wizardDetail.text = "That folder is missing terrain or unit art.\n" + c.DataRoot;
                return;
            }

            var paths = LocalAssetPaths.Load() ?? new LocalAssetPaths();
            paths.dataRoot = c.DataRoot;
            var mapFolders = Wc2InstallLocator.MapFolders(c.DataRoot);
            if (mapFolders.Count > 0)
                paths.mapsDir = mapFolders[0];

            try
            {
                // persistentDataPath, not the project root: players have no project.
                paths.Save(LocalAssetPaths.PersistentPath);
            }
            catch (IOException e)
            {
                _wizardDetail.text = "Could not save settings: " + e.Message;
                return;
            }

            Debug.Log($"[Craftwar] Import complete: {c.DataRoot}");
            _paths = LocalAssetPaths.Load();
            ShowMain();
        }

        // --- Music -------------------------------------------------------------

        void StartMenuMusic()
        {
            if (_musicStarted)
                return;
            string dataRoot = _paths?.dataRoot;
            if (string.IsNullOrEmpty(dataRoot))
                AssetResolution.TryFindUsableInstall(out dataRoot);
            var music = MusicLibrary.Create(_paths, dataRoot);
            if (music != null)
            {
                MusicDirector.Ensure(music).Play(MusicCue.Menu);
                _musicStarted = true;
            }
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
