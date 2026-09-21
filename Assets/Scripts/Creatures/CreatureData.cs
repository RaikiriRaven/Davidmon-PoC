using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.Creatures
{
    /// <summary>
    /// Static definition of a creature species: identity, base stats, element,
    /// evolution stages, abilities and the world model prefab used as its avatar.
    /// All data assets live under ScriptableObjects/Creatures.
    /// </summary>
    [CreateAssetMenu(fileName = "Creature", menuName = "Davidmon/Creatures/New Creature", order = 1)]
    public sealed class CreatureData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string creatureId = "creature";
        [SerializeField] private string displayName = "New Creature";
        [SerializeField][TextArea] private string description = "";
        [SerializeField] private Sprite icon;
        [SerializeField] private ElementType element = ElementType.Neutral;

        [Header("Stats")]
        [SerializeField] private StatBlock baseStats = new StatBlock(100, 20, 15, 10);
        [SerializeField] private StatBlock statGrowth = new StatBlock(6, 2, 2, 1);

        [Header("World")]
        [Tooltip("Recruitable at the starter selection screen.")]
        [SerializeField] private bool isStarter;
        [SerializeField] private float walkSpeedMultiplier = 1f;
        [SerializeField] private GameObject prefab;

        [Header("Combat")]
        [SerializeField] private List<AbilityData> abilities = new List<AbilityData>();

        [Header("Evolution")]
        [SerializeField] private List<EvolutionStage> evolutionStages = new List<EvolutionStage>();

        public string CreatureId => creatureId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public ElementType Element => element;
        public StatBlock BaseStats => baseStats;
        public StatBlock StatGrowth => statGrowth;
        public bool IsStarter => isStarter;
        public float WalkSpeedMultiplier => walkSpeedMultiplier;
        public GameObject Prefab => prefab;
        public IReadOnlyList<AbilityData> Abilities => abilities;
        public IReadOnlyList<EvolutionStage> EvolutionStages => evolutionStages;

        public override string ToString() => displayName;
    }
}