using UnityEngine;

namespace Davidmon.Player
{
    /// <summary>
    /// Camera-relative third-person movement using a CharacterController.
    /// Walk, run (sprint), jump, smooth acceleration and facing.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private PlayerInputProvider input;
        [SerializeField] private float walkSpeed = 6f;
        [SerializeField] private float runSpeed = 11f;
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float rotationSpeed = 720f;

        [Header("Jumping / Gravity")]
        [SerializeField] private float jumpHeight = 1.3f;
        [SerializeField] private float gravity = -22f;

        [Header("Camera")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController _controller;
        private Vector3 _moveVelocity;
        private Vector3 _movePlan;
        private float _verticalVelocity;

        public bool IsGrounded => _controller != null && _controller.isGrounded;
        public bool IsSprinting { get; private set; }
        public float CurrentSpeed => _moveVelocity.magnitude;
        public Vector3 Velocity => _moveVelocity;

        /// <summary>Applied on top of walk/run speeds. Set by the active creature's speed.
        public float SpeedMultiplier { get; set; } = 1f;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (input == null) input = GetComponent<PlayerInputProvider>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            if (input == null || !input.MovementEnabled)
            {
                _movePlan = Vector3.zero;
                IsSprinting = false;
                ApplyGravityAndMove(Time.deltaTime);
                return;
            }

            Vector2 moveInput = input.Move.ReadValue<Vector2>();
            IsSprinting = input.Sprint.IsPressed();

            float targetSpeed = (IsSprinting ? runSpeed : walkSpeed) * SpeedMultiplier;
            Vector3 moveDirection = (cameraTransform.right * moveInput.x + cameraTransform.forward * moveInput.y);
            moveDirection.y = 0f;
            moveDirection.Normalize();

            Vector3 targetVelocity = moveDirection * targetSpeed;
            _movePlan = Vector3.MoveTowards(_movePlan, targetVelocity, acceleration * Time.deltaTime);

            if (input.Jump.WasPressedThisFrame() && _controller.isGrounded)
            {
                _verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
            }

            ApplyGravityAndMove(Time.deltaTime);

            if (moveDirection.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rotationSpeed * Time.deltaTime);
            }
        }

        private void ApplyGravityAndMove(float deltaTime)
        {
            if (!_controller.isGrounded || _verticalVelocity < 0f)
            {
                _verticalVelocity += gravity * deltaTime;
            }
            else if (_verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            _moveVelocity = _movePlan;
            _moveVelocity.y = _verticalVelocity;
            _controller.Move(_moveVelocity * deltaTime);
        }
    }
}