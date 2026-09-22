using UnityEngine;
using Davidmon.Core;

namespace Davidmon.NPC
{
    /// <summary>
    /// A standing NPC: renders a placeholder body from <see cref="NpcData"/> colours
    /// (or a real prefab), faces the player while talked to, registers itself in the
    /// <see cref="NPCRegistry"/> and hands conversation over to the
    /// <see cref="DialogueSystem"/> when the player interacts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Npc : MonoBehaviour, IInteractable
    {
        [SerializeField] private string npcId;
        [SerializeField] private NpcData data;

        private Transform _player;
        private float _playerRefreshTimer;
        private bool _playerInRange;
        private float _yaw;
        private Transform _visualRoot;

        public NpcData Data => data;
        public string NpcId => !string.IsNullOrEmpty(npcId) ? npcId : gameObject.name;
        public Transform VisualRoot => _visualRoot;
        public bool IsPlayerInRange => _playerInRange;

        // ----- IInteractable -----
        public string DisplayName => data != null ? data.DisplayName : gameObject.name;
        public string InteractionHint => data != null ? data.InteractionHint : "Talk";
        public bool CanInteract => data != null && _playerInRange;

        /// <summary>Spawns and initialises an NPC without scene references.</summary>
        public static Npc Spawn(NpcData npcData, Vector3 at, Transform parent = null)
        {
            if (npcData == null) return null;

            var go = new GameObject("Npc_" + npcData.DisplayName);
            Npc npc = go.AddComponent<Npc>();
            if (parent != null) go.transform.SetParent(parent, true);

            npc.Init(npcData, at);
            return npc;
        }

        private void Init(NpcData npcData, Vector3 at)
        {
            data = npcData;
            npcId = npcData.NpcId;
            transform.position = at;
            BuildVisual();
        }

        private void OnEnable()
        {
            NPCRegistry.Register(this);
        }

        private void OnDisable()
        {
            NPCRegistry.Unregister(this);
        }

        private void Update()
        {
            if (data == null) return;
            RefreshPlayer();

            Vector3 toPlayer = _player != null ? _player.position - transform.position : Vector3.zero;
            toPlayer.y = 0f;
            _playerInRange = _player != null
                && toPlayer.sqrMagnitude <= data.InteractRadius * data.InteractRadius;

            if (_playerInRange && toPlayer.sqrMagnitude > 0.001f)
            {
                float targetYaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
                _yaw = Mathf.LerpAngle(_yaw, targetYaw, 8f * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
        }

        public void Interact()
        {
            if (!CanInteract) return;
            ServiceLocator.GetOrCreate(() => new DialogueSystem()).Open(this);
        }

        private void BuildVisual()
        {
            if (_visualRoot != null)
            {
                Destroy(_visualRoot.gameObject);
                _visualRoot = null;
            }

            if (data.Prefab != null)
            {
                GameObject instance = Instantiate(data.Prefab, transform);
                instance.name = "Visual";
                _visualRoot = instance.transform;
                _visualRoot.localRotation = Quaternion.identity;
                GroundVisual();
                return;
            }

            var root = new GameObject("Visual");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;

            CreatePrimitive("Body", PrimitiveType.Capsule, new Vector3(0f, 1.45f, 0f),
                new Vector3(0.85f, 1.15f, 0.85f), data.RobeColor);
            CreatePrimitive("Head", PrimitiveType.Sphere, new Vector3(0f, 2.60f, 0f),
                Vector3.one * 0.72f, data.SkinColor);

            if (data.HasHat)
            {
                Color hatColor = Color.Lerp(data.RobeColor, Color.black, 0.35f);
                CreatePrimitive("Hat_Rim", PrimitiveType.Cylinder, new Vector3(0f, 3.12f, 0f),
                    new Vector3(1.05f, 0.16f, 1.05f), hatColor);
                CreatePrimitive("Hat_Top", PrimitiveType.Cylinder, new Vector3(0f, 3.55f, 0f),
                    new Vector3(0.62f, 0.34f, 0.62f), hatColor);
            }

            GroundVisual();
        }

        private void CreatePrimitive(string name, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_visualRoot, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            foreach (Collider collider in go.GetComponents<Collider>())
                Destroy(collider);

            Material mat = new Material(LitShader());
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Shader LitShader()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            return urp != null ? urp : Shader.Find("Standard");
        }

        private void GroundVisual()
        {
            if (_visualRoot == null) return;
            Bounds bounds = CalculateWorldBounds(_visualRoot);
            if (bounds.size.sqrMagnitude < 0.0001f) return;

            Vector3 localMin = _visualRoot.InverseTransformPoint(bounds.min);
            Vector3 localMax = _visualRoot.InverseTransformPoint(bounds.max);
            Vector3 localCenter = (localMin + localMax) * 0.5f;
            _visualRoot.localPosition = new Vector3(-localCenter.x, -localMin.y, -localCenter.z);
        }

        private void RefreshPlayer()
        {
            _playerRefreshTimer -= Time.deltaTime;
            if (_playerRefreshTimer <= 0f)
            {
                _playerRefreshTimer = 3f;
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                _player = go != null ? go.transform : null;
            }
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