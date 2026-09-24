using UnityEngine;
using UnityEngine.InputSystem;

namespace Davidmon.Player
{
    /// <summary>
    /// Third-person follow camera: mouse orbit, scroll zoom, pitch clamp,
    /// smoothed following and sphere-cast collision to avoid clipping through walls.
    /// </summary>
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.6f, 0f);

        [Header("Orbit")]
        [SerializeField] private float yaw = 0f;
        [SerializeField] private float pitch = 12f;
        [SerializeField] private float sensitivity = 0.40f;
        [SerializeField] private float minPitch = -35f;
        [SerializeField] private float maxPitch = 70f;

        [Header("Distance")]
        [SerializeField] private float distance = 6f;
        [SerializeField] private float minDistance = 2f;
        [SerializeField] private float maxDistance = 14f;
        [SerializeField] private float zoomSpeed = 1.2f;

        [Header("Smoothing")]
        [SerializeField] private float positionSmooth = 10f;
        [SerializeField] private float rotationSmooth = 15f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.25f;

        [SerializeField] private PlayerInputProvider input;

        private Collider[] _targetColliders;

        private void Awake()
        {
            if (input == null) input = PlayerInputProvider.LocalOrAny();
        }

        private void OnEnable()
        {
            PlayerInputProvider.LocalChanged += OnLocalInputChanged;
        }

        private void OnDisable()
        {
            PlayerInputProvider.LocalChanged -= OnLocalInputChanged;
        }

        /// <summary>Follows input ownership (multiplayer spawn/parking).</summary>
        private void OnLocalInputChanged(PlayerInputProvider local)
        {
            input = local ?? PlayerInputProvider.LocalOrAny();
        }

        private void Start()
        {
            if (target == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) target = player.transform;
            }
            CacheTargetColliders();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            bool canLook = input == null || input.MovementEnabled;
            if (canLook)
            {
                Vector2 look = input != null ? input.Look.ReadValue<Vector2>() : Vector2.zero;
                yaw += look.x * sensitivity;
                pitch -= look.y * sensitivity;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

                if (input != null && Mouse.current != null)
                {
                    float scroll = Mouse.current.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                        distance = Mathf.Clamp(distance - scroll * zoomSpeed * 0.1f, minDistance, maxDistance);
                }
            }

            FollowTarget(Time.deltaTime);
        }

        private void FollowTarget(float deltaTime)
        {
            Vector3 focus = target.position + lookOffset;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 lookDirection = rotation * Vector3.forward;
            Vector3 desiredPosition = focus - lookDirection * distance;

            float resolvedDistance = distance;
            Ray ray = new Ray(focus, -lookDirection);
            RaycastHit[] hits = Physics.SphereCastAll(ray, collisionRadius, distance, collisionMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsTargetCollider(hits[i].collider)) continue;
                resolvedDistance = Mathf.Min(resolvedDistance, Mathf.Max(hits[i].distance, 0f));
            }
            desiredPosition = focus - lookDirection * resolvedDistance;

            float t = 1f - Mathf.Exp(-positionSmooth * deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, t);

            Quaternion targetRotation = Quaternion.LookRotation(focus - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-rotationSmooth * deltaTime));
        }

        /// <summary>Ping the newly spawned player as the camera follows it.</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            CacheTargetColliders();
        }

        /// <summary>Colliders belonging to the follow target so the camera ignores the player itself.</summary>
        private void CacheTargetColliders()
        {
            _targetColliders = target != null ? target.GetComponentsInChildren<Collider>(true) : null;
        }

        private bool IsTargetCollider(Collider collider)
        {
            if (collider == null) return false;
            if (target != null && collider.transform.IsChildOf(target)) return true;
            if (_targetColliders == null) return false;
            for (int i = 0; i < _targetColliders.Length; i++)
            {
                if (_targetColliders[i] == collider)
                    return true;
            }
            return false;
        }
    }
}