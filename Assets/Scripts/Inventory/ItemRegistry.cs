using UnityEngine;

namespace Davidmon.Inventory
{
    /// <summary>
    /// Runtime access to the item catalog. Initialized once by
    /// <see cref="ItemRegistryBootstrap"/> so any system can resolve items by ID
    /// without holding direct references or scanning folders.
    /// </summary>
    public static class ItemRegistry
    {
        private static ItemCatalog _catalog;

        public static bool HasCatalog => _catalog != null;

        public static void Initialize(ItemCatalog catalog)
        {
            _catalog = catalog;
        }

        public static ItemCatalog Catalog => _catalog;

        public static ItemData Find(string itemId)
        {
            if (_catalog == null)
            {
                Debug.LogError("[ItemRegistry] Catalog not initialized.");
                return null;
            }
            return _catalog.Find(itemId);
        }
    }
}