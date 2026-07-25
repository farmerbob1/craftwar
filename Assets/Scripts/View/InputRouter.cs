using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// The single owner of gameplay input. Reads the bindings from the
    /// <c>CraftwarControls</c> InputActionAsset, turns them into typed events
    /// plus polled values, and disables the world while a modal screen is up.
    ///
    /// Three maps:
    ///   Gameplay — pointer, selection, orders, command-card letters, groups.
    ///   Camera   — arrow pan, wheel zoom, viewport bookmarks, Alt+C centering.
    ///   System   — menu / options / speed / debug. Stays live under a modal so
    ///              F10 can close what F10 opened.
    ///
    /// Command-card shortcuts are the original's per-command letters, not a
    /// fixed grid: one CommandHotkey action carries every letter A-Z and the
    /// pressed letter is resolved against the live card (see CommandHotkeys).
    /// A letter chorded with Ctrl or Alt is not a card shortcut and is dropped
    /// here, which is what keeps Alt+C (centre) off the Cannon Tower button.
    ///
    /// WASD does not pan — arrows plus edge scroll is the original's camera.
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        /// <summary>Control groups 1-9 then 0, in key order.</summary>
        public const int GroupCount = 10;

        /// <summary>Saved camera positions on F2/F3/F4, as in the original.</summary>
        public const int ViewportSlots = 3;

        const string ControlsResource = "CraftwarControls";

        [Tooltip("Bindings asset. Left empty, it is loaded from Resources/" + ControlsResource + ".")]
        [SerializeField] InputActionAsset controls;

        UIState _ui;
        UIManager _manager;

        InputActionAsset _asset;
        InputActionMap _gameplay, _camera, _system;
        InputAction _point, _select, _order, _additive, _attackMove, _cancel;
        InputAction _groupModifier, _altModifier;
        InputAction _commandHotkey, _controlGroup;
        InputAction _pan, _zoom, _viewport, _centerOnSelection;
        InputAction _gameMenu, _options, _speedUp, _speedDown, _toggleDebug;

        public event Action OnSelectPressed;
        public event Action OnSelectReleased;
        public event Action OnOrderPressed;
        public event Action OnEscape;
        public event Action OnToggleDebug;

        /// <summary>A command-card shortcut letter was pressed (always upper case).</summary>
        public event Action<char> OnCommandHotkey;

        /// <summary>
        /// Escape reached the card. Returns true if the card consumed it by
        /// pressing its Cancel button — a plain event cannot report that back.
        /// </summary>
        public Func<bool> CardEscapeHandler;

        /// <summary>Control group key pressed: (group index, ctrl held).</summary>
        public event Action<int, bool> OnGroupKey;

        /// <summary>Viewport bookmark: (slot 0-2, shift held = save instead of recall).</summary>
        public event Action<int, bool> OnViewportKey;

        /// <summary>Alt+C — centre the camera on the current selection.</summary>
        public event Action OnCenterOnSelection;

        /// <summary>Game speed stepped by +1 or -1.</summary>
        public event Action<int> OnSpeedStep;

        public Vector2 PointerPosition => _point?.ReadValue<Vector2>() ?? Vector2.zero;
        public Vector2 Pan => _pan?.ReadValue<Vector2>() ?? Vector2.zero;
        public float Zoom => _zoom?.ReadValue<float>() ?? 0f;
        public bool Additive => _additive != null && _additive.IsPressed();
        public bool AttackMove => _attackMove != null && _attackMove.IsPressed();
        public bool GroupModifier => _groupModifier != null && _groupModifier.IsPressed();
        bool AltHeld => _altModifier != null && _altModifier.IsPressed();

        public void Init(UIState ui, UIManager manager)
        {
            _ui = ui;
            _manager = manager;
            if (!BindActions())
                return;
            _gameplay.Enable();
            _camera.Enable();
            _system.Enable();
        }

        /// <summary>
        /// Resolves every action the router drives. A missing map or action is a
        /// broken asset rather than a runtime condition, so it fails loudly and
        /// disables the component instead of silently swallowing input.
        /// </summary>
        bool BindActions()
        {
            _asset = controls != null ? controls : Resources.Load<InputActionAsset>(ControlsResource);
            if (_asset == null)
            {
                Debug.LogError($"[Craftwar] Input asset '{ControlsResource}' not found. " +
                               "Assign it on InputRouter or restore " +
                               $"Assets/Resources/{ControlsResource}.inputactions.");
                enabled = false;
                return false;
            }

            // Cloned so runtime rebinds and the enabled/disabled state belong to
            // this match and never write back into the project asset.
            _asset = Instantiate(_asset);

            _gameplay = RequireMap("Gameplay");
            _camera = RequireMap("Camera");
            _system = RequireMap("System");
            if (_gameplay == null || _camera == null || _system == null)
            {
                enabled = false;
                return false;
            }

            _point = Require(_gameplay, "Point");
            _select = Require(_gameplay, "Select");
            _order = Require(_gameplay, "Order");
            _additive = Require(_gameplay, "AdditiveModifier");
            _attackMove = Require(_gameplay, "AttackMoveModifier");
            _groupModifier = Require(_gameplay, "GroupModifier");
            _altModifier = Require(_gameplay, "AltModifier");
            _cancel = Require(_gameplay, "Cancel");
            _commandHotkey = Require(_gameplay, "CommandHotkey");
            _controlGroup = Require(_gameplay, "ControlGroup");

            _pan = Require(_camera, "Pan");
            _zoom = Require(_camera, "Zoom");
            _viewport = Require(_camera, "Viewport");
            _centerOnSelection = Require(_camera, "CenterOnSelection");

            _gameMenu = Require(_system, "GameMenu");
            _options = Require(_system, "Options");
            _speedUp = Require(_system, "SpeedUp");
            _speedDown = Require(_system, "SpeedDown");
            _toggleDebug = Require(_system, "ToggleDebug");

            if (_missing)
            {
                enabled = false;
                return false;
            }

            _select.started += _ => OnSelectPressed?.Invoke();
            _select.canceled += _ => OnSelectReleased?.Invoke();
            _order.started += _ => OnOrderPressed?.Invoke();
            _cancel.performed += _ => HandleEscape();
            _commandHotkey.performed += HandleCommandHotkey;
            _controlGroup.performed += HandleControlGroup;
            _viewport.performed += HandleViewport;
            _centerOnSelection.performed += _ => OnCenterOnSelection?.Invoke();

            _gameMenu.performed += _ => _manager?.OpenPauseMenu();
            _options.performed += _ => OpenOptions();
            _speedUp.performed += _ => OnSpeedStep?.Invoke(1);
            _speedDown.performed += _ => OnSpeedStep?.Invoke(-1);
            _toggleDebug.performed += _ => OnToggleDebug?.Invoke();
            return true;
        }

        bool _missing;

        InputActionMap RequireMap(string name)
        {
            var map = _asset.FindActionMap(name, throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError($"[Craftwar] Input asset has no '{name}' action map.");
                _missing = true;
            }
            return map;
        }

        InputAction Require(InputActionMap map, string name)
        {
            var action = map.FindAction(name, throwIfNotFound: false);
            if (action == null)
            {
                Debug.LogError($"[Craftwar] Input map '{map.name}' has no '{name}' action.");
                _missing = true;
            }
            return action;
        }

        /// <summary>
        /// A-Z go to the command card. Ctrl and Alt chords are other people's
        /// shortcuts (Alt+C centres, Ctrl+N assigns a group), so a held modifier
        /// means this is not a card press.
        /// </summary>
        void HandleCommandHotkey(InputAction.CallbackContext ctx)
        {
            if (GroupModifier || AltHeld)
                return;
            string name = ctx.control?.name;
            if (string.IsNullOrEmpty(name) || name.Length != 1)
                return;
            OnCommandHotkey?.Invoke(char.ToUpperInvariant(name[0]));
        }

        /// <summary>Digits 1-9 then 0 map onto groups 0-9.</summary>
        void HandleControlGroup(InputAction.CallbackContext ctx)
        {
            string name = ctx.control?.name;
            if (string.IsNullOrEmpty(name) || name.Length != 1)
                return;
            char c = name[0];
            if (c < '0' || c > '9')
                return;
            int group = c == '0' ? GroupCount - 1 : c - '1';
            OnGroupKey?.Invoke(group, GroupModifier);
        }

        /// <summary>F2/F3/F4 recall a saved camera position; Shift+Fn saves one.</summary>
        void HandleViewport(InputAction.CallbackContext ctx)
        {
            string name = ctx.control?.name;
            if (string.IsNullOrEmpty(name) || name.Length != 2 || name[0] != 'f')
                return;
            int slot = name[1] - '2';
            if ((uint)slot >= ViewportSlots)
                return;
            OnViewportKey?.Invoke(slot, Additive);
        }

        void OpenOptions()
        {
            if (_manager != null && !_manager.HasScreen<OptionsScreen>())
                _manager.Push(new OptionsScreen(_manager));
        }

        /// <summary>
        /// Escape priority: cancel placement, then the screen stack (which
        /// closes the card's build page), then the card's own Cancel button,
        /// and only then open the game menu.
        /// </summary>
        void HandleEscape()
        {
            OnEscape?.Invoke();
            if (_ui.HasPendingOrder)
            {
                _ui.ClearPendingOrder();
                return;
            }
            if (_manager == null)
                return;
            if (_manager.RouteEscape())
                return;
            if (CardEscapeHandler != null && CardEscapeHandler())
                return;
            _manager.OpenPauseMenu();
        }

        void Update()
        {
            // A modal owns the keyboard and mouse; world and camera go dead.
            // The System map stays live so F10/F5 can close what they opened.
            bool shouldEnable = _ui == null || !_ui.ModalOpen;
            if (shouldEnable == _gameplayEnabled)
                return;
            _gameplayEnabled = shouldEnable;

            if (shouldEnable)
            {
                _gameplay.Enable();
                _camera.Enable();
                return;
            }

            _camera.Disable();
            _commandHotkey.Disable();
            _controlGroup.Disable();
            _select.Disable();
            _order.Disable();
            // Escape must survive so the menu can close itself.
        }

        bool _gameplayEnabled = true;

        void OnDestroy()
        {
            if (_asset == null)
                return;
            _asset.Disable();
            Destroy(_asset);
        }
    }
}
