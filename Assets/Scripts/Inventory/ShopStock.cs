using System;
using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.Inventory
{
    /// <summary>A single purchasable listing inside a <see cref="ShopStock"/>.</summary>
    [Serializable]
    public sealed class ShopEntry
    {
        [SerializeField] private ItemData item;
        [SerializeField] private long price = 100;

        public ItemData Item => item;
        public long Price => Math.Max(0, price);

        public ShopEntry() { }

        public ShopEntry(ItemData item, long price)
        {
            this.item = item;
            this.price = price;
        }
    }

    /// <summary>
    /// The stock list for a shop: a set of <see cref="ShopEntry"/> listings. Assets live
    /// under ScriptableObjects/Items. Each merchant can point at its own stock; the scene's
    /// ShopUI holds the active one.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopStock", menuName = "Davidmon/Items/New Shop Stock", order = 5)]
    public sealed class ShopStock : ScriptableObject
    {
        [SerializeField] private List<ShopEntry> entries = new List<ShopEntry>();

        public IReadOnlyList<ShopEntry> Entries => entries;
    }
}