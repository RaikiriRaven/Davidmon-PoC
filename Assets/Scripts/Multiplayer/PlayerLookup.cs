using UnityEngine;
using Davidmon.Combat;
using Davidmon.Core;
using Davidmon.Player;
using Davidmon.World;

namespace Davidmon.Multiplayer
{
    /// <summary>
    /// Multi-player targeting helper. Online, every client sees all avatars
    /// (FishNet spawns + NetworkTransform), so "nearest player" is resolved by
    /// scanning NetworkPlayers — never FindGameObjectWithTag (arbitrary single).
    /// Offline it falls back to the scene-placed player tag.
    /// </summary>
    public static class PlayerLookup
    {
        /// <summary>Nearest avatar transform to a position (all connections).</summary>
        public static Transform NearestPlayer(Vector3 pos, float maxDist = float.PositiveInfinity)
        {
            float best = maxDist * maxDist;
            Transform bestT = null;

            // Online: scan spawned avatars (works on both host and clients).
            if (ServerApi.IsOnline)
            {
                NetworkPlayer[] players = Object.FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
                for (int i = 0; i < players.Length; i++)
                {
                    NetworkPlayer p = players[i];
                    if (p == null) continue;
                    float d = (p.transform.position - pos).sqrMagnitude;
                    if (d < best) { best = d; bestT = p.transform; }
                }
                if (bestT != null) return bestT;
            }

            // Offline (or no avatars yet): legacy single-player lookup.
            try
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) return go.transform;
            }
            catch (System.Exception) { }
            return bestT;
        }

        /// <summary>True when a collider belongs to any player avatar.</summary>
        public static bool IsPlayerCollider(Transform t)
        {
            if (t == null) return false;
            if (t.GetComponentInParent<NetworkPlayer>() != null) return true;
            Transform root = t.root;
            if (root != null && root.CompareTag("Player")) return true;
            if (t.CompareTag("Player")) return true;
            return false;
        }

        /// <summary>
        /// The locally controlled player's manager. UI/HUD must use this instead of
        /// a cached scene reference: on Host/Client the scene player is parked and a
        /// fresh avatar owns progression. Falls back to the shared registration
        /// (single-player) so offline behaviour is unchanged.
        /// </summary>
        public static PlayerManager LocalManager()
        {
            PlayerInputProvider local = PlayerInputProvider.Local;
            if (local != null)
            {
                PlayerManager pm = local.GetComponent<PlayerManager>();
                if (pm != null) return pm;
            }
            return ServiceLocator.Get<PlayerManager>();
        }

        /// <summary>
        /// The locally controlled player's combat brain (ability slots/cooldowns).
        /// Same ownership rule as <see cref="LocalManager"/>.
        /// </summary>
        public static AbilityCaster LocalCaster()
        {
            PlayerInputProvider local = PlayerInputProvider.Local;
            if (local != null)
            {
                AbilityCaster caster = local.GetComponent<AbilityCaster>();
                if (caster != null) return caster;
            }
            return ServiceLocator.Get<AbilityCaster>();
        }
    }
}
