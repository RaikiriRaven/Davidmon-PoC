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

        /// <summary>Passive EXP trickle: small gain granted on an interval.</summary>
        private const float PassiveTickSeconds = 1.5f;
        private const long PassiveExpPerTick = 2L;
        private float _passiveTimer;

        /// <summary>Out-of-combat HP regen: 1.5% of max (rounded up, min 1) every
        /// 2 seconds once no damage was taken for <see cref="RegenDelaySeconds"/>.</summary>
        private const float RegenDelaySeconds = 8.5f;
        private const float RegenTickSeconds = 2f;
        private const float RegenPercent = 0.015f;
        private float _lastDamageTime = -100f;
        private float _regenTimer;

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
            ServiceLocator.Register(this);
            GameEvents.CreatureEvolved += OnCreatureEvolved;
            GameEvents.CreatureSelected += OnCreatureSelected;
        }

        private void OnDisable()
        {
            GameEvents.CreatureEvolved -= OnCreatureEvolved;
            GameEvents.CreatureSelected -= OnCreatureSelected;
        }

        private void Update()
        {
            // Passive EXP trickle (1.5s). Offline it applies locally; online it is
            // a server-validated request ("passive" source) like any other reward,
            // so clients can't forge amounts and parked avatars (inactive) skip it.
            if (HasCreature && !ActiveCreature.IsMaxLevel)
            {
                _passiveTimer += Time.deltaTime;
                if (_passiveTimer >= PassiveTickSeconds)
                {
                    _passiveTimer = 0f;
                    if (Davidmon.Multiplayer.ServerApi.IsOnline)
                        Davidmon.Multiplayer.ServerApi.AwardExp("passive", PassiveExpPerTick);
                    else
                        AddExperienceToActive(PassiveExpPerTick);
                }
            }

            // Out-of-combat HP regen (offline only — online the server runs the
            // same rule authoritatively and mirrors the result). Requires the
            // creature to be alive; faint/revive owns the 0-HP case.
            if (!HasCreature
                || Davidmon.Multiplayer.ServerApi.IsOnline
                || ActiveCreature.CurrentHp <= 0
                || ActiveCreature.CurrentHp >= ActiveCreature.MaxHp
                || Time.time - _lastDamageTime < RegenDelaySeconds)
            {
                _regenTimer = 0f;
                return;
            }
            _regenTimer += Time.deltaTime;
            if (_regenTimer < RegenTickSeconds) return;
            _regenTimer = 0f;
            int amount = Mathf.Max(1, Mathf.CeilToInt(ActiveCreature.MaxHp * RegenPercent));
            ActiveCreature.Heal(amount);
            RaiseHpChanged();
        }

        private void OnDestroy()
        {
            if (ServiceLocator.Get<PlayerManager>() == this)
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

        /// <summary>Restores a pre-built creature instance (save/load), keeping its progression.</summary>
        public void LoadCreature(CreatureInstance instance)
        {
            if (instance == null) return;
            ActiveCreature = instance;
            if (instance.Data != null) avatar?.SetCreature(instance.Data);
            GameEvents.RaiseCreatureSelected(instance.Data != null ? instance.Data.CreatureId : "");
            RaiseHpChanged();
        }

        /// <summary>Replaces the active creature after evolution, preserving its progression.</summary>
        public void EvolveTo(CreatureData newSpecies)
        {
            if (ActiveCreature == null || newSpecies == null) return;
            // Online: species changes are server-authoritative (TryEvolve mirror).
            // Local EvolveTo would desync; EvolutionUI routes via RequestEvolve.
            if (Davidmon.Multiplayer.ServerApi.IsOnline) return;
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
            _lastDamageTime = Time.time;
            // Online: damage is a server-validated request; the result arrives
            // via AuthHp/AuthMaxHp mirror (ApplyServerHp). No local mutation.
            if (Davidmon.Multiplayer.ServerApi.IsOnline)
            {
                Davidmon.Multiplayer.ServerApi.RequestDamage("enemy", amount);
                return;
            }
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
            // Online: heal is server-authoritative (healer NPC).
            if (Davidmon.Multiplayer.ServerApi.IsOnline)
            {
                Davidmon.Multiplayer.ServerApi.RequestHeal();
                return;
            }
            ActiveCreature.HealToFull();
            RaiseHpChanged();
        }

        /// <summary>
        /// Applies the server-authoritative HP mirror. Raises the same HUD event
        /// as local damage and starts the local revive ticker only as a fallback
        /// (the server owns the real revive timer via ProcessRevives).
        /// </summary>
        public void ApplyServerHp(int hp, int maxHp)
        {
            if (ActiveCreature == null) return;
            int clampedMax = Mathf.Max(1, maxHp);
            // Max may have changed (level-up/evolution): rebuild stats first by
            // re-applying level/exp, then set absolute HP.
            if (ActiveCreature.MaxHp != clampedMax)
            {
                int pct = clampedMax > 0
                    ? Mathf.Clamp(Mathf.RoundToInt((float)Mathf.Max(0, hp) / clampedMax * 100f), 0, 100)
                    : 100;
                // pct==0 means fainted; LoadState treats 0 as full-heal, so use 1% floor
                // then correct to 0 via SetCurrentHp below.
                ActiveCreature.LoadState(ActiveCreature.Data, ActiveCreature.Level, ActiveCreature.Exp,
                    Mathf.Max(1, pct));
            }
            ActiveCreature.SetCurrentHp(hp);
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