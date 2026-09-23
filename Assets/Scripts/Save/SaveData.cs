using System;
using System.Collections.Generic;

namespace Davidmon.Save
{
    /// <summary>Serialized creature progression used inside a save file.</summary>
    [Serializable]
    public sealed class CreatureSave
    {
        public string creatureId;
        public int level = 1;
        public long exp;
        /// <summary>HP as a whole-number percent (0 = full, matching <see cref="Creatures.CreatureInstance.LoadState"/>).</summary>
        public int hpPercent;
    }

    /// <summary>One item stack (itemId + quantity) inside a save file.</summary>
    [Serializable]
    public sealed class ItemStackSave
    {
        public string itemId;
        public int count;
    }

    /// <summary>
    /// Root save payload. Plain public fields so Unity's JsonUtility can round-trip it
    /// without glue. Versioned so future milestones can migrate old saves.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public int version = SaveManager.Version;
        public string timestamp;
        public long coins;

        public CreatureSave activeCreature;
        public List<ItemStackSave> inventory = new List<ItemStackSave>();

        public float posX;
        public float posY;
        public float posZ;
        public float rotY;
    }
}