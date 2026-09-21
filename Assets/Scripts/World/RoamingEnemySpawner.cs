using System;
using System.Collections.Generic;
using UnityEngine;
using Davidmon.Creatures;

namespace Davidmon.World
{
    /// <summary>
    /// Spawns the low-level roaming enemies that populate a test arena. The spawn list
    /// is fully serializable so designers can lay out encounters in the Inspector; when
    /// left empty it falls back to a few development defaults so the arena is never bare.
    /// Enemies only surface once <see cref="CreatureRegistry"/> is initialized (bootstrap).
    /// </summary>
    public sealed class RoamingEnemySpawner : MonoBehaviour
    {
        [Serializable]
        public struct SpawnEntry
        {
            public string creatureId;
            public int level;
            public Vector3 position;
        }

        [SerializeField] private List<SpawnEntry> spawns = new List<SpawnEntry>();

        [SerializeField] private bool spawnDefaultsWhenEmpty = true;

        [SerializeField] private Vector3[] defaultPositions =
        {
            new Vector3(9f, 0.5f, 8f),
            new Vector3(-9f, 0.5f, 8f),
            new Vector3(0f, 0.5f, -9f),
            new Vector3(9f, 0.5f, -3f)
        };

        [SerializeField] private string[] defaultIds = { "emberling", "voltling", "leafhorn", "aquaphin" };
        [SerializeField] private int[] defaultLevels = { 3, 4, 5, 2 };
        [SerializeField] private Transform parent;

        private void Start()
        {
            if (!CreatureRegistry.HasCatalog)
            {
                Debug.LogWarning("[RoamingEnemySpawner] Catalog unavailable; enemies not spawned.");
                return;
            }

            if (parent == null) parent = transform;
            if (spawnDefaultsWhenEmpty && spawns.Count == 0)
                SpawnDefaults();
            else
                SpawnConfigured();
        }

        public void SpawnConfigured()
        {
            foreach (SpawnEntry entry in spawns)
            {
                if (string.IsNullOrEmpty(entry.creatureId)) continue;
                CreatureData data = CreatureRegistry.Find(entry.creatureId);
                if (data == null)
                {
                    Debug.LogWarning("[RoamingEnemySpawner] Unknown creature id: " + entry.creatureId);
                    continue;
                }
                RoamingEnemy.Spawn(data, Mathf.Max(1, entry.level), entry.position, parent);
            }
        }

        public void SpawnDefaults()
        {
            int count = Mathf.Min(defaultIds.Length, defaultLevels.Length);
            for (int i = 0; i < count; i++)
            {
                CreatureData data = CreatureRegistry.Find(defaultIds[i]);
                if (data == null)
                {
                    Debug.LogWarning("[RoamingEnemySpawner] Unknown default creature id: " + defaultIds[i]);
                    continue;
                }
                Vector3 at = i < defaultPositions.Length ? defaultPositions[i] : new Vector3(0f, 0.5f, 0f);
                RoamingEnemy.Spawn(data, Mathf.Max(1, defaultLevels[i]), at, parent);
            }
        }
    }
}