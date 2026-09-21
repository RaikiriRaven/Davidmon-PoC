using System.Collections.Generic;
using UnityEngine;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Player;
using Davidmon.World;

namespace Davidmon.Combat
{
    /// <summary>
    /// Player combat brain. Resolves the active creature's ability list, tracks the
    /// selected slot (keys 1–4) and fires it on the Attack action by aiming straight
    /// down the camera's forward vector. Cooldowns are tracked locally and exposed to
    /// the HUD. Registered in the ServiceLocator like the PlayerManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityCaster : MonoBehaviour
    {
        private const int SlotCount = 4;

        private PlayerManager _pm;
        private PlayerInputProvider _input;
        private Camera _cam;

        private readonly IReadOnlyList<AbilityData> _empty = new List<AbilityData>();
        private readonly float[] _readyAt = new float[SlotCount];
        private readonly float[] _cooldowns = new float[SlotCount];

        public int SelectedIndex { get; private set; }

        public IReadOnlyList<AbilityData> Abilities => _pm != null && _pm.HasCreature && _pm.ActiveCreature.Data != null
            ? _pm.ActiveCreature.Data.Abilities
            : _empty;

        public int AbilityCount => Abilities.Count;

        public AbilityData SelectedAbility
        {
            get
            {
                var list = Abilities;
                return SelectedIndex < list.Count ? list[SelectedIndex] : null;
            }
        }

        public float CooldownRemaining(int index) => Mathf.Max(0f, _readyAt[index] - Time.time);

        /// <summary>Fraction of the cooldown still remaining (0 = ready, 1 = just used).</summary>
        public float CooldownFraction(int index)
        {
            float total = _cooldowns[index];
            if (total <= 0f) return 0f;
            return Mathf.Clamp01(CooldownRemaining(index) / total);
        }

        private void Awake()
        {
            _pm = ServiceLocator.Get<PlayerManager>();
            _input = GetComponent<PlayerInputProvider>();
            if (_input == null) _input = GetComponentInChildren<PlayerInputProvider>();
            _cam = Camera.main;
            if (_cam == null) _cam = CombatFx.CameraEnsure();
            ServiceLocator.Register(this);
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;
        }

        private void OnEnable()
        {
            if (_input != null) _input.AbilityPressed += OnAbilityPressed;
            if (_input != null) _input.AttackPressed += OnAttackPressed;
        }

        private void OnDisable()
        {
            if (_input != null) _input.AbilityPressed -= OnAbilityPressed;
            if (_input != null) _input.AttackPressed -= OnAttackPressed;
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister<AbilityCaster>();
        }

        private void OnAbilityPressed(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= AbilityCount) return;
            SelectedIndex = slotIndex;
            GameEvents.RaiseAbilitySelected(slotIndex);
        }

        private void OnAttackPressed()
        {
            if (_pm == null || _pm.IsFainted) return;

            var list = Abilities;
            if (SelectedIndex >= list.Count) return;
            AbilityData ability = list[SelectedIndex];
            if (ability == null) return;
            if (Time.time < _readyAt[SelectedIndex]) return;

            _cooldowns[SelectedIndex] = ability.Cooldown;
            _readyAt[SelectedIndex] = Time.time + ability.Cooldown;

            Fire(ability);
        }

        private void Fire(AbilityData ability)
        {
            Vector3 origin = _cam != null ? _cam.transform.position : transform.position + Vector3.up * 1.2f;
            Vector3 dir = _cam != null ? _cam.transform.forward : transform.forward;

            switch (ability.CastType)
            {
                case CastType.Melee:
                    MeleeAttack(transform.position, ability);
                    break;
                case CastType.Cone:
                    ConeAttack(origin, dir, ability);
                    break;
                case CastType.Hitscan:
                    HitscanAttack(origin, dir, ability);
                    break;
                case CastType.Projectile:
                    ProjectileAttack(origin, dir, ability);
                    break;
            }
        }

        private void MeleeAttack(Vector3 center, AbilityData ability)
        {
            Color tint = CombatFx.ElementColor(ability.Element);
            for (int i = EnemyRegistry.All.Count - 1; i >= 0; i--)
            {
                RoamingEnemy enemy = EnemyRegistry.All[i];
                if (enemy == null || !enemy.IsAlive) continue;
                Vector3 d = enemy.transform.position - center;
                d.y = 0f;
                if (d.sqrMagnitude <= ability.Radius * ability.Radius)
                {
                    ApplyDamage(enemy, ability);
                    CombatFx.SpawnImpact(enemy.transform.position + Vector3.up * 0.6f, tint);
                }
            }
        }

        private void ConeAttack(Vector3 origin, Vector3 dir, AbilityData ability)
        {
            Color tint = CombatFx.ElementColor(ability.Element);
            for (int i = EnemyRegistry.All.Count - 1; i >= 0; i--)
            {
                RoamingEnemy enemy = EnemyRegistry.All[i];
                if (enemy == null || !enemy.IsAlive) continue;

                Vector3 toEnemy = enemy.transform.position - origin;
                float distance = toEnemy.magnitude;
                if (distance > ability.Range) continue;

                float angle = Vector3.Angle(dir, toEnemy);
                if (angle > 50f) continue;

                ApplyDamage(enemy, ability);
                CombatFx.SpawnImpact(enemy.transform.position + Vector3.up * 0.6f, tint);
            }
        }

        private void HitscanAttack(Vector3 origin, Vector3 dir, AbilityData ability)
        {
            Color tint = CombatFx.ElementColor(ability.Element);
            float range = ability.Range;
            Vector3 end = origin + dir * range;
            float bestDistance = range;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, range))
            {
                if (hit.collider != null && !hit.collider.isTrigger && hit.collider.transform != transform)
                {
                    end = hit.point;
                    bestDistance = hit.distance;
                }
            }

            RoamingEnemy hitEnemy = null;
            for (int i = EnemyRegistry.All.Count - 1; i >= 0; i--)
            {
                RoamingEnemy enemy = EnemyRegistry.All[i];
                if (enemy == null || !enemy.IsAlive) continue;
                Vector3 toEnemy = enemy.transform.position - origin;
                float squared = toEnemy.sqrMagnitude;
                if (squared > range * range) continue;

                float projected = Vector3.Dot(toEnemy, dir);
                if (projected < 0f || projected > range) continue;

                float radial = (toEnemy - dir * projected).magnitude;
                if (radial <= 0.9f)
                {
                    hitEnemy = enemy;
                    bestDistance = projected;
                    end = origin + dir * projected + Vector3.up * 0.5f;
                    break;
                }
            }

            CombatFx.SpawnTracer(origin + dir * 0.5f, end, tint);
            if (hitEnemy != null)
            {
                ApplyDamage(hitEnemy, ability);
                CombatFx.SpawnImpact(hitEnemy.transform.position + Vector3.up * 0.6f, tint);
            }
        }

        private void ProjectileAttack(Vector3 origin, Vector3 dir, AbilityData ability)
        {
            RoamingEnemy best = null;
            float bestProjected = float.MaxValue;
            float range = ability.Range;

            for (int i = EnemyRegistry.All.Count - 1; i >= 0; i--)
            {
                RoamingEnemy enemy = EnemyRegistry.All[i];
                if (enemy == null || !enemy.IsAlive) continue;

                Vector3 toEnemy = enemy.transform.position - origin;
                float projected = Vector3.Dot(toEnemy, dir);
                if (projected <= 0f || projected > range) continue;

                float radial = (toEnemy - dir * projected).magnitude;
                if (radial > ability.Radius + 0.5f) continue;

                if (projected < bestProjected)
                {
                    bestProjected = projected;
                    best = enemy;
                }
            }

            Vector3 aimDir;
            Vector3 spawn = origin + dir * 0.5f;
            if (best != null)
            {
                Vector3 target = best.transform.position + Vector3.up * 0.6f;
                aimDir = (target - spawn).normalized;
            }
            else
            {
                aimDir = dir;
            }

            AbilityProjectile.Launch(spawn, aimDir, ability.ProjectileSpeed,
                e => ResolveDamage(ability, e), ability.Radius, CombatFx.ElementColor(ability.Element));
        }

        private void ApplyDamage(RoamingEnemy enemy, AbilityData ability)
        {
            int damage = ResolveDamage(ability, enemy);
            enemy.TakeDamage(damage);
            if (damage > 0)
                CombatFx.SpawnDamageNumber(enemy.transform.position + Vector3.up * 1.2f, damage);
        }

        private int ResolveDamage(AbilityData ability, RoamingEnemy enemy)
        {
            if (_pm == null || !_pm.HasCreature) return ability.BasePower;
            CreatureInstance attacker = _pm.ActiveCreature;
            int baseDamage = Mathf.RoundToInt(ability.BasePower * (1f + attacker.Attack / 40f));
            float mitigation = Mathf.Clamp01(1f - enemy.Defense * 0.012f);
            int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * mitigation));
            if (Random.value > ability.Accuracy) damage = Mathf.Max(Mathf.RoundToInt(damage * 0.5f), 1);
            return damage;
        }
    }
}