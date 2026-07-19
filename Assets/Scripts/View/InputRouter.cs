using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Craftwar.View
{
    /// <summary>
    /// The single owner of gameplay input. Builds two action maps (Gameplay and
    /// Camera), turns them into typed events plus polled values, and disables
    /// both while a modal screen is up.
    ///
    /// The maps are built in code rather than loaded from a .inputactions asset:
    /// the asset route needs Unity's C# code generator to run, and the layout
    /// here is the same one the asset would hold. Bindings stay rebindable at
    /// runtime through InputActionAsset's override APIs.
    ///
    /// Hotkeys are a fixed 3x3 grid (QWE/ASD/ZXC -> card slots 0-8) rather than
    /// per-command WC2 letters: the card is dynamic, so letter hotkeys would
    /// need conflict management. The card already renders a per-slot hotkey
    /// label, so WC2 letters become a data swap later.
    ///
    /// WASD camera panning is gone — those keys are hotkeys now, and arrows +
    /// edge scroll is the WC2-faithful pan. That is a deliberate feel change and
    /// one rebind away from reverting.
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        public const int CardSlots = 9;
        static readonly string[] SlotKeys =
            { "q", "w", "e", "a", "s", "d", "z", "x", "c" };

        UIState _ui;
        UIManager _manager;

        InputActionAsset _asset;
        InputActionMap _gameplay, _camera;
        InputAction _point, _select, _order, _additive, _attackMove, _cancel, _toggleDebug;
        InputAction _pan, _zoom;
        readonly InputAction[] _cardSlots = new InputAction[CardSlots];

        public event Action OnSelectPressed;
        public event Action OnSelectReleased;
        public event Action OnOrderPressed;
        public event Action<int> OnCardSlot;
        public event Action OnEscape;
        public event Action OnToggleDebug;

        public Vector2 PointerPosition => _point?.ReadValue<Vector2>() ?? Vector2.zero;
        public Vector2 Pan => _pan?.ReadValue<Vector2>() ?? Vector2.zero;
        public float Zoom => _zoom?.ReadValue<float>() ?? 0f;
        public bool Additive => _additive != null && _additive.IsPressed();
        public bool AttackMove => _attackMove != null && _attackMove.IsPressed();

        public void Init(UIState ui, UIManager manager)
        {
            _ui = ui;
            _manager = manager;
            BuildActions();
            _gameplay.Enable();
            _camera.Enable();
        }

        void BuildActions()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();

            _gameplay = _asset.AddActionMap("Gameplay");
            _point = _gameplay.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position");
            _select = _gameplay.AddAction("Select", InputActionType.Button, "<Mouse>/leftButton");
            _order = _gameplay.AddAction("Order", InputActionType.Button, "<Mouse>/rightButton");
            _additive = _gameplay.AddAction("AdditiveModifier", InputActionType.Button, "<Keyboard>/shift");
            _attackMove = _gameplay.AddAction("AttackMoveModifier", InputActionType.Button, "<Keyboard>/leftCtrl");
            _cancel = _gameplay.AddAction("CancelOrMenu", InputActionType.Button, "<Keyboard>/escape");
            _toggleDebug = _gameplay.AddAction("ToggleDebug", InputActionType.Button, "<Keyboard>/f3");

            for (int i = 0; i < CardSlots; i++)
            {
                var action = _gameplay.AddAction("CardSlot" + i, InputActionType.Button,
                    "<Keyboard>/" + SlotKeys[i]);
                int slot = i; // capture
                action.performed += _ => OnCardSlot?.Invoke(slot);
                _cardSlots[i] = action;
            }

            _camera = _asset.AddActionMap("Camera");
            _pan = _camera.AddAction("Pan", InputActionType.PassThrough);
            _pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _zoom = _camera.AddAction("Zoom", InputActionType.PassThrough, "<Mouse>/scroll/y");

            _select.started += _ => OnSelectPressed?.Invoke();
            _select.canceled += _ => OnSelectReleased?.Invoke();
            _order.started += _ => OnOrderPressed?.Invoke();
            _cancel.performed += _ => HandleEscape();
            _toggleDebug.performed += _ => OnToggleDebug?.Invoke();
        }

        /// <summary>
        /// Escape priority: cancel placement, then close the card's advanced
        /// page, then let the screen stack consume it, and only then open the
        /// pause menu.
        /// </summary>
        void HandleEscape()
        {
            OnEscape?.Invoke();
            if (_ui.PendingBuildType != 0)
            {
                _ui.PendingBuildType = 0;
                return;
            }
            if (_manager == null)
                return;
            if (_manager.RouteEscape())
                return;
            _manager.OpenPauseMenu();
        }

        void Update()
        {
            // A modal owns the keyboard and mouse; world and camera go dead.
            bool shouldEnable = _ui == null || !_ui.ModalOpen;
            if (shouldEnable != _gameplayEnabled)
            {
                _gameplayEnabled = shouldEnable;
                // Escape must survive so the menu can close itself.
                if (shouldEnable)
                {
                    _gameplay.Enable();
                    _camera.Enable();
                }
                else
                {
                    _camera.Disable();
                    foreach (var a in _cardSlots)
                        a.Disable();
                    _select.Disable();
                    _order.Disable();
                }
            }
        }

        bool _gameplayEnabled = true;

        void OnDestroy()
        {
            _asset?.Disable();
            if (_asset != null)
                Destroy(_asset);
        }
    }
}
