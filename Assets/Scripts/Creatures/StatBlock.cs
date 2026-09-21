using System;

namespace Davidmon.Creatures
{
    /// <summary>
    /// The core numeric attributes of a creature.
    /// <see cref="CreatureData.baseStats"/> is the level-1 value;
    /// <see cref="CreatureData.statGrowth"/> is added per extra level.
    /// </summary>
    [Serializable]
    public struct StatBlock
    {
        public int maxHp;
        public int attack;
        public int defense;
        public int speed;

        public StatBlock(int hp, int atk, int def, int spd)
        {
            maxHp = hp;
            attack = atk;
            defense = def;
            speed = spd;
        }

        public StatBlock Add(StatBlock other)
        {
            return new StatBlock(maxHp + other.maxHp, attack + other.attack, defense + other.defense, speed + other.speed);
        }
    }
}