using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.NPC
{
    /// <summary>
    /// Tracks every placed NPC so the interaction hub can resolve nearby talk targets
    /// without FindObjectOfType loops. NPCs register on enable and drop out when
    /// destroyed, mirroring Davidmon.World.EnemyRegistry.
    /// </summary>
    public static class NPCRegistry
    {
        private static readonly List<Npc> Active = new List<Npc>();

        public static IReadOnlyList<Npc> All => Active;

        public static int Count => Active.Count;

        public static void Register(Npc npc)
        {
            if (npc != null && !Active.Contains(npc))
                Active.Add(npc);
        }

        public static void Unregister(Npc npc)
        {
            Active.Remove(npc);
        }

        /// <summary>
        /// The nearest NPC within <paramref name="maxRange"/> of <paramref name="pos"/>.
        /// When <paramref name="requireInRange"/> is set the NPC must currently have the
        /// player inside its interaction ring as well.
        /// </summary>
        public static Npc Nearest(Vector3 pos, float maxRange, bool requireInRange = true)
        {
            Npc best = null;
            float bestSqr = float.MaxValue;

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Npc npc = Active[i];
                if (npc == null)
                {
                    Active.RemoveAt(i);
                    continue;
                }
                if (requireInRange && !npc.IsPlayerInRange) continue;

                float sqr = (npc.transform.position - pos).sqrMagnitude;
                if (sqr <= maxRange * maxRange && sqr < bestSqr)
                {
                    best = npc;
                    bestSqr = sqr;
                }
            }
            return best;
        }
    }
}