using UnityEngine;

namespace Davidmon.Creatures
{
    /// <summary>How an ability resolves when cast during combat.</summary>
    public enum CastType
    {
        Projectile,   // a travelling bolt that explodes on contact
        Hitscan,      // an instant ray along the aim direction
        Cone,         // a fan of effect covering a forward arc
        Melee         // a close-range hit around the caster
    }

    /// <summary>
    /// Static definition of a creature ability. Abilities are referenced by
    /// <see cref="CreatureData"/> and become usable in combat. The CastType plus
    /// range/radius describe how the projectile or area is resolved at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "Ability", menuName = "Davidmon/Abilities/New Ability", order = 0)]
    public sealed class AbilityData : ScriptableObject
    {
        [SerializeField] private string abilityId = "ability";
        [SerializeField] private string displayName = "Tackle";
        [SerializeField][TextArea] private string description = "";
        [SerializeField] private ElementType element = ElementType.Neutral;
        [SerializeField] private int basePower = 20;
        [SerializeField][Range(0f, 1f)] private float accuracy = 1f;
        [SerializeField] private float cooldown = 1.5f;
        [SerializeField] private float range = 3f;
        [SerializeField] private CastType castType = CastType.Melee;
        [SerializeField] private float radius = 1.5f;
        [SerializeField] private float projectileSpeed = 14f;
        [SerializeField] private bool isBasicAttack = true;

        public string AbilityId => abilityId;
        public string DisplayName => displayName;
        public string Description => description;
        public ElementType Element => element;
        public int BasePower => basePower;
        public float Accuracy => accuracy;
        public float Cooldown => cooldown;
        public float Range => range;
        public CastType CastType => castType;
        public float Radius => radius;
        public float ProjectileSpeed => projectileSpeed;
        public bool IsBasicAttack => isBasicAttack;

        public override string ToString() => displayName;
    }
}