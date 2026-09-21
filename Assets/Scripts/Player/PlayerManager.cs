using System.Collections;
using UnityEngine;
using Davidmon.Core;
using Davidmon.Creatures;

namespace Davidmon.Player
{
    /// <summary>
    /// Owns the player's active creature instance and reacts to evolution by swapping
    /// the avatar. Registered in the ServiceLocator so other systems (HUD, combat)
    /// can resolve the local player without FindObjectOfType.
    /// </summary>
    public sealed class PlayerManager : MonoBehaviour
    {
        [SerializeField] private PlayerAvatar avatar;

        public CreatureInstance ActiveCreature { get; private set; }
        public bool HasCreature => ActiveCreature != null;
        public bool IsFainted => HasCreature && !ActiveCreature.IsAlive;

        private void Awake()
        {
            if (avatar == null) avatar = GetComponent<PlayerAvatar>();
            if (avatar == null) avatar = GetComponentInChildren<PlayerAvatar>();
            ServiceLocator.Register(this);
        }

        private void OnEnable()
        {
            GameEvents.CreatureEvolved += OnCreatureEvolved;
            GameEvents.CreatureSelected += OnCreatureSelected;
        }

        private void OnDisable()
        {
            GameEvents.CreatureEvolved -= OnCreatureEvolved;
            GameEvents.CreatureSelected -= OnCreatureSelected;
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister<PlayerManager>();
        }

        /// <summary>Spawns a fresh creature of the given species at the given level.</summary>
        public void SetCreature(CreatureData data, int level = 1)
        {
            if (data == null)
            {
                ActiveCreature = null;
                avatar?.SetCreature(null);
                return;
            }

            ActiveCreature = new CreatureInstance(data, level);
            avatar.SetCreature(data);
            GameEvents.RaiseCreatureSelected(data.CreatureId);
        }

        /// <summary>Replaces the active creature after evolution, preserving its progression.</summary>
        public void EvolveTo(CreatureData newSpecies)
        {
            if (ActiveCreature == null || newSpecies == null) return;
            string fromName = ActiveCreature.Data != null ? ActiveCreature.Data.DisplayName : "Protoform";
            ActiveCreature.Evolve(newSpecies);
            avatar?.SetCreature(newSpecies);
            if (newSpecies) GameEvents.RaiseShowLegendaryNotification(fromName + " evolved into " + newSpecies.DisplayName + "!");
        }

        /// <summary>
        /// Grants experience to the active creature, handling any level-ups. Evolution
        /// is now player-initiated via the Evolutions menu: when a stage unlocks the
        /// player is told so instead of being transformed automatically.
        /// </summary>
        public int AddExperienceToActive(long amount)
        {
            if (ActiveCreature == null || amount <= 0) return 0;
            int availableBefore = AvailableEvolutionCount();
            int levels = ActiveCreature.AddExperience(amount);
            if (levels > 0)
                GameEvents.RaiseShowNotification(ActiveCreature.Data.DisplayName
                    + " reached Lv." + ActiveCreature.Level + "!");
            if (AvailableEvolutionCount() > availableBefore)
                GameEvents.RaiseShowLegendaryNotification(ActiveCreature.Data.DisplayName
                    + " can evolve now! Open the Evolutions menu.");
            return levels;
        }

        /// <summary>Number of evolution stages currently available to the active creature.</summary>
        public int AvailableEvolutionCount()
        {
            if (ActiveCreature == null) return 0;
            int count = 0;
            foreach (EvolutionStage stage in ActiveCreature.Data.EvolutionStages)
                if (IsEvolutionUnlocked(stage)) count++;
            return count;
        }

        /// <summary>Whether an evolution stage is available: level met and any item requirement satisfied.</summary>
        public bool IsEvolutionUnlocked(EvolutionStage stage)
        {
            return ActiveCreature != null && stage != null && stage.nextCreature != null
                && ActiveCreature.Level >= stage.requiredLevel
                && string.IsNullOrEmpty(stage.requiredItemId);
        }

        /// <summary>Applies damage to the active creature and handles fainting/revive.</summary>
        public void TakeDamageToActive(int amount)
        {
            if (ActiveCreature == null || amount <= 0) return;
            int before = ActiveCreature.CurrentHp;
            ActiveCreature.TakeDamage(amount);
            RaiseHpChanged();

            if (before > 0 && ActiveCreature.CurrentHp <= 0)
                StartCoroutine(ReviveAfterSeconds(4f));
        }

        /// <summary>Restores the active creature's HP to full immediately.</summary>
        public void HealActiveToFull()
        {
            if (ActiveCreature == null) return;
            ActiveCreature.HealToFull();
            RaiseHpChanged();
        }

        public void RaiseHpChanged()
        {
            if (ActiveCreature == null) return;
            GameEvents.RaiseCreatureHpChanged(ActiveCreature.CurrentHp, ActiveCreature.MaxHp);
        }

        private IEnumerator ReviveAfterSeconds(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (ActiveCreature != null)
            {
                ActiveCreature.HealToFull();
                RaiseHpChanged();
                GameEvents.RaiseShowNotification(ActiveCreature.Data.DisplayName + " recovered its strength!");
            }
        }

        private void OnCreatureEvolved(string fromId, string toId)
        {
            CreatureData data = CreatureRegistry.Find(toId);
            if (data != null && avatar != null && avatar.CurrentData != data)
                avatar?.SetCreature(data);
        }

        private void OnCreatureSelected(string creatureId)
        {
            // Kept for future unlocks/telemetry.
        }
    }
}