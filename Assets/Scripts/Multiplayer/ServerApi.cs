using System;
using System.Collections.Generic;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Inventory;
using Davidmon.Player;

namespace Davidmon.Multiplayer
{
    /// <summary>
    /// Milestone 12 — single choke point between gameplay code and rewards.
    ///
    /// Rule 13 documentation:
    /// - Authority: offline it applies locally (single-player). Online it only
    ///   ever sends *requests* (ServerRpcs); the server in ServerGameState decides.
    /// - Ownership: the owned NetworkPlayer issues requests for its connection.
    /// - Synchronization: results arrive as SyncVars, mirrored into the local
    ///   PlayerManager/Wallet/PlayerInventory so HUD, shop and save keep working.
    /// - Validation: server-side (sources, caps, rate limits, prices, funds).
    /// - Persistence: mirrors stay save-compatible; SaveManager is untouched.
    ///
    /// Gameplay code (enemies, boss, shop) must call these instead of mutating
    /// Wallet/PlayerInventory/PlayerManager directly.
    /// </summary>
    public static class ServerApi
    {
        /// <summary>Owned network player; set by NetworkPlayer.OnStartClient.</summary>
        public static NetworkPlayer Local { get; set; }

        /// <summary>True when a FishNet client is running (host counts).</summary>
        public static bool IsOnline
        {
            get
            {
                try { return Local != null && FishNet.InstanceFinder.IsClientStarted; }
                catch (Exception) { return false; }
            }
        }

        public static void AwardExp(string source, long amount)
        {
            if (amount <= 0) return;
            if (!IsOnline)
            {
                PlayerManager pm = ServiceLocator.Get<PlayerManager>();
                if (pm != null) pm.AddExperienceToActive(amount);
                return;
            }
            Local.CmdAwardExp(source, amount);
        }

        public static void AwardCoins(string source, long amount)
        {
            if (amount <= 0) return;
            if (!IsOnline)
            {
                ServiceLocator.GetOrCreate(() => new Wallet()).AddCoins(amount);
                return;
            }
            Local.CmdAwardCoins(source, amount);
        }

        public static void GrantItem(string source, string itemId, int qty)
        {
            if (string.IsNullOrEmpty(itemId) || qty <= 0) return;
            if (!IsOnline)
            {
                ItemData data = ItemRegistry.Find(itemId);
                if (data != null)
                    ServiceLocator.GetOrCreate(() => new PlayerInventory()).Add(data, qty);
                return;
            }
            Local.CmdGrantItem(source, itemId, qty);
        }

        /// <summary>Shop purchase request (result arrives as receipt/denial notice).</summary>
        public static void RequestPurchase(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            if (!IsOnline) return; // offline uses the local ShopUI path
            Local.CmdBuyItem(itemId);
        }

        /// <summary>Serializes the local mirror inventory for join-time adoption.</summary>
        public static string SnapshotInventory()
        {
            PlayerInventory inv = ServiceLocator.GetOrCreate(() => new PlayerInventory());
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kvp in inv.Items)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0) continue;
                sb.Append(kvp.Key).Append('=').Append(kvp.Value).Append(';');
            }
            return sb.ToString();
        }
    }
}
