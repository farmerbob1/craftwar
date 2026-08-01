using System.Collections.Generic;
using System.IO;
using Craftwar.Import;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;
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
    public sealed partial class MainMenuController : MonoBehaviour
    {
        public const string GameSceneName = "Game";

        LocalAssetPaths _paths;
        VisualElement _root;
        VisualElement _panelMain, _panelSetup, _panelWizard, _panelOptions;
        bool _musicStarted;

        // Options panel
        Button _optFog, _optSpeed, _tabGameplay, _tabSound;
        VisualElement _optPageGameplay, _optPageSound;

        // Setup panel
        List<MapEntry> _maps;
        int _mapSel;
        Label _mapLabel, _setupWarn;
        VisualElement _mapRow, _slotList;
        Image _setupMapThumb;
        Button _setupStart;

        /// <summary>One configurable seat of the selected map. Defaults come
        /// from the PUD's OWNR/SIDE/AIPL; the human is fixed to slot 0 for M9
        /// (the view hard-codes LocalPlayer = 0).</summary>
        sealed class SlotRow
        {
            public int Slot;
            public Controller Controller;
            public Race Race;
            public byte AiType;
            public AiTier Tier = AiTier.Normal;
            public string Strategy = AiProfileLibrary.DefaultName;
            public Button CtrlBtn;
            public Button RaceBtn;
            public Button StratBtn;
            public Button DiffBtn;
        }

        // Selectable AI strategies (built-ins + player files), computed when the
        // setup panel is built so a freshly-dropped mod file appears next time.
        List<string> _strategyNames;

        PudFile _setupPud;
        readonly List<SlotRow> _slotRows = new List<SlotRow>();

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

            _root = root;
            _panelMain = root.Q("panel-main");
            _panelSetup = root.Q("panel-setup");
            _panelWizard = root.Q("panel-wizard");
            _panelOptions = root.Q("panel-options");

            // Main panel
            root.Q<Button>("single-player").clicked += ShowSetup;
            root.Q<Button>("locate").clicked += ShowWizard;
            InitLan(root);
            InitOnline(root);
            InitSocial(root);
            root.Q<Button>("quit").clicked += Quit;
            root.Q<Button>("options").clicked += ShowOptions;

            // Options panel (may be absent from an older scene's UXML)
            _optFog = root.Q<Button>("opt-fog");
            _optSpeed = root.Q<Button>("opt-speed");
            if (_optFog != null)
                _optFog.clicked += () =>
                {
                    GameplaySettings.Current.revealMap = !GameplaySettings.Current.revealMap;
                    GameplaySettings.Save();
                    RefreshOptionLabels();
                };
            if (_optSpeed != null)
                _optSpeed.clicked += () =>
                {
                    GameplaySettings.Current.CycleSpeed(1);
                    GameplaySettings.Save();
                    RefreshOptionLabels();
                };
            // Sound tab. Built in code from the shared panel so the main menu and
            // the in-game options screen cannot drift apart.
            _tabGameplay = root.Q<Button>("tab-gameplay");
            _tabSound = root.Q<Button>("tab-sound");
            _optPageGameplay = root.Q("options-page-gameplay");
            _optPageSound = root.Q("options-page-sound");
            if (_optPageSound != null)
                View.SoundOptionsPanel.Build(_optPageSound);
            if (_tabGameplay != null)
                _tabGameplay.clicked += () => ShowOptionsTab(soundTab: false);
            if (_tabSound != null)
                _tabSound.clicked += () => ShowOptionsTab(soundTab: true);

            root.Q<Button>("options-back")?.RegisterCallback<ClickEvent>(_ =>
            {
                GameplaySettings.Save();
                ShowMain();
            });

            // Setup panel
            _mapLabel = root.Q<Label>("map-label");
            _setupWarn = root.Q<Label>("setup-warn");
            _mapRow = root.Q("map-row");
            _slotList = root.Q("slot-list");
            _setupMapThumb = root.Q<Image>("setup-map-thumb");
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
            if (_panelOptions != null)
                Show(_panelOptions, false);
            HideLanPanels();

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
            LoadSetupPud();

            Show(_panelMain, false);
            Show(_panelSetup, true);
            Show(_panelWizard, false);
        }

        void ShowWizard()
        {
            Show(_panelMain, false);
            Show(_panelSetup, false);
            Show(_panelWizard, true);
            if (_panelOptions != null)
                Show(_panelOptions, false);
            Rescan();
        }

        void ShowOptions()
        {
            if (_panelOptions == null)
                return;
            RefreshOptionLabels();
            ShowOptionsTab(soundTab: false);
            Show(_panelMain, false);
            Show(_panelSetup, false);
            Show(_panelWizard, false);
            Show(_panelOptions, true);
        }

        /// <summary>Swap options pages. The selected tab is the disabled one —
        /// same rule as the in-game screen, and it is what the USS styles.</summary>
        void ShowOptionsTab(bool soundTab)
        {
            if (_optPageGameplay != null)
                Show(_optPageGameplay, !soundTab);
            if (_optPageSound != null)
                Show(_optPageSound, soundTab);
            _tabGameplay?.SetEnabled(soundTab);
            _tabSound?.SetEnabled(!soundTab);
        }

        void RefreshOptionLabels()
        {
            var s = GameplaySettings.Current;
            if (_optFog != null)
                _optFog.text = s.revealMap ? "Fog of War: Off" : "Fog of War: On";
            if (_optSpeed != null)
                _optSpeed.text = $"Game Speed: {s.SpeedLabel}";
        }

        // --- Skirmish setup ----------------------------------------------------

        void Step(int delta)
        {
            if (_maps == null || _maps.Count == 0)
                return;
            _mapSel = (_mapSel + delta + _maps.Count) % _maps.Count;
            _mapLabel.text = _maps[_mapSel].Label;
            LoadSetupPud();
        }

        /// <summary>Parse the selected map so the slot rows can be offered.
        /// On any failure the rows vanish and Start falls back to the PUD's
        /// own OWNR/SIDE — exactly the pre-M9 behaviour.</summary>
        void LoadSetupPud()
        {
            _setupPud = null;
            if (_maps != null && _maps.Count > 0)
            {
                try
                {
                    if (MapList.TryReadMapBytes(_paths, _maps[_mapSel].Value, out var bytes))
                        _setupPud = PudFile.Parse(bytes);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Craftwar] No slot setup for this map: {e.Message}");
                }
            }
            if (_setupMapThumb != null)
                _setupMapThumb.image = BakeThumbnailFromPud(_setupPud, ThumbnailMaxDimension);
            RebuildSlotRows();
        }

        void RebuildSlotRows()
        {
            _slotRows.Clear();
            if (_slotList == null)
                return;
            _slotList.Clear();
            if (_setupPud == null)
                return;
            _strategyNames = AiProfileLibrary.Names();

            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                if (MatchSetup.ControllerFor(_setupPud.Owner[p]) == Controller.None)
                    continue;
                var row = new SlotRow
                {
                    Slot = p,
                    // Seat 0 defaults to "You", but the seat is no longer fixed:
                    // the view reads MatchConfig.localSlot now, so any playable
                    // seat can be the human one. Playing a skirmish as a seat
                    // other than 0 is also how the local-slot plumbing gets
                    // exercised before a LAN client depends on it.
                    Controller = p == 0 ? Controller.Human : Controller.Computer,
                    Race = _setupPud.Side[p] == (byte)Race.Orc ? Race.Orc : Race.Human,
                    AiType = _setupPud.AiType[p],
                };
                _slotRows.Add(row);

                var line = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var label = new Label($"Slot {p + 1}") { pickingMode = PickingMode.Ignore };
                label.AddToClassList("text");
                label.style.width = 70;
                line.Add(label);

                row.CtrlBtn = new Button(() => CycleController(row)) { text = "" };
                row.CtrlBtn.AddToClassList("menu__button");
                row.CtrlBtn.style.flexGrow = 1;
                line.Add(row.CtrlBtn);

                row.RaceBtn = new Button(() => CycleRace(row)) { text = "" };
                row.RaceBtn.AddToClassList("menu__button");
                row.RaceBtn.style.width = 90;
                line.Add(row.RaceBtn);

                // AI strategy + difficulty, meaningful only for Computer slots.
                row.StratBtn = new Button(() => CycleStrategy(row)) { text = "" };
                row.StratBtn.AddToClassList("menu__button");
                row.StratBtn.style.width = 130;
                line.Add(row.StratBtn);

                row.DiffBtn = new Button(() => CycleTier(row)) { text = "" };
                row.DiffBtn.AddToClassList("menu__button");
                row.DiffBtn.style.width = 90;
                line.Add(row.DiffBtn);

                UpdateRowLabels(row);
                _slotList.Add(line);
            }
        }

        /// <summary>
        /// You -> Computer -> Off -> You. Exactly one row may be "You": taking
        /// the seat hands the previous holder to the computer, so the lobby
        /// cannot reach a state with two humans or none.
        /// </summary>
        void CycleController(SlotRow row)
        {
            row.Controller = row.Controller switch
            {
                Controller.Human => Controller.Computer,
                Controller.Computer => Controller.None,
                _ => Controller.Human,
            };

            if (row.Controller == Controller.Human)
            {
                foreach (var other in _slotRows)
                    if (other != row && other.Controller == Controller.Human)
                    {
                        other.Controller = Controller.Computer;
                        UpdateRowLabels(other);
                    }
            }
            else if (!_slotRows.Exists(r => r.Controller == Controller.Human))
            {
                // Never leave the match without a seat for the player.
                row.Controller = Controller.Human;
            }

            UpdateRowLabels(row);
        }

        void CycleRace(SlotRow row)
        {
            row.Race = row.Race == Race.Orc ? Race.Human : Race.Orc;
            UpdateRowLabels(row);
        }

        void CycleStrategy(SlotRow row)
        {
            if (_strategyNames == null || _strategyNames.Count == 0)
                return;
            int i = _strategyNames.IndexOf(row.Strategy);
            row.Strategy = _strategyNames[(i + 1) % _strategyNames.Count];
            UpdateRowLabels(row);
        }

        void CycleTier(SlotRow row)
        {
            row.Tier = row.Tier switch
            {
                AiTier.Dumb => AiTier.Normal,
                AiTier.Normal => AiTier.Smart,
                AiTier.Smart => AiTier.God,
                _ => AiTier.Dumb,
            };
            UpdateRowLabels(row);
        }

        static void UpdateRowLabels(SlotRow row)
        {
            row.CtrlBtn.text = row.Controller switch
            {
                Controller.Human => "You",
                Controller.Computer => row.AiType == 0x01 ? "Computer (passive)" : "Computer",
                _ => "Off",
            };
            row.RaceBtn.text = row.Race == Race.Orc ? "Orc" : "Human";
            row.RaceBtn.SetEnabled(row.Controller != Controller.None);

            // Strategy/difficulty apply only to an active Computer that actually plays.
            bool aiActive = row.Controller == Controller.Computer && row.AiType != 0x01;
            row.StratBtn.text = row.Strategy;
            row.DiffBtn.text = row.Tier.ToString();
            row.StratBtn.SetEnabled(aiActive);
            row.DiffBtn.SetEnabled(aiActive);
        }

        void StartSkirmish()
        {
            if (_maps == null || _maps.Count == 0)
                return;
            if (_setupPud == null || _slotRows.Count == 0)
            {
                // No parsed map to configure: the PUD's own OWNR/SIDE decide,
                // exactly as before M9.
                StartMatch(MatchConfig.FromMapDefaults(_maps[_mapSel].Value));
                return;
            }

            // Whichever seat is marked "You" is the one this client drives.
            var humanRow = _slotRows.Find(r => r.Controller == Controller.Human);
            var config = new MatchConfig
            {
                mapPath = _maps[_mapSel].Value,
                localSlot = (byte)(humanRow?.Slot ?? 0),
                slots = new SlotConfig[SimConstants.MaxPlayers],
            };
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                config.slots[p] = new SlotConfig
                {
                    controller = Controller.None,
                    race = Race.Human,
                    team = (byte)p,
                };
            foreach (var row in _slotRows)
                config.slots[row.Slot] = new SlotConfig
                {
                    controller = row.Controller,
                    race = row.Race,
                    team = (byte)row.Slot, // free-for-all, like melee defaults
                    aiType = row.AiType,
                    aiStrategy = row.Strategy,
                    aiTier = (byte)row.Tier,
                };
            StartMatch(config);
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
            var music = BakedMusicLibrary.Load();
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

        /// <summary>Safety net: GameplaySettings otherwise only saves on a
        /// slider's PointerUpEvent or an options button click, so a drag
        /// whose release doesn't land cleanly (easy in the small Editor Game
        /// View) can leave an in-session change unsaved. Unity raises this
        /// both for a real quit and for stopping Play Mode in the editor, so
        /// it covers "changed a setting, stopped Play Mode, it reverted".</summary>
        void OnApplicationQuit() => GameplaySettings.Save();
    }
}
