using UnityEngine;

namespace Davidmon.Inventory
{
    /// <summary>Broad purpose of an item; kept open so milestone 8 (shops) and future consumables can plug in.</summary>
    public enum ItemType
    {
        Consumable = 0,
        EvolutionStone,
        Material,
        KeyItem,
    }

    /// <summary>
    /// Static definition of a stackable collectible: identity, description, type, the
    /// maximum amount that can sit in one stack and an optional icon. Assets live under
    /// ScriptableObjects/Items. The icon is a placeholder slot — swap in real artwork
    /// later without touching gameplay code.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemData", menuName = "Davidmon/Items/New Item", order = 3)]
    public sealed class ItemData : ScriptableObject
    {
        [SerializeField] private string itemId = "item";
        [SerializeField] private string displayName = "New Item";
        [SerializeField, TextArea] private string description = "";
        [SerializeField] private ItemType type = ItemType.Consumable;
        [SerializeField] private int maxStack = 99;
        [Tooltip("Optional icon. Placeholder-driven game: assign artwork here later.")]
        [SerializeField] private Sprite icon;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string Description => description;
        public ItemType Type => type;
        public int MaxStack => Mathf.Max(1, maxStack);
        public Sprite Icon => icon;

        public override string ToString() => displayName;
    }
}