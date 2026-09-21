using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.World
{
    /// <summary>
    /// Tracks every alive roaming enemy so interaction and (later) combat systems can
    /// resolve targets without FindObjectOfType loops. Enemies register on enable and
    /// drop out automatically when destroyed.
    /// </summary>
    public static class EnemyRegistry
    {
        private static readonly List<RoamingEnemy> Active = new List<RoamingEnemy>();

        public static IReadOnlyList<RoamingEnemy> All => Active;

        public static int Count => Active.Count;

        public static void Register(RoamingEnemy enemy)
        {
            if (enemy != null && !Active.Contains(enemy))
                Active.Add(enemy);
        }

        public static void Unregister(RoamingEnemy enemy)
        {
            Active.Remove(enemy);
        }

        /// <summary>
        /// The nearest enemy within <paramref name="maxRange"/> of <paramref name="pos"/>.
        /// When <paramref name="requireInRange"/> is set, the enemy must currently have the
        /// player in its detection ring as well.
        /// </summary>
        public static RoamingEnemy Nearest(Vector3 pos, float maxRange, bool requireInRange = true)
        {
            RoamingEnemy best = null;
            float bestSqr = float.MaxValue;

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                RoamingEnemy enemy = Active[i];
                if (enemy == null)
                {
                    Active.RemoveAt(i);
                    continue;
                }
                if (requireInRange && !enemy.IsPlayerInRange) continue;

                float sqr = (enemy.transform.position - pos).sqrMagnitude;
                if (sqr <= maxRange * maxRange && sqr < bestSqr)
                {
                    best = enemy;
                    bestSqr = sqr;
                }
            }
            return best;
        }
    }
}