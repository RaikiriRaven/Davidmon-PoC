namespace Davidmon.Core
{
    /// <summary>
    /// Anything the player can press E to interact with: NPCs, item pickups,
    /// doors, world objects, etc.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>The display name shown in the interaction prompt.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Optional subtitle appended to the prompt, e.g. "Talk", "Buy", "Pick up".
        /// </summary>
        string InteractionHint { get; }

        /// <summary>Whether the object can currently be interacted with.</summary>
        bool CanInteract { get; }

        /// <summary>
        /// Called by the player when they press the interact button.
        /// Gameplay effects are applied by the implementer.
        /// </summary>
        void Interact();
    }
}