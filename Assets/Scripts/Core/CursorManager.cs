using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Davidmon.Player;

namespace Davidmon.Core
{
    /// <summary>
    /// Single source of truth for cursor visibility. Full-screen UI (menus, dialogs,
    /// chat) requests modal access via <see cref="RequestModal"/>/<see cref="ReleaseModal"/>;
    /// while any is open the cursor is shown and character movement is locked. Pressing
    /// Escape with no modal open toggles a free-cursor mode so the player can click the
    /// persistent UI (e.g. the Evolutions button); pressing Escape again or clicking on
    /// empty game space resumes gameplay with a locked cursor.
    /// </summary>
    public sealed class CursorManager : MonoBehaviour
    {
        private static CursorManager Instance;

        private PlayerInputProvider _input;
        private int _modalRequests;
        private bool _freeCursor;
        private bool _subscribed;

        /// <summary>Resolves the active manager, tolerating the editor not yet assigning <see cref="Instance"/>.</summary>
        public static CursorManager Current
        {
            get
            {
                if (Instance != null) return Instance;
                var found = FindAnyObjectByType<CursorManager>();
                if (found != null) Instance = found;
                return found;
            }
        }

        /// <summary>True when the cursor is visible (a modal is open or free-cursor mode is active).</summary>
        public bool CursorVisible => _modalRequests > 0 || _freeCursor;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ServiceLocator.Register(this);
        }

        private void OnEnable()
        {
            EnsureSubscribed();
        }

        private void Start()
        {
            EnsureSubscribed(); // Start runs after every Awake, so all InputActions exist.
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (Instance == this) ServiceLocator.Unregister<CursorManager>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void EnsureSubscribed()
        {
            if (_subscribed) return;
            if (_input == null) _input = ServiceLocator.Get<PlayerInputProvider>() ?? FindAnyObjectByType<PlayerInputProvider>();
            if (_input != null && _input.EscapeMenu != null)
            {
                _input.EscapeMenu.performed += OnEscape;
                _subscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (_input != null && _input.EscapeMenu != null) _input.EscapeMenu.performed -= OnEscape;
            _subscribed = false;
        }

        /// <summary>Increments the modal count and shows the cursor.</summary>
        public void RequestModal()
        {
            _modalRequests++;
            Apply();
        }

        /// <summary>Decrements the modal count and hides the cursor once no modal is open.</summary>
        public void ReleaseModal()
        {
            if (_modalRequests > 0) _modalRequests--;
            Apply();
        }

        /// <summary>Turns the transient free-cursor mode on or off.</summary>
        public void SetFreeCursor(bool free)
        {
            _freeCursor = free;
            if (_freeCursor) _input?.SetMovementLocked(true);
            else _input?.SetMovementLocked(false);
            Apply();
        }

        private void OnEscape(InputAction.CallbackContext context)
        {
            if (_modalRequests > 0)
            {
                GameEvents.RaiseEscapeRequested();
                return;
            }
            SetFreeCursor(!_freeCursor);
        }

        private void Update()
        {
            if (!_freeCursor || _modalRequests > 0) return;
            if (IsPointerOverUi()) return;

            if (Mouse.current != null &&
                (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame))
            {
                SetFreeCursor(false);
            }
        }

        private static bool IsPointerOverUi()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        private void Apply()
        {
            bool visible = CursorVisible;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }
    }
}