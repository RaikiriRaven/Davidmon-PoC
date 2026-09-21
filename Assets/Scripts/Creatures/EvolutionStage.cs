using System;

namespace Davidmon.Creatures
{
    /// <summary>
    /// A single evolution requirement: evolve into <see cref="nextCreature"/>
    /// once the creature reaches <see cref="requiredLevel"/> and, if set, owns
    /// <see cref="requiredItemId"/>. Branching is supported by listing multiple stages.
    /// </summary>
    [Serializable]
    public class EvolutionStage
    {
        public CreatureData nextCreature;
        public int requiredLevel = 10;
        public string requiredItemId = "";
        public string requiredQuestId = "";

        public bool HasItemRequirement => !string.IsNullOrEmpty(requiredItemId);
    }
}