using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.Creatures
{
    /// <summary>
    /// Lists every creature definition in the game. The prototype decides what a
    /// player can select from <see cref="Starters"/>; the full list is used by the
    /// registry to resolve creatures by ID (for spawning, evolution, saves, etc.).
    /// </summary>
    [CreateAssetMenu(fileName = "CreatureCatalog", menuName = "Davidmon/Creatures/New Creature Catalog", order = 2)]
    public sealed class CreatureCatalog : ScriptableObject
    {
        [SerializeField] private List<CreatureData> starters = new List<CreatureData>();
        [SerializeField] private List<CreatureData> allCreatures = new List<CreatureData>();

        public IReadOnlyList<CreatureData> Starters => starters;
        public IReadOnlyList<CreatureData> AllCreatures => allCreatures;

        public CreatureData Find(string creatureId)
        {
            if (string.IsNullOrEmpty(creatureId)) return null;
            foreach (var creature in allCreatures)
            {
                if (creature != null && creature.CreatureId == creatureId)
                    return creature;
            }
            foreach (var creature in starters)
            {
                if (creature != null && creature.CreatureId == creatureId)
                    return creature;
            }
            return null;
        }
    }
}