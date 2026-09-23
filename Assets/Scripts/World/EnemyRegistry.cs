using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.World
{
    /// <summary>
    /// Tracks every alive hostile target (roaming enemies and bosses) so combat and
    /// (later) targeting systems can resolve targets without FindObjectOfType loops.
    /// Targets register on enable and drop out automatically when destroyed.
    /// </summary>
    public static class EnemyRegistry
    {
        private static readonly List<IEnemyTarget> Active = new List<IEnemyTarget>();

        public static IReadOnlyList<IEnemyTarget> All => Active;

        public static int Count => Active.Count;

        public static void Register(IEnemyTarget enemy)
        {
            if (enemy != null && !Active.Contains(enemy))
                Active.Add(enemy);
        }

        public static void Unregister(IEnemyTarget enemy)
        {
            Active.Remove(enemy);
        }

        /// <summary>
        /// The nearest roaming enemy (not boss) within <paramref name="maxRange"/> of
        /// <paramref name="pos"/>. When <paramref name="requireInRange"/> is set, the
        /// enemy must currently have the player in its detection ring as well.
        /// </summary>
        public static RoamingEnemy Nearest(Vector3 pos, float maxRange, bool requireInRange = true)
        {
            RoamingEnemy best = null;
            float bestSqr = float.MaxValue;

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                RoamingEnemy enemy = Active[i] as RoamingEnemy;
                if (enemy == null)
                {
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