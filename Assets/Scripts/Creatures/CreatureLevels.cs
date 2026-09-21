using UnityEngine;

namespace Davidmon.Creatures
{
    /// <summary>
    /// Central progression math. A single exponential formula governs how much
    /// experience is required to advance from one level to the next, so new content
    /// never needs hand-authored level tables.
    /// </summary>
    public static class CreatureLevels
    {
        public const int LevelCap = 100;

        /// <summary>
        /// Experience required to go from <paramref name="level"/> to level+1.
        /// Roughly 100 * level^1.5 — tuned to feel fast during development.
        /// </summary>
        public static long ExpForNextLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 1, LevelCap - 1);
            return (long)Mathf.Round(100f * Mathf.Pow(clamped, 1.5f));
        }

        /// <summary>Total experience needed to reach <paramref name="targetLevel"/> from level 1.</summary>
        public static long TotalExpForLevel(int targetLevel)
        {
            long total = 0;
            for (int level = 1; level < targetLevel; level++)
                total += ExpForNextLevel(level);
            return total;
        }

        /// <summary>Fraction of the current level's experience bar that is filled (0..1).</summary>
        public static float ProgressIntoLevel(int level, long currentExp)
        {
            if (level >= LevelCap) return 1f;
            long needed = ExpForNextLevel(level);
            return needed <= 0 ? 0f : Mathf.Clamp01((float)currentExp / needed);
        }
    }
}