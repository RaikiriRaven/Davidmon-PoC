using UnityEngine;

namespace Davidmon.Creatures
{
    /// <summary>
    /// Runtime access to the creature catalog. The GameWorldBootstrap loads the
    /// catalog once and registers it here so any system can resolve species by ID
    /// without holding direct references or scanning folders.
    /// </summary>
    public static class CreatureRegistry
    {
        private static CreatureCatalog _catalog;

        public static bool HasCatalog => _catalog != null;

        public static void Initialize(CreatureCatalog catalog)
        {
            _catalog = catalog;
        }

        public static CreatureCatalog Catalog => _catalog;

        public static CreatureData Find(string creatureId)
        {
            if (_catalog == null)
            {
                Debug.LogError("[CreatureRegistry] Catalog not initialized.");
                return null;
            }
            return _catalog.Find(creatureId);
        }
    }
}