using System;
using UnityEngine;
using Davidmon.Creatures;
using Davidmon.World;

namespace Davidmon.Combat
{
    /// <summary>
    /// A travelling spell bolt. Moves along its direction each frame, stops on solid
    /// geometry (except the player and other bolts) and detonates on the first enemy it
    /// reaches, dealing damage resolved against that exact enemy. Auto-destroys after
    /// its lifetime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityProjectile : MonoBehaviour
    {
        private Vector3 _velocity;
        private Func<IEnemyTarget, int> _resolveDamage;
        private float _radius;
        private float _age;
        private float _maxLife = 6f;
        private Transform _player;

        public static AbilityProjectile Launch(Vector3 origin, Vector3 direction, float speed,
            Func<IEnemyTarget, int> resolveDamage, float blastRadius, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "AbilityBolt";
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.18f;
            go.GetComponent<MeshRenderer>().sharedMaterial = CombatFx.TintedMaterial(tint);
            var collider = go.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            var bolt = go.AddComponent<AbilityProjectile>();
            bolt._velocity = direction.normalized * speed;
            bolt._resolveDamage = resolveDamage;
            bolt._radius = blastRadius;
            return bolt;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _maxLife)
            {
                Explode(transform.position);
                return;
            }

            Vector3 origin = transform.position;
            float step = _velocity.magnitude * Time.deltaTime;
            Vector3 dir = _velocity.normalized;

            if (_player == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                _player = go != null ? go.transform : null;
            }

            if (Physics.Raycast(origin, dir, out RaycastHit obstacle, step))
            {
                if (obstacle.collider != null && !obstacle.collider.isTrigger)
                {
                    bool self = obstacle.collider.transform.IsChildOf(transform);
                    bool playerCollider = _player != null && obstacle.collider.transform.root == _player;
                    if (!self && !playerCollider)
                    {
                        Explode(obstacle.point);
                        return;
                    }
                }
            }

            transform.position += _velocity * Time.deltaTime;

            for (int i = EnemyRegistry.All.Count - 1; i >= 0; i--)
            {
                IEnemyTarget enemy = EnemyRegistry.All[i];
                if (enemy == null) continue;
                if (!enemy.IsAlive) continue;
                if ((enemy.Position - transform.position).sqrMagnitude <= _radius * _radius)
                {
                    Explode(enemy.Position + Vector3.up * 0.6f);
                    ApplyTo(enemy);
                    return;
                }
            }
        }

        private void ApplyTo(IEnemyTarget enemy)
        {
            int damage = _resolveDamage != null ? _resolveDamage(enemy) : 1;
            // Online: server owns HP; route as a request (mirror via broadcast).
            if (Davidmon.Multiplayer.ServerApi.IsOnline && enemy is Component c && c != null)
            {
                Davidmon.Multiplayer.ServerApi.RequestEnemyDamage(c.gameObject.name, damage);
                CombatFx.SpawnDamageNumber(enemy.Position + Vector3.up * 1.2f, damage);
                return;
            }
            enemy.TakeDamage(damage);
            if (damage > 0)
                CombatFx.SpawnDamageNumber(enemy.Position + Vector3.up * 1.2f, damage);
        }

        private void Explode(Vector3 position)
        {
            CombatFx.SpawnImpact(position, CombatFx.ElementColor(ElementType.Neutral));
            Destroy(gameObject);
        }
    }
}