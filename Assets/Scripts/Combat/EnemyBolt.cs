using UnityEngine;
using Davidmon.Core;
using Davidmon.Player;

namespace Davidmon.Combat
{
    /// <summary>
    /// A hostile projectile fired by a <see cref="World.RoamingEnemy"/> at the player.
    /// Travels straight, detonates on solid geometry (ignoring the player's own
    /// colliders) and applies damage plus an incoming-damage number once it reaches the
    /// player. Auto-destroys after its lifetime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyBolt : MonoBehaviour
    {
        private const float MaxLife = 6f;

        private Vector3 _velocity;
        private float _radius;
        private int _damage;
        private Color _tint;
        private float _age;
        private Transform _player;
        private Transform _shooter;

        public static EnemyBolt Launch(Vector3 origin, Vector3 direction, float speed, float radius,
            int damage, Color tint, Transform shooter = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "EnemyBolt";
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.18f;
            go.GetComponent<MeshRenderer>().sharedMaterial = CombatFx.TintedMaterial(tint);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            var bolt = go.AddComponent<EnemyBolt>();
            bolt._velocity = direction.normalized * speed;
            bolt._radius = radius;
            bolt._damage = damage;
            bolt._tint = tint;
            bolt._shooter = shooter;
            return bolt;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= MaxLife)
            {
                Destroy(gameObject);
                return;
            }

            ResolvePlayer();

            Vector3 origin = transform.position;
            float step = _velocity.magnitude * Time.deltaTime;
            Vector3 dir = _velocity.normalized;

            if (Physics.Raycast(origin, dir, out RaycastHit obstacle, step))
            {
                bool valid = obstacle.collider != null
                    && !obstacle.collider.isTrigger
                    && !obstacle.collider.transform.IsChildOf(transform)
                    && !PartOf(_shooter, obstacle.collider.transform)
                    && !IsPlayerAvatar(obstacle.collider.transform);
                if (valid)
                {
                    CombatFx.SpawnImpact(obstacle.point, _tint);
                    Destroy(gameObject);
                    return;
                }
            }

            transform.position += _velocity * Time.deltaTime;

            // Multi-player: each client simulates its own bolts locally, so a bolt
            // may only ever damage the OWNED player (remote hits are visual only —
            // the remote owner's own simulation handles their damage). This keeps
            // server damage requests attributable (one client, one victim).
            if (Davidmon.Multiplayer.ServerApi.IsOnline)
            {
                var players = UnityEngine.Object.FindObjectsByType<Davidmon.Multiplayer.NetworkPlayer>(
                    FindObjectsInactive.Exclude);
                for (int i = 0; i < players.Length; i++)
                {
                    var p = players[i];
                    if (p == null || !p.IsOwner) continue;
                    Vector3 chest = p.transform.position + Vector3.up * 0.9f;
                    float reach = _radius + 0.5f;
                    if ((chest - transform.position).sqrMagnitude <= reach * reach)
                    {
                        _player = p.transform;
                        ApplyToPlayer();
                        return;
                    }
                }
            }
            else if (_player != null)
            {
                Vector3 chest = _player.position + Vector3.up * 0.9f;
                float reach = _radius + 0.5f;
                if ((chest - transform.position).sqrMagnitude <= reach * reach)
                {
                    ApplyToPlayer();
                }
            }
        }

        private void ResolvePlayer()
        {
            if (_player != null) return;
            // Multi-player: bolts track the nearest avatar, not an arbitrary one.
            _player = Davidmon.Multiplayer.PlayerLookup.NearestPlayer(transform.position);
        }

        private static bool PartOf(Transform owner, Transform t)
        {
            if (owner == null) return false;
            return t.IsChildOf(owner) || owner.IsChildOf(t);
        }

        private bool IsPlayerAvatar(Transform t)
        {
            return Davidmon.Multiplayer.PlayerLookup.IsPlayerCollider(t);
        }

        private void ApplyToPlayer()
        {
            var pm = ServiceLocator.Get<PlayerManager>();
            if (pm == null) pm = UnityEngine.Object.FindFirstObjectByType<PlayerManager>();
            if (pm != null) pm.TakeDamageToActive(_damage);
            CombatFx.SpawnIncomingDamageNumber(
                _player != null ? _player.position + Vector3.up * 1.6f : transform.position, _damage);
            CombatFx.SpawnImpact(transform.position, _tint);
            GameEvents.RaiseDamageDealt(gameObject.name, "player", _damage);
            Destroy(gameObject);
        }
    }
}