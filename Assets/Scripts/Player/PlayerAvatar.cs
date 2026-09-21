using UnityEngine;
using Davidmon.Creatures;

namespace Davidmon.Player
{
    /// <summary>
    /// Renders the currently selected creature as the player's visible avatar.
    /// Instantiates the species prefab under the player root, re-centers it so it
    /// stands on the ground, and updates the controller's walk speed.
    /// </summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        [SerializeField] private ThirdPersonController controller;

        private Transform _visualRoot;
        private CreatureData _currentData;

        public CreatureData CurrentData => _currentData;
        public Transform VisualRoot => _visualRoot;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<ThirdPersonController>();
        }

        /// <summary>Swaps the visible avatar to the given species (null clears it).</summary>
        public void SetCreature(CreatureData data)
        {
            if (_visualRoot != null)
            {
                Destroy(_visualRoot.gameObject);
                _visualRoot = null;
            }

            _currentData = data;

            if (data == null || data.Prefab == null)
            {
                if (controller != null) controller.SpeedMultiplier = 1f;
                return;
            }

            GameObject instance = Instantiate(data.Prefab, transform);
            instance.name = "Avatar_" + data.CreatureId;
            _visualRoot = instance.transform;
            _visualRoot.localRotation = Quaternion.identity;
            GroundAvatar();

            if (controller != null) controller.SpeedMultiplier = data.WalkSpeedMultiplier;
        }

        private void GroundAvatar()
        {
            Bounds bounds = CalculateWorldBounds(_visualRoot);
            if (bounds.size.sqrMagnitude < 0.0001f) return;

            Vector3 localMin = _visualRoot.InverseTransformPoint(bounds.min);
            Vector3 localMax = _visualRoot.InverseTransformPoint(bounds.max);
            Vector3 localCenter = (localMin + localMax) * 0.5f;

            _visualRoot.localPosition = new Vector3(-localCenter.x, -localMin.y, -localCenter.z);
        }

        private static Bounds CalculateWorldBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}