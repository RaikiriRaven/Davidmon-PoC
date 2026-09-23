using UnityEngine;
using Davidmon.Creatures;

namespace Davidmon.World
{
    /// <summary>
    /// Spawns a single boss at a designer-placed position once the creature catalog is
    /// ready. The stage ids drive the Aurity-style digi-evolution phases (wired through
    /// <see cref="BossEnemy"/>); when left empty the boss simply stays in its base form.
    /// </summary>
    public sealed class BossEnemySpawner : MonoBehaviour
    {
        [SerializeField] private string creatureId = "leafhorn";
        [SerializeField] private int level = 12;
        [Tooltip("Forms the boss digi-evolves into as its HP drops, in order.")]
        [SerializeField] private string[] evolutionStageIds = { "bramblehart", "verdantaur" };
        [SerializeField] private Vector3 position;

        private void Start()
        {
            if (!CreatureRegistry.HasCatalog)
            {
                Debug.LogWarning("[BossEnemySpawner] Catalog unavailable; boss not spawned.");
                return;
            }

            CreatureData data = CreatureRegistry.Find(creatureId);
            if (data == null)
            {
                Debug.LogWarning("[BossEnemySpawner] Unknown creature id: " + creatureId);
                return;
            }

            BossEnemy.Spawn(data, Mathf.Max(1, level), position, evolutionStageIds, transform);
        }
    }
}