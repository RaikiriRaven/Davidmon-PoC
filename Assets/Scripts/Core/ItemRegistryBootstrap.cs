using UnityEngine;
using Davidmon.Inventory;

namespace Davidmon.Core
{
    /// <summary>
    /// Loads the item catalog into the <see cref="ItemRegistry"/> once at scene start
    /// so every system can resolve items by ID. Mirrors the creature registry bootstrap;
    /// an editor-only fallback locates the asset when the inspector reference was not
    /// wired to keep play-in-Editor frictionless.
    /// </summary>
    public sealed class ItemRegistryBootstrap : MonoBehaviour
    {
        [SerializeField] private ItemCatalog catalog;

        private void Awake()
        {
            if (catalog == null && !ItemRegistry.HasCatalog)
                catalog = LoadFromEditorAsset();

            if (catalog != null)
                ItemRegistry.Initialize(catalog);

            if (ItemRegistry.HasCatalog)
                Debug.Log("[ItemRegistry] Initialized with " + ItemRegistry.Catalog.Items.Count + " items.");
            else
                Debug.LogWarning("[ItemRegistry] No item catalog available.");
        }

        private static ItemCatalog LoadFromEditorAsset()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/ScriptableObjects/Items/ItemCatalog.asset");
#else
            return null;
#endif
        }
    }
}