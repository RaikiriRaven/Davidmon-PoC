using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Combat;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Inventory;
using Davidmon.Player;
using Davidmon.UI;

namespace Davidmon.World
{
    /// <summary>
    /// An oversized arena boss styled after world/raid bosses: huge HP, telegraphed
    /// multi-hit attacks, EXP granted per hit landed, and a multi-phase fight where the
    /// boss "digi-evolves" into stronger forms at HP thresholds (Aurity-style). Defeating
    /// it drops a boss egg plus exp/coins and it respawns on a timer so it can be farmed.
    /// Shares the <see cref="IEnemyTarget"/> damage path with roaming enemies.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossEnemy : MonoBehaviour, IEnemyTarget
    {
        [SerializeField] private string baseCreatureId = "leafhorn";
        [SerializeField] private int level = 12;
        [Tooltip("Forms the boss digi-evolves into, in order, as its HP drops.")]
        [SerializeField] private string[] evolutionStageIds = { "bramblehart", "verdantaur" };
        [SerializeField] private float bossScale = 1.7f;
        [SerializeField] private float respawnSeconds = 60f;

        [Header("Roam")]
        [SerializeField] private float wanderRadius = 3f;
        [SerializeField] private float moveSpeed = 2.2f;
        [SerializeField] private float repickInterval = 3.5f;
        [SerializeField] private float pauseSeconds = 1.1f;

        [Header("Combat")]
        [SerializeField] private float detectionRadius = 9f;
        [SerializeField] private float preferredRange = 8f;
        [SerializeField] private float leashRange = 60f;

        [Header("Bolt Fan")]
        [SerializeField] private float shotRange = 24f;
        [SerializeField] private float shootCooldown = 3.2f;
        [SerializeField] private float telegraphDuration = 0.6f;
        [SerializeField] private float boltSpreadDegrees = 22f;

        [Header("Ground Slam (stage 2+)")]
        [SerializeField] private float slamCooldown = 9f;
        [SerializeField] private float slamTelegraph = 0.9f;
        [SerializeField] private float slamRadius = 7f;
        [SerializeField] private float slamImpactDelay = 0.55f;

        private CreatureData _data;
        private CreatureInstance _instance;
        private Transform _visualRoot;
        private Renderer[] _renderers;
        private Color[] _rendererBaseColors;
        private float _flashTimer;

        private GameObject _hpBarRoot;
        private Image _hpFill;
        private Text _nameLabel;

        private Vector3 _home;
        private Vector3 _target;
        private float _repickTimer;
        private float _pauseTimer;
        private bool _paused;
        private float _yaw;

        private Transform _player;
        private float _playerRefreshTimer;
        private bool _aggro;
        private bool _dead;
        private bool _transforming;

        private float _shotCooldownTimer;
        private float _slamCooldownTimer;
        private float _telegraphTimer;
        private bool _telegraphing;
        private bool _slamTelegraphing;

        private int _stageIndex;
        private float _damageMult = 1f;
        private readonly float[] _thresholds = new float[4];

        public string EnemyId => gameObject.name;
        public CreatureData Data => _data;
        public CreatureInstance Instance => _instance;
        public int Level => _instance != null ? _instance.Level : level;
        public bool IsAlive => !_dead && _instance != null && _instance.CurrentHp > 0;
        public bool IsAggro => _aggro;
        public int Defense => _instance != null ? _instance.Defense : 0;
        public Vector3 Position => transform.position;
        public string DisplayName => _data != null ? "Alpha " + _data.DisplayName : gameObject.name;

        /// <summary>Spawns and initialises a boss without scene references.</summary>
        public static BossEnemy Spawn(CreatureData baseData, int bossLevel, Vector3 at,
            string[] stageIds, Transform parent = null)
        {
            if (baseData == null) return null;

            var go = new GameObject("Boss_" + baseData.CreatureId);
            BossEnemy boss = go.AddComponent<BossEnemy>();
            if (parent != null) go.transform.SetParent(parent, true);
            boss.Init(baseData, bossLevel, at, stageIds);
            return boss;
        }

        private void Init(CreatureData baseData, int bossLevel, Vector3 at, string[] stageIds)
        {
            _data = baseData;
            level = Mathf.Max(1, bossLevel);
            transform.position = at;
            _home = at;
            _instance = new CreatureInstance(_data, level);
            if (stageIds != null) evolutionStageIds = stageIds;
            ComputeThresholds();
            BuildVisual();
        }

        private void ComputeThresholds()
        {
            int stages = evolutionStageIds != null ? evolutionStageIds.Length : 0;
            for (int k = 0; k < _thresholds.Length; k++)
            {
                int stage = k + 1;
                _thresholds[k] = stage <= stages
                    ? (float)(stages + 1 - stage) / (stages + 1)
                    : 0f;
            }
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
            _visualRoot.localScale = Vector3.one * bossScale;

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

            if (_hpBarRoot == null) BuildHpBar(bounds);
            else RebuildHpBarPosition(bounds);
        }

        private void BuildHpBar(Bounds worldBounds)
        {
            var go = new GameObject("BossHpBar", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            Camera cam = Camera.main;
            canvas.worldCamera = cam;

            RectTransform barRt = go.GetComponent<RectTransform>();
            barRt.sizeDelta = new Vector2(3.2f, 0.22f);
            barRt.localScale = Vector3.one * 0.05f;
            float topY = worldBounds.size.sqrMagnitude > 0.0001f
                ? worldBounds.max.y - transform.position.y
                : 1.5f;
            barRt.localPosition = new Vector3(0f, topY + 0.45f, 0f);

            _nameLabel = CreateBarText(go.transform, barRt, 0f, 22f, 15, "Name");

            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.SetParent(barRt, false);
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            Image bg = bgGo.GetComponent<Image>();
            bg.sprite = CreateWhiteSprite();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.9f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.SetParent(bgRt, false);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _hpFill = fillGo.GetComponent<Image>();
            _hpFill.sprite = CreateWhiteSprite();
            _hpFill.color = new Color(0.95f, 0.78f, 0.25f, 0.95f);

            _hpBarRoot = go;
        }

        private Text CreateBarText(Transform hpBarRoot, RectTransform barRt, float y, float height, int size, string name)
        {
            var textGo = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.SetParent(hpBarRoot, false);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(3.6f, height);
            Text label = textGo.GetComponent<Text>();
            label.font = UIFactory.Font();
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = size;
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(1f, 0.9f, 0.55f, 1f);
            label.text = DisplayName + "  Lv." + Level;
            label.raycastTarget = false;
            return label;
        }

        private void RebuildHpBarPosition(Bounds worldBounds)
        {
            if (_hpBarRoot == null) return;
            float topY = worldBounds.size.sqrMagnitude > 0.0001f
                ? worldBounds.max.y - transform.position.y
                : 1.5f;
            RectTransform barRt = _hpBarRoot.GetComponent<RectTransform>();
            barRt.localPosition = new Vector3(0f, topY + 0.45f, 0f);
        }

        private void Update()
        {
            if (_data == null || _dead)
            {
                if (_hpBarRoot != null) _hpBarRoot.SetActive(false);
                return;
            }
            if (_hpBarRoot != null) _hpBarRoot.SetActive(true);
            if (_nameLabel != null) _nameLabel.text = DisplayName + "  Lv." + Level;

            RefreshPlayer();

            UpdateHpBar();

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f) RestoreBaseColors();
            }

            float distToPlayer = _player != null ? Vector3.Distance(transform.position, _player.position) : float.MaxValue;

            if (_transforming)
            {
                FacePlayer();
                return;
            }

            if (_aggro && _player != null)
            {
                if (distToPlayer > leashRange)
                {
                    _aggro = false;
                    _telegraphing = false;
                    _slamTelegraphing = false;
                    _target = _home;
                }
                else if (_slamTelegraphing)
                {
                    FacePlayer();
                    PulseWindUp();
                    _telegraphTimer -= Time.deltaTime;
                    if (_telegraphTimer <= 0f)
                    {
                        _slamTelegraphing = false;
                        StartCoroutine(SlamAt(_player.position));
                    }
                }
                else if (_telegraphing)
                {
                    FacePlayer();
                    PulseWindUp();
                    _telegraphTimer -= Time.deltaTime;
                    if (_telegraphTimer <= 0f)
                    {
                        _telegraphing = false;
                        FireBoltFan();
                    }
                }
                else
                {
                    _shotCooldownTimer -= Time.deltaTime;
                    _slamCooldownTimer -= Time.deltaTime;

                    if (distToPlayer <= shotRange && _shotCooldownTimer <= 0f)
                    {
                        _shotCooldownTimer = shootCooldown * AttackSpeedMultiplier();
                        _telegraphing = true;
                        _telegraphTimer = telegraphDuration;
                    }
                    else if (distToPlayer <= shotRange && _stageIndex >= 1 && _slamCooldownTimer <= 0f)
                    {
                        _slamCooldownTimer = slamCooldown * AttackSpeedMultiplier();
                        _slamTelegraphing = true;
                        _telegraphTimer = slamTelegraph;
                    }
                    else if (distToPlayer > preferredRange + 0.5f)
                    {
                        AdvanceChase(distToPlayer);
                    }
                    else if (distToPlayer < preferredRange - 0.5f)
                    {
                        Retreat(distToPlayer);
                    }
                }
                return;
            }

            _telegraphing = false;
            _slamTelegraphing = false;

            // Roaming bosses notice the player at detection range and engage.
            if (_aggro == false && _player != null && distToPlayer <= detectionRadius)
            {
                _aggro = true;
                GameEvents.RaiseEnemyEngaged(EnemyId);
            }

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

        private float AttackSpeedMultiplier()
        {
            return Mathf.Max(0.45f, 1f - _stageIndex * 0.2f);
        }

        private void AdvanceChase(float distToPlayer)
        {
            Vector3 chaseDir = _player.position - transform.position;
            chaseDir.y = 0f;
            Vector3 dir = chaseDir.sqrMagnitude > 0.001f ? chaseDir.normalized : Vector3.zero;
            float step = moveSpeed * 1.35f * Time.deltaTime;
            if (dir.sqrMagnitude > 0f && !WouldCollide(dir, step + 0.2f))
            {
                transform.position += dir * step;
                transform.position = new Vector3(transform.position.x, _home.y, transform.position.z);
                _yaw = Mathf.LerpAngle(_yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 12f * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
        }

        private void Retreat(float distToPlayer)
        {
            Vector3 away = transform.position - _player.position;
            away.y = 0f;
            Vector3 dir = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.zero;
            float step = moveSpeed * 0.8f * Time.deltaTime;
            if (dir.sqrMagnitude > 0f && !WouldCollide(dir, step + 0.2f))
            {
                transform.position += dir * step;
                transform.position = new Vector3(transform.position.x, _home.y, transform.position.z);
            }
        }

        /// <summary>
        /// Bosses are farmed like raids: every landed hit grants the active creature a
        /// small slice of EXP, plus the boss flashes and (at thresholds) digi-evolves.
        /// </summary>
        public void TakeDamage(int rawAmount)
        {
            if (_dead || _instance == null || !IsAlive || _transforming) return;

            _instance.TakeDamage(rawAmount);
            GameEvents.RaiseDamageDealt("player", EnemyId, rawAmount);
            _aggro = true;
            FlashHit();

            int hitExp = 4;
            var pm = ServiceLocator.Get<PlayerManager>();
            if (pm == null) pm = FindFirstObjectByType<PlayerManager>();
            if (pm != null) pm.AddExperienceToActive(hitExp);

            if (_instance.CurrentHp <= 0)
            {
                Die();
                return;
            }

            int nextStage = _stageIndex + 1;
            if (nextStage < _thresholds.Length && _thresholds[nextStage - 1] > 0f)
            {
                float frac = _instance.MaxHp > 0 ? (float)_instance.CurrentHp / _instance.MaxHp : 0f;
                if (frac <= _thresholds[nextStage - 1])
                    StartCoroutine(DigivolveNextStage(nextStage));
            }
        }

        private IEnumerator DigivolveNextStage(int targetStage)
        {
            if (_transforming || _dead || evolutionStageIds == null || _instance == null) yield break;
            if (targetStage - 1 >= evolutionStageIds.Length) yield break;

            _transforming = true;
            RestoreBaseColors();

            CombatFx.SpawnImpact(transform.position + Vector3.up * 0.6f, CombatFx.ElementColor(_data != null ? _data.Element : ElementType.Neutral));

            CreatureData next = CreatureRegistry.Find(evolutionStageIds[targetStage - 1]);
            if (next != null)
            {
                _data = next;
                _stageIndex = targetStage;
                _damageMult = 1f + _stageIndex * 0.35f;

                if (_visualRoot != null) Destroy(_visualRoot.gameObject);
                BuildVisual();

                _instance.Evolve(next);
                _instance.Heal(Mathf.RoundToInt(_instance.MaxHp * 0.12f));

                GameEvents.RaiseShowLegendaryNotification(
                    "✦ " + "Alpha " + next.DisplayName + " has digi-evolved! It becomes enraged.");
            }

            yield return new WaitForSecondsRealtime(0.9f);
            _transforming = false;
        }

        private IEnumerator SlamAt(Vector3 center)
        {
            RestoreBaseColors();
            SpawnSlamRing(center);

            float elapsed = 0f;
            int damage = Mathf.Max(1, Mathf.RoundToInt((6 + _instance.Level * 2) * 1.4f * _damageMult));
            var pm = ServiceLocator.Get<PlayerManager>();
            if (pm == null) pm = FindFirstObjectByType<PlayerManager>();

            while (elapsed < slamImpactDelay)
            {
                elapsed += Time.deltaTime;
                if (pm != null && pm.HasCreature && pm.ActiveCreature != null)
                {
                    Vector3 to = pm.transform.position - center;
                    to.y = 0f;
                    if (to.sqrMagnitude <= slamRadius * slamRadius)
                    {
                        pm.TakeDamageToActive(damage);
                        CombatFx.SpawnIncomingDamageNumber(pm.transform.position + Vector3.up * 1.2f, damage);
                        break;
                    }
                }
                yield return null;
            }
        }

        private void SpawnSlamRing(Vector3 center)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SlamRing";
            ring.transform.position = center;
            ring.transform.localScale = new Vector3(0.5f, 0.05f, 0.5f);
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var collider = ring.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFx.TintedMaterial(CombatFx.ElementColor(_data != null ? _data.Element : ElementType.Neutral));
            ring.AddComponent<SlamRingEffect>();
        }

        private void FireBoltFan()
        {
            RestoreBaseColors();
            if (_player == null || _instance == null || !IsAlive) return;

            Vector3 origin = transform.position + Vector3.up * 1.6f;
            int boltCount = 1 + _stageIndex * 2; // 1, 3, 5
            int damage = Mathf.Max(1, Mathf.RoundToInt((6 + _instance.Level * 2) * _damageMult));
            Color tint = CombatFx.ElementColor(_data != null ? _data.Element : ElementType.Neutral);

            Vector3 baseDir = (_player.position + Vector3.up * 0.9f - origin).normalized;
            if (boltCount == 1)
            {
                EnemyBolt.Launch(origin, baseDir, 13f, 0.26f, damage, tint, transform);
                return;
            }

            float gapF = boltCount - 1;
            for (int i = 0; i < boltCount; i++)
            {
                float t = -1f + 2f * (i / gapF); // -1 .. 1
                Quaternion rot = Quaternion.AngleAxis(t * boltSpreadDegrees, Vector3.up);
                EnemyBolt.Launch(origin, rot * baseDir, 13f, 0.26f, damage, tint, transform);
            }
        }

        private void Die()
        {
            _dead = true;
            _aggro = false;
            _telegraphing = false;
            _slamTelegraphing = false;
            _transforming = false;
            RestoreBaseColors();
            if (_hpBarRoot != null) _hpBarRoot.SetActive(false);
            if (_visualRoot != null) _visualRoot.gameObject.SetActive(false);

            GameEvents.RaiseTargetDied(EnemyId);
            GameEvents.RaiseEnemyDefeated(EnemyId);
            GameEvents.RaiseBossDefeated();

            int exp = 150 + _instance.Level * 25;
            long coins = 400 + _instance.Level * 50L;

            var pm = ServiceLocator.Get<PlayerManager>();
            if (pm == null) pm = FindFirstObjectByType<PlayerManager>();
            if (pm != null) pm.AddExperienceToActive(exp);

            var wallet = ServiceLocator.GetOrCreate(() => new Wallet());
            wallet.AddCoins(coins);

            GameEvents.RaiseShowLegendaryNotification(
                "★ " + DisplayName + " defeated! +" + exp + " Exp, +" + coins + " coins");

            GrantBossRewards();

            StartCoroutine(RespawnAfter(respawnSeconds));
        }

        private void GrantBossRewards()
        {
            ItemData egg = ItemRegistry.Find("leaf_boss_egg");
            if (egg == null) return;
            var inventory = ServiceLocator.GetOrCreate(() => new PlayerInventory());
            int added = inventory.Add(egg, 1);
            if (added > 0)
                GameEvents.RaiseShowNotification("Boss drop: " + egg.DisplayName);
        }

        private IEnumerator RespawnAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (this == null) yield break;

            _data = CreatureRegistry.Find(baseCreatureId);
            if (_data == null) yield break;

            _stageIndex = 0;
            _damageMult = 1f;
            _instance = new CreatureInstance(_data, level);
            if (_visualRoot != null) Destroy(_visualRoot.gameObject);
            BuildVisual();
            transform.position = _home;
            _yaw = 0f;
            _aggro = false;
            _dead = false;
            GameEvents.RaiseEnemySpawned(EnemyId);
        }

        private void FacePlayer()
        {
            if (_player == null) return;
            Vector3 to = _player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
            {
                _yaw = Mathf.LerpAngle(_yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 16f * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
        }

        private void PulseWindUp()
        {
            if (_renderers == null || _renderers.Length == 0) return;
            Color bright = CombatFx.ElementColor(_data != null ? _data.Element : ElementType.Neutral);
            float pulse = 0.5f + 0.35f * Mathf.Sin(Time.time * 28f);
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Material m = _renderers[i].material;
                Color c = Color.Lerp(_rendererBaseColors[i], bright, pulse);
                if (m.HasProperty("_Color")) m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            }
        }

        private void FlashHit()
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Renderer r = _renderers[i];
                Material m = r.material;
                Color flash = Color.Lerp(_rendererBaseColors[i], Color.white, 0.85f);
                if (m.HasProperty("_Color")) m.color = flash;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", flash);
            }
            _flashTimer = 0.12f;
        }

        private void RestoreBaseColors()
        {
            if (_renderers == null) return;
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
        }

        private void PickNewTarget()
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            _target = _home + new Vector3(offset.x, 0f, offset.y);
        }

        private bool WouldCollide(Vector3 dir, float distance)
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, 0.5f, dir, distance))
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
                _playerRefreshTimer = 2.5f;
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

        /// <summary>Expanding, fading ground ring for slam telegraph/impact visual.</summary>
        private sealed class SlamRingEffect : MonoBehaviour
        {
            private float _age;

            private void Update()
            {
                _age += Time.deltaTime;
                float t = _age / 0.6f;
                if (t >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
                float scale = Mathf.Lerp(0.5f, 8f, t);
                transform.localScale = new Vector3(scale, 0.05f, scale);
                Renderer r = GetComponent<Renderer>();
                if (r != null)
                {
                    Material m = r.material; // instanced: safe to fade without corrupting the shared tint cache
                    m.color = Color.Lerp(m.color, Color.white, t) * Mathf.Lerp(1f, 0f, t);
                }
            }
        }
    }
}