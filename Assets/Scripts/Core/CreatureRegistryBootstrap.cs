using UnityEngine;
using Davidmon.Creatures;

namespace Davidmon.Core
{
    /// <summary>
    /// Loads the creature catalog into the <see cref="CreatureRegistry"/> once at
    /// scene start so every system can resolve species by ID. The catalog reference
    /// is serialized in the scene; an editor-only fallback also locates the asset
    /// when the inspector reference was not wired, to keep play-in-Editor frictionless.
    /// </summary>
    public sealed class CreatureRegistryBootstrap : MonoBehaviour
    {
        [SerializeField] private CreatureCatalog catalog;

        private void Awake()
        {
            if (catalog == null && !CreatureRegistry.HasCatalog)
                catalog = LoadFromEditorAsset();

            if (catalog != null)
                CreatureRegistry.Initialize(catalog);

            if (CreatureRegistry.HasCatalog)
                Debug.Log("[CreatureRegistry] Initialized with " + CreatureRegistry.Catalog.AllCreatures.Count + " species.");
            else
                Debug.LogWarning("[CreatureRegistry] No catalog available. Systems that resolve species by ID will fail.");
        }

#if UNITY_EDITOR
        private static CreatureCatalog LoadFromEditorAsset()
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureCatalog>("Assets/ScriptableObjects/Creatures/CreatureCatalog.asset");
        }
#else
        private static CreatureCatalog LoadFromEditorAsset()
        {
            return null;
        }
#endif
    }
}