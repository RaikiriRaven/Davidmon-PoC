using System;
using UnityEngine;
using Davidmon.Core;

namespace Davidmon.Creatures
{
    /// <summary>
    /// A living creature instance: static species definition plus mutable progression
    /// (level, experience, current HP). Raises the shared <see cref="GameEvents"/> on
    /// level-up and evolution so the HUD and other systems can react.
    /// This class is serializable so it can later be written into a save file.
    /// </summary>
    [Serializable]
    public class CreatureInstance
    {
        [SerializeField] private CreatureData data;

        [SerializeField] private int level = 1;
        [SerializeField] private long exp;
        [SerializeField] private int currentHp;
        [SerializeField] private int maxHp;
        [SerializeField] private int attack;
        [SerializeField] private int defense;
        [SerializeField] private int speed;

        public CreatureData Data => data;
        public int Level => level;
        public long Exp => exp;
        public int CurrentHp => currentHp;
        public int MaxHp => maxHp;
        public int Attack => attack;
        public int Defense => defense;
        public int Speed => speed;

        public bool IsMaxLevel => level >= CreatureLevels.LevelCap;
        public long ExpNextLevel => CreatureLevels.ExpForNextLevel(level);
        public bool IsAlive => currentHp > 0;

        public CreatureInstance() { }

        public CreatureInstance(CreatureData species, int startLevel = 1)
        {
            data = species;
            level = startLevel;
            ApplyStats();
            currentHp = maxHp;
        }

        private void ApplyStats()
        {
            int extraLevels = Mathf.Max(0, level - 1);
            StatBlock effective = data.BaseStats.Add(new StatBlock(
                data.StatGrowth.maxHp * extraLevels,
                data.StatGrowth.attack * extraLevels,
                data.StatGrowth.defense * extraLevels,
                data.StatGrowth.speed * extraLevels));

            maxHp = effective.maxHp;
            attack = effective.attack;
            defense = effective.defense;
            speed = effective.speed;
        }

        /// <summary>
        /// Grants experience, handles any level-ups, and returns how many levels were gained.
        /// </summary>
        public int AddExperience(long amount)
        {
            if (amount <= 0 || data == null || IsMaxLevel) return 0;

            exp += amount;
            GameEvents.RaiseExpGained(0, amount, exp);

            int levelsGained = 0;
            while (!IsMaxLevel && exp >= ExpNextLevel)
            {
                exp -= ExpNextLevel;
                level++;
                levelsGained++;

                ApplyStats();
                currentHp = maxHp;
                GameEvents.RaiseLevelUp(0, level, maxHp);
            }
            return levelsGained;
        }

        /// <summary>
        /// Returns the evolution stage that becomes available, or null.
        /// </summary>
        public EvolutionStage GetAvailableEvolution()
        {
            if (data == null) return null;
            foreach (var stage in data.EvolutionStages)
            {
                if (stage == null || stage.nextCreature == null) continue;
                if (level >= stage.requiredLevel)
                    return stage;
            }
            return null;
        }

        /// <summary>
        /// Evolves the creature into the new species, preserving level, experience and
        /// HP ratio. Raises CreatureEvolved so the world visual can be replaced.
        /// </summary>
        public void Evolve(CreatureData newSpecies)
        {
            if (newSpecies == null) return;

            float hpRatio = maxHp > 0 ? (float)currentHp / maxHp : 1f;
            string oldId = data != null ? data.CreatureId : "";
            string newId = newSpecies.CreatureId;

            data = newSpecies;
            ApplyStats();
            currentHp = Mathf.Clamp(Mathf.RoundToInt(maxHp * hpRatio), 0, maxHp);

            GameEvents.RaiseCreatureEvolved(oldId, newId);
        }

        public void TakeDamage(int amount)
        {
            currentHp = Mathf.Max(0, currentHp - Mathf.Max(0, amount));
        }

        public void HealToFull()
        {
            currentHp = maxHp;
        }

        public void Heal(int amount)
        {
            currentHp = Mathf.Min(maxHp, currentHp + Mathf.Max(0, amount));
        }

        /// <summary>
        /// Rebuilds the creature from serialized progression fields.
        /// Used by the save system to avoid running through exp arithmetic again.
        /// </summary>
        public void LoadState(CreatureData species, int savedLevel, long savedExp, int hpFractionPercent)
        {
            data = species;
            level = savedLevel;
            exp = savedExp;
            ApplyStats();
            currentHp = Mathf.Clamp(hpFractionPercent, 0, 100) == 0
                ? maxHp
                : Mathf.RoundToInt(maxHp * (hpFractionPercent / 100f));
        }
    }
}