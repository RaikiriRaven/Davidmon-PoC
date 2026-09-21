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

        /// <summary>Replaces the active creature after evolution.</summary>
        public void EvolveTo(CreatureData newSpecies)
        {
            if (ActiveCreature == null || newSpecies == null) return;
            ActiveCreature.Evolve(newSpecies);
        }

        /// <summary>
        /// Grants experience to the active creature, handling any level-ups and
        /// automatically evolving into the next form once eligible.
        /// </summary>
        public int AddExperienceToActive(long amount)
        {
            if (ActiveCreature == null || amount <= 0) return 0;
            int levels = ActiveCreature.AddExperience(amount);
            TryAutoEvolve();
            return levels;
        }

        private void TryAutoEvolve()
        {
            if (ActiveCreature == null) return;
            var stage = ActiveCreature.GetAvailableEvolution();
            if (stage == null || stage.nextCreature == null) return;
            if (stage.nextCreature == ActiveCreature.Data) return;

            GameEvents.RaiseShowNotification(
                ActiveCreature.Data.DisplayName + " evolved into " + stage.nextCreature.DisplayName + "!");
            ActiveCreature.Evolve(stage.nextCreature);
            avatar?.SetCreature(stage.nextCreature);
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
            if (data != null) avatar?.SetCreature(data);
        }

        private void OnCreatureSelected(string creatureId)
        {
            // Kept for future unlocks/telemetry.
        }
    }
}