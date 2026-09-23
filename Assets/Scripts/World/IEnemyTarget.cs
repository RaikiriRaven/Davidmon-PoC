using UnityEngine;

namespace Davidmon.World
{
    /// <summary>
    /// Anything the player can damage: roaming enemies and bosses. Player abilities
    /// resolve targets through <see cref="EnemyRegistry"/> as this interface so bosses
    /// share the exact same combat path without special-casing.
    /// </summary>
    public interface IEnemyTarget
    {
        bool IsAlive { get; }
        int Defense { get; }
        Vector3 Position { get; }
        void TakeDamage(int rawAmount);
    }
}