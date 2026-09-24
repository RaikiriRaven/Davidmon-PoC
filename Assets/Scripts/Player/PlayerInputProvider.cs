using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Davidmon.Player
{
    /// <summary>
    /// Wraps the DavidmonControls InputActionAsset and exposes the gameplay actions.
    /// Other systems gate their behaviour on <see cref="MovementEnabled"/>, which gets
    /// locked while the chat input or any full-screen UI is open.
    /// </summary>
    public sealed class PlayerInputProvider : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputAsset;

        private InputActionMap _gameplay;
        private bool _movementLocked;

        public InputAction Move { get; private set; }
        public InputAction Look { get; private set; }
        public InputAction Jump { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Interact { get; private set; }
        public InputAction ChatOpen { get; private set; }
        public InputAction Attack { get; private set; }
        public InputAction Inventory { get; private set; }
        public InputAction EscapeMenu { get; private set; }

        /// <summary>The four ability-selection actions (keys 1–4). Index 0 maps to ability slot 1.</summary>
        public InputAction[] Abilities { get; private set; }

        /// <summary>
        /// The locally controlled player's input. Set by the scene player on load
        /// and claimed by the owned <c>NetworkPlayer</c> in multiplayer, so UI
        /// screens always lock/read the local avatar instead of a remote copy.
        /// Fires <see cref="LocalChanged"/> whenever ownership moves.
        /// </summary>
        public static PlayerInputProvider Local
        {
            get => _local;
            set
            {
                if (_local == value) return;
                _local = value;
                if (_local != null)
                {
                    try { LocalChanged?.Invoke(_local); }
                    catch (Exception e) { Debug.LogWarning("[PlayerInputProvider] LocalChanged failed: " + e.Message); }
                }
            }
        }
        private static PlayerInputProvider _local;

        /// <summary>Raised when the locally controlled input moves to another player object (e.g. multiplayer spawn).</summary>
        public static event Action<PlayerInputProvider> LocalChanged;

        /// <summary>Local provider when known, otherwise any provider in the scene.</summary>
        public static PlayerInputProvider LocalOrAny()
        {
            if (Local != null) return Local;
            return FindAnyObjectByType<PlayerInputProvider>();
        }

        /// <summary>True when character movement/jump should respond to input.</summary>
        public bool MovementEnabled => _gameplay != null && !_movementLocked;

        /// <summary>Fired whenever the player finishes the "Interact" action (E key).</summary>
        public event Action InteractPressed;

        /// <summary>Fired with the 0-based slot index when keys 1–4 are pressed.</summary>
        public event Action<int> AbilityPressed;

        /// <summary>Fired whenever the "Attack" action fires (left mouse / right trigger).</summary>
        public event Action AttackPressed;

        /// <summary>Fired whenever the "Inventory" action fires (I key / Tab).</summary>
        public event Action InventoryPressed;

        private void Awake()
        {
            if (Local == null) Local = this;
            if (inputAsset == null)
            {
                Debug.LogError("[PlayerInputProvider] No InputActionAsset assigned.", this);
                return;
            }

            _gameplay = inputAsset.FindActionMap("Gameplay");
            Move = _gameplay.FindAction("Move");
            Look = _gameplay.FindAction("Look");
            Jump = _gameplay.FindAction("Jump");
            Sprint = _gameplay.FindAction("Sprint");
            Interact = _gameplay.FindAction("Interact");
            ChatOpen = _gameplay.FindAction("Chat");
            Attack = _gameplay.FindAction("Attack");
            Inventory = _gameplay.FindAction("Inventory");
            EscapeMenu = _gameplay.FindAction("EscapeMenu");

            Abilities = new InputAction[4];
            for (int i = 0; i < Abilities.Length; i++)
                Abilities[i] = _gameplay.FindAction("Ability" + (i + 1));
        }

        private void OnEnable()
        {
            inputAsset?.Enable();
            if (Interact != null) Interact.performed += OnInteractPerformed;
            if (Attack != null) Attack.performed += OnAttackPerformed;
            if (Inventory != null) Inventory.performed += OnInventoryPerformed;
            if (Abilities != null)
                for (int i = 0; i < Abilities.Length; i++)
                    if (Abilities[i] != null)
                        Abilities[i].performed += OnAbilityPerformed;
        }

        private void OnDisable()
        {
            if (Interact != null) Interact.performed -= OnInteractPerformed;
            if (Attack != null) Attack.performed -= OnAttackPerformed;
            if (Inventory != null) Inventory.performed -= OnInventoryPerformed;
            if (Abilities != null)
                for (int i = 0; i < Abilities.Length; i++)
                    if (Abilities[i] != null)
                        Abilities[i].performed -= OnAbilityPerformed;
            inputAsset?.Disable();
        }

        /// <summary>
        /// Locks or unlocks character movement. Used by the chat window, menus and
        /// any other full-screen UI that should capture gameplay input.
        /// </summary>
        public void SetMovementLocked(bool locked)
        {
            _movementLocked = locked;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            InteractPressed?.Invoke();
        }

        private void OnAttackPerformed(InputAction.CallbackContext context)
        {
            AttackPressed?.Invoke();
        }

        private void OnInventoryPerformed(InputAction.CallbackContext context)
        {
            InventoryPressed?.Invoke();
        }

        private void OnAbilityPerformed(InputAction.CallbackContext context)
        {
            for (int i = 0; i < Abilities.Length; i++)
            {
                if (Abilities[i] != null && Abilities[i] == context.action)
                {
                    AbilityPressed?.Invoke(i);
                    return;
                }
            }
        }
    }
}