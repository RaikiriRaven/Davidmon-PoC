using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Player;

namespace Davidmon.World
{
    /// <summary>
    /// A hostile wild creature that paces its home territory. It cannot be caught; it
    /// roams until the player damages it, which aggroes it into chasing and dealing
    /// contact damage. Defeating it grants the player experience. Registers itself in
    /// the <see cref="EnemyRegistry"/> and shows a world-space HP bar overhead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoamingEnemy : MonoBehaviour, IInteractable
    {
        [SerializeField] private string creatureId;
        [SerializeField] private int level = 1;
        [SerializeField] private float wanderRadius = 4f;
        [SerializeField] private float moveSpeed = 1.4f;
        [SerializeField] private float detectionRadius = 3f;
        [SerializeField] private float repickInterval = 3.5f;
        [SerializeField] private float pauseSeconds = 1.1f;
        [SerializeField] private float leashRange = 30f;
        [SerializeField] private float contactDamageCooldown = 1f;

        private CreatureData _data;
        private CreatureInstance _instance;
        private Transform _visualRoot;

        private Vector3 _home;
        private Vector3 _target;
        private float _repickTimer;
        private float _pauseTimer;
        private bool _paused;
        private float _yaw;

        private Transform _player;
        private float _playerRefreshTimer;
        private bool _playerInRange;
        private bool _aggro;
        private bool _dead;
        private float _contactTimer;

        private Renderer[] _renderers;
        private Color[] _rendererBaseColors;
        private float _flashTimer;

        private GameObject _hpBarRoot;
        private Image _hpFill;

        public string EnemyId => gameObject.name;
        public CreatureData Data => _data;
        public CreatureInstance Instance => _instance;
        public int Level => _instance != null ? _instance.Level : level;
        public bool IsPlayerInRange => _playerInRange;
        public bool IsAlive => !_dead && _instance != null && _instance.CurrentHp > 0;
        public int Defense => _instance != null ? _instance.Defense : 0;
        public Vector3 Position => transform.position;

        // ----- IInteractable (legacy prompt support; combat is now trigger-based) -----
        public string DisplayName => _data != null ? _data.DisplayName : gameObject.name;
        public string InteractionHint => "Hostile";
        public bool CanInteract => IsAlive && _playerInRange;

        /// <summary>Spawns and initialises a roaming enemy without scene references.</summary>
        public static RoamingEnemy Spawn(CreatureData data, int spawnLevel, Vector3 at, Transform parent = null)
        {
            if (data == null) return null;

            var go = new GameObject("Enemy_" + data.CreatureId);
            RoamingEnemy enemy = go.AddComponent<RoamingEnemy>();
            if (parent != null) go.transform.SetParent(parent, true);

            enemy.Init(data, spawnLevel, at);
            return enemy;
        }

        private void Init(CreatureData data, int startLevel, Vector3 at)
        {
            _data = data;
            level = startLevel;
            _instance = new CreatureInstance(data, startLevel);
            transform.position = at;
            _home = at;
            BuildVisual();
        }

        private void Awake()
        {
            _target = transform.position;
        }

        private void OnEnable()
        {
            EnemyRegistry.Register(this);
            GameEvents.RaiseEnemySpawned(EnemyId);
        }

        private void OnDisable()
        {
            EnemyRegistry.Unregister(this);
        }

        private void BuildVisual()
        {
            if (_data == null || _data.Prefab == null) return;

            GameObject instance = Instantiate(_data.Prefab, transform);
            instance.name = "Visual";
            _visualRoot = instance.transform;
            _visualRoot.localRotation = Quaternion.identity;

            Bounds bounds = CalculateWorldBounds(_visualRoot);
            if (bounds.size.sqrMagnitude > 0.0001f)
            {
                Vector3 localMin = _visualRoot.InverseTransformPoint(bounds.min);
                Vector3 localMax = _visualRoot.InverseTransformPoint(bounds.max);
                Vector3 localCenter = (localMin + localMax) * 0.5f;
                _visualRoot.localPosition = new Vector3(-localCenter.x, -localMin.y, -localCenter.z);
            }

            _renderers = instance.GetComponentsInChildren<Renderer>();
            _rendererBaseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _rendererBaseColors[i] = _renderers[i].sharedMaterial != null ? _renderers[i].sharedMaterial.color : Color.white;

            BuildHpBar(bounds);
        }

        private void BuildHpBar(Bounds worldBounds)
        {
            var go = new GameObject("EnemyHpBar", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            Camera cam = Camera.main;
            canvas.worldCamera = cam;

            RectTransform barRt = go.GetComponent<RectTransform>();
            barRt.sizeDelta = new Vector2(1.6f, 0.18f);
            barRt.localScale = Vector3.one * 0.035f;
            float topY = worldBounds.size.sqrMagnitude > 0.0001f
                ? worldBounds.max.y - transform.position.y
                : 1.5f;
            barRt.localPosition = new Vector3(0f, topY + 0.25f, 0f);

            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.SetParent(barRt, false);
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            Image bg = bgGo.GetComponent<Image>();
            bg.sprite = CreateWhiteSprite();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.SetParent(bgRt, false);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _hpFill = fillGo.GetComponent<Image>();
            _hpFill.sprite = CreateWhiteSprite();
            _hpFill.color = new Color(0.85f, 0.25f, 0.25f, 0.95f);

            _hpBarRoot = go;
        }

        private void Update()
        {
            if (_data == null || _dead)
            {
                if (_hpBarRoot != null) _hpBarRoot.SetActive(false);
                return;
            }
            if (_hpBarRoot != null) _hpBarRoot.SetActive(true);

            RefreshPlayer();

            UpdateHpBar();

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f) RestoreBaseColors();
            }

            float distToPlayer = _player != null ? Vector3.Distance(transform.position, _player.position) : float.MaxValue;
            _playerInRange = _player != null && distToPlayer <= detectionRadius;

            // ---- combat: aggro chase + contact damage ----
            if (_aggro && _player != null)
            {
                if (distToPlayer > leashRange)
                {
                    _aggro = false;
                    _target = _home;
                }
                else if (distToPlayer > 1.25f)
                {
                    Vector3 chaseDir = _player.position - transform.position;
                    chaseDir.y = 0f;
                    Vector3 dir = chaseDir.sqrMagnitude > 0.001f ? chaseDir.normalized : Vector3.zero;

                    float step = moveSpeed * 1.75f * Time.deltaTime;
                    if (dir.sqrMagnitude > 0f && !WouldCollide(dir, step + 0.2f))
                    {
                        transform.position += dir * step;
                        transform.position = new Vector3(transform.position.x, _home.y, transform.position.z);
                        _yaw = Mathf.LerpAngle(_yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 12f * Time.deltaTime);
                        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                    }
                }
                else
                {
                    _contactTimer += Time.deltaTime;
                    if (_contactTimer >= contactDamageCooldown)
                    {
                        _contactTimer = 0f;
                        var pm = ServiceLocator.Get<PlayerManager>();
                        if (pm != null)
                        {
                            int dmg = Mathf.Max(1, 4 + _instance.Level);
                            pm.TakeDamageToActive(dmg);
                            GameEvents.RaiseDamageDealt(EnemyId, "player", dmg);
                        }
                    }
                }
                return;
            }
            _contactTimer = 0f;

            // ---- roaming ----
            if (_paused)
            {
                _pauseTimer -= Time.deltaTime;
                if (_pauseTimer <= 0f) _paused = false;
                return;
            }

            Vector3 homeDelta = _home - transform.position;
            if (homeDelta.sqrMagnitude > wanderRadius * wanderRadius)
            {
                _target = _home;
                _repickTimer = 0f;
            }

            Vector3 moveDir = _target - transform.position;
            moveDir.y = 0f;
            Vector3 desiredDir = moveDir.sqrMagnitude > 0.001f ? moveDir.normalized : Vector3.zero;

            // A player standing right next to a wild creature counts as an obstacle to walk around.
            if (desiredDir.sqrMagnitude > 0f && distToPlayer < 1f)
            {
                Vector3 away = transform.position - _player.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.01f) desiredDir = away.normalized;
                else desiredDir = Vector3.zero;
            }

            if (desiredDir.sqrMagnitude > 0f)
            {
                float step = moveSpeed * Time.deltaTime;
                if (WouldCollide(desiredDir, step + 0.15f))
                {
                    PickNewTarget();
                }
                else
                {
                    transform.position += desiredDir * step;
                    transform.position = new Vector3(transform.position.x, _home.y, transform.position.z);
                }

                float targetYaw = Mathf.Atan2(desiredDir.x, desiredDir.z) * Mathf.Rad2Deg;
                _yaw = Mathf.LerpAngle(_yaw, targetYaw, 10f * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            _repickTimer -= Time.deltaTime;
            if (_repickTimer <= 0f || (moveDir.sqrMagnitude <= 0.05f && !_paused))
            {
                _paused = true;
                _pauseTimer = pauseSeconds;
                _repickTimer = repickInterval;
                PickNewTarget();
            }
        }

        /// <summary>Applies damage to this enemy. Flash, aggro, and defeat handling.</summary>
        public void TakeDamage(int rawAmount)
        {
            if (_dead || _instance == null || !IsAlive) return;

            _instance.TakeDamage(rawAmount);
            GameEvents.RaiseDamageDealt("player", EnemyId, rawAmount);
            _aggro = true;
            FlashHit();

            if (_instance.CurrentHp <= 0)
                Die();
        }

        private void Die()
        {
            _dead = true;
            _aggro = false;
            if (_hpBarRoot != null) _hpBarRoot.SetActive(false);
            if (_visualRoot != null) _visualRoot.gameObject.SetActive(false);

            GameEvents.RaiseTargetDied(EnemyId);
            GameEvents.RaiseEnemyDefeated(EnemyId);

            int exp = 15 + _instance.Level * 10;
            GameEvents.RaiseShowNotification(
                _data.DisplayName + " (Lv." + _instance.Level + ") defeated! +" + exp + " Exp");

            var pm = ServiceLocator.Get<PlayerManager>();
            if (pm != null) pm.AddExperienceToActive(exp);

            StartCoroutine(RespawnAfter(12f));
        }

        private IEnumerator RespawnAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (this == null) yield break;

            _instance = new CreatureInstance(_data, level);
            transform.position = _home;
            _yaw = 0f;
            _aggro = false;
            _dead = false;
            _contactTimer = 0f;
            if (_visualRoot != null) _visualRoot.gameObject.SetActive(true);
            GameEvents.RaiseEnemySpawned(EnemyId);
        }

        public void Interact()
        {
            if (!CanInteract) return;
            _aggro = true;
            GameEvents.RaiseEnemyEngaged(EnemyId);
        }

        private void FlashHit()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Renderer r = _renderers[i];
                Color flash = Color.Lerp(_rendererBaseColors[i], Color.white, 0.85f);
                Material m = r.material;
                if (m.HasProperty("_Color")) m.color = flash;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", flash);
            }
            _flashTimer = 0.12f;
        }

        private void RestoreBaseColors()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                if (_renderers[i].sharedMaterial == null) continue;
                Color baseColor = _rendererBaseColors[i];
                Material m = _renderers[i].material;
                if (m.HasProperty("_Color")) m.color = baseColor;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            }
        }

        private void UpdateHpBar()
        {
            if (_hpBarRoot == null || _hpFill == null || _instance == null) return;
            float frac = _instance.MaxHp > 0 ? (float)_instance.CurrentHp / _instance.MaxHp : 0f;
            RectTransform rt = _hpFill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Camera cam = Camera.main;
            _hpBarRoot.transform.localScale = Vector3.one * (0.035f * (1f + Mathf.Max(0f, _instance.Level - 1) * 0.03f));
        }

        private void PickNewTarget()
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            _target = _home + new Vector3(offset.x, 0f, offset.y);
        }

        private bool WouldCollide(Vector3 dir, float distance)
        {
            Vector3 origin = transform.position + Vector3.up * 0.35f;
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, 0.3f, dir, distance))
            {
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (_player != null && hit.collider.transform.root == _player) continue;
                return true;
            }
            return false;
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

        private static Sprite CreateWhiteSprite()
        {
            return Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
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