using System;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing.Client;
using UnityEngine;
using Davidmon.World;

namespace Davidmon.Multiplayer
{
    /// <summary>
    /// Server -> all clients enemy HP mirror. No NetworkObject required: spawns are
    /// deterministic ("roam_0".., "boss_0") so every client owns a local visual copy
    /// and the server owns the HP record (ServerGameState.EnsureEnemy/DamageEnemyById).
    /// </summary>
    public struct EnemyHpBroadcast : IBroadcast
    {
        public string EnemyId;
        public int Hp;
        public int MaxHp;
        public bool Died;
    }

    public static class EnemyNetwork
    {
        private static bool _handlerRegistered;

        public static void EnsureClientHandler()
        {
            if (_handlerRegistered) return;
            ClientManager cm = null;
            try { cm = InstanceFinder.ClientManager; } catch (Exception) { cm = null; }
            if (cm == null) return;
            try
            {
                cm.RegisterBroadcast<EnemyHpBroadcast>(OnEnemyHp);
                _handlerRegistered = true;
            }
            catch (Exception e) { Debug.LogWarning("[EnemyNetwork] Handler skipped: " + e.Message); }
        }

        private static void OnEnemyHp(EnemyHpBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if (string.IsNullOrEmpty(msg.EnemyId)) return;
            // Find the local visual copy by deterministic id (gameObject.name).
            for (int i = EnemyRegistry.All.Count - 1; i >= 0; i--)
            {
                var target = EnemyRegistry.All[i];
                if (target == null) continue;
                if (target is Component c && c.gameObject.name == msg.EnemyId)
                {
                    if (target is RoamingEnemy roam) roam.ApplyServerHp(msg.Hp, msg.MaxHp, msg.Died);
                    else if (target is BossEnemy boss) boss.ApplyServerHp(msg.Hp, msg.MaxHp, msg.Died);
                    return;
                }
            }
        }
    }
}
