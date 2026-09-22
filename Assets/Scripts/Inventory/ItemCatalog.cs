using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.Inventory
{
    /// <summary>
    /// Lists every item definition in the game. The registry resolves items by ID from
    /// this list so gameplay systems never hold direct references or hard-code ids.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "Davidmon/Items/New Item Catalog", order = 3)]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] private List<ItemData> items = new List<ItemData>();

        public IReadOnlyList<ItemData> Items => items;

        public ItemData Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            foreach (ItemData item in items)
            {
                if (item != null && item.ItemId == itemId)
                    return item;
            }
            return null;
        }
    }
}