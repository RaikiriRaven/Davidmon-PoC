using System;
using UnityEngine;

namespace Davidmon.NPC
{
    /// <summary>
    /// What a dialogue choice does once selected. Kept open so future milestones
    /// (shops, quests, gifts) can plug in without changing the dialogue UI.
    /// </summary>
    public enum DialogueAction
    {
        None = 0,
        Close,
        Heal,
        Shop,
        Quest,
    }

    /// <summary>One spoken line inside an NPC conversation.</summary>
    [Serializable]
    public struct DialogueLine
    {
        [SerializeField] private string speaker;
        [SerializeField, TextArea(1, 3)] private string text;

        public string Speaker => speaker;
        public string Text => text;

        public DialogueLine(string text, string speaker = "") { this.text = text; this.speaker = speaker; }
    }

    /// <summary>An option the player can pick once the intro lines are read.</summary>
    [Serializable]
    public struct DialogueChoice
    {
        [SerializeField] private string label;
        [SerializeField] private DialogueAction action;
        [SerializeField, TextArea(1, 2)] private string reply;

        public string Label => label;
        public DialogueAction Action => action;
        public string Reply => reply;

        public DialogueChoice(string label, DialogueAction action, string reply = "")
        {
            this.label = label;
            this.action = action;
            this.reply = reply;
        }
    }

    /// <summary>
    /// Static definition of a non-player character: identity, placeholder visual
    /// colours, the lines they start with and any choices they offer. All data assets
    /// live under ScriptableObjects/NPCs. Replace the placeholder robe/skin colours with
    /// a real <see cref="prefab"/> later — swap one field, everything keeps working.
    /// </summary>
    [CreateAssetMenu(fileName = "NpcData", menuName = "Davidmon/NPC/New NPC", order = 2)]
    public sealed class NpcData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string npcId = "npc";
        [SerializeField] private string displayName = "New NPC";
        [SerializeField] private string title = "";
        [SerializeField, TextArea] private string description = "";

        [Header("Placeholder Visual")]
        [Tooltip("Used only until a real character prefab is assigned.")]
        [SerializeField] private Color robeColor = new Color(0.35f, 0.5f, 0.8f, 1f);
        [SerializeField] private Color skinColor = new Color(0.95f, 0.8f, 0.65f, 1f);
        [SerializeField] private bool hasHat;
        [Tooltip("Optional living/replacement visual. When set, it replaces the generated placeholder body.")]
        [SerializeField] private GameObject prefab;

        [Header("Interaction")]
        [SerializeField] private string interactionHint = "Talk";
        [SerializeField] private float interactRadius = 3.5f;

        [Header("Dialogue")]
        [SerializeField] private DialogueLine[] introLines = Array.Empty<DialogueLine>();
        [Tooltip("Choices offered after the intro lines are read.")]
        [SerializeField] private DialogueChoice[] choices = Array.Empty<DialogueChoice>();

        [Header("Role")]
        [Tooltip("Healer NPCs can restore the player's creature mid-conversation.")]
        [SerializeField] private bool isHealer;

        public string NpcId => npcId;
        public string DisplayName => displayName;
        public string Title => title;
        public string Description => description;
        public Color RobeColor => robeColor;
        public Color SkinColor => skinColor;
        public bool HasHat => hasHat;
        public GameObject Prefab => prefab;
        public string InteractionHint => interactionHint;
        public float InteractRadius => interactRadius;
        public DialogueLine[] IntroLines => introLines;
        public DialogueChoice[] Choices => choices;
        public bool IsHealer => isHealer;

        public override string ToString() => displayName;
    }
}