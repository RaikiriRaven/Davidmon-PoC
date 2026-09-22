using System.Collections.Generic;
using UnityEngine;
using Davidmon.Core;

namespace Davidmon.Inventory
{
    /// <summary>
    /// Owns the player's item stacks (itemId to quantity). Resolves item definitions
    /// through the <see cref="ItemRegistry"/> and raises <see cref="GameEvents"/>
    /// inventory channels so UI updates without tight coupling. Registered in the
    /// ServiceLocator by whichever system first needs it, ready to be swapped for a
    /// server-backed store later.
    /// </summary>
    public sealed class PlayerInventory
    {
        private readonly Dictionary<string, int> _items = new Dictionary<string, int>();

        public IReadOnlyDictionary<string, int> Items => _items;

        /// <summary>Current quantity of an item id (0 when absent).</summary>
        public int GetCount(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            return _items.TryGetValue(itemId, out int count) ? count : 0;
        }

        /// <summary>Adds to a stack up to the item's max stack, raising change events.</summary>
        public int Add(ItemData data, int quantity)
        {
            if (data == null || quantity <= 0) return 0;

            int current = GetCount(data.ItemId);
            int added = Mathf.Clamp(quantity, 0, data.MaxStack - current);
            if (added <= 0) return 0;

            _items[data.ItemId] = current + added;
            GameEvents.RaiseItemAdded(data.ItemId, added);
            GameEvents.RaiseInventoryChanged();
            return added;
        }

        /// <summary>Removes from a stack. False when the player does not hold enough.</summary>
        public bool TryRemove(string itemId, int quantity)
        {
            if (quantity <= 0) return true;
            int current = GetCount(itemId);
            if (current < quantity) return false;

            int remaining = current - quantity;
            if (remaining <= 0) _items.Remove(itemId);
            else _items[itemId] = remaining;

            GameEvents.RaiseItemRemoved(itemId, quantity);
            GameEvents.RaiseInventoryChanged();
            return true;
        }

        /// <summary>Enumerates held stacks as (data, quantity) for UI/display purposes.</summary>
        public IEnumerable<KeyValuePair<ItemData, int>> EnumerateStacks()
        {
            foreach (KeyValuePair<string, int> pair in _items)
            {
                ItemData data = ItemRegistry.Find(pair.Key);
                if (data != null)
                    yield return new KeyValuePair<ItemData, int>(data, pair.Value);
            }
        }
    }
}