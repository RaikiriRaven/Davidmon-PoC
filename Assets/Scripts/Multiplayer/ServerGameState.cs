using System;
using System.Collections.Generic;
using UnityEngine;
using Davidmon.Creatures;
using Davidmon.Inventory;

namespace Davidmon.Multiplayer
{
    /// <summary>
    /// Milestone 12 — server-authoritative gameplay state (networking rules 1-3).
    ///
    /// Rule 13 documentation:
    /// - Authority: server only. All mutation methods must run on the server
    ///   (called from ServerRpcs). Clients never write records directly.
    /// - Ownership: one <see cref="Record"/> per client connection id. Records are
    ///   plain server memory; clients hold read-only mirrors via SyncVars.
    /// - Synchronization: server writes NetworkPlayer SyncVars (AuthLevel/AuthExp/
    ///   AuthCoins/InventoryState); FishNet replicates to owner + observers.
    /// - Validation: whitelisted sources, per-call amount caps, per-connection
    ///   sliding-window rate limits, server-side price lookup, funds + stack caps.
    /// - Persistence: records are session state. The owner's local mirror stays
    ///   save-compatible, so SaveManager keeps persisting progression to disk.
    ///
    /// Join baseline (session trust, documented): the first snapshot a connection
    /// sends (species/level/exp/coins/inventory) is adopted once, so single-player
    /// progress survives into multiplayer. Every later change must pass validation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ServerGameState : MonoBehaviour
    {
        public static ServerGameState Instance { get; private set; }

        [Tooltip("Authoritative shop stock used to price purchases (StarterShop asset).")]
        [SerializeField] private ShopStock defaultStock;

        // ----- Validation policy -----
        private static readonly string[] ExpSources = { "combat", "boss", "quest" };
        private static readonly string[] CoinSources = { "combat", "boss", "quest" };
        private static readonly string[] ItemSources = { "boss", "quest" };

        private const long MaxExpPerCall = 5000L;
        private const long MaxCoinsPerCall = 20000L;
        private const int MaxQtyPerCall = 5;
        private const long MaxCoinsTotal = 999999999L;
        private const int MaxCallsPerWindow = 60;
        private const float WindowSeconds = 10f;

        /// <summary>Server-side per-player record. Never sent to clients wholesale.</summary>
        public sealed class Record
        {
            public int clientId;
            public string creatureId = "";
            public int level = 1;
            public long exp;
            public long coins;
            public readonly Dictionary<string, int> inventory = new Dictionary<string, int>();
            public bool baselineAdopted;
            public float windowStart;
            public int windowCalls;
        }

        private readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public Record GetOrCreate(int clientId)
        {
            if (!_records.TryGetValue(clientId, out Record r))
            {
                r = new Record { clientId = clientId, windowStart = Time.time };
                _records[clientId] = r;
            }
            return r;
        }

        private static bool ValidSource(string source, string[] allowed)
        {
            if (string.IsNullOrEmpty(source)) return false;
            for (int i = 0; i < allowed.Length; i++)
                if (source == allowed[i]) return true;
            return false;
        }

        private static bool CheckRate(Record r)
        {
            float now = Time.time;
            if (now - r.windowStart >= WindowSeconds)
            {
                r.windowStart = now;
                r.windowCalls = 0;
            }
            if (r.windowCalls >= MaxCallsPerWindow) return false;
            r.windowCalls++;
            return true;
        }

        /// <summary>Adopts the joining player's snapshot once; later calls only change species.</summary>
        public void AdoptBaseline(int clientId, string creatureId, int level, long exp, long coins, string invState)
        {
            Record r = GetOrCreate(clientId);
            if (CreatureRegistry.Find(creatureId) != null)
                r.creatureId = creatureId;

            if (r.baselineAdopted) return;
            r.baselineAdopted = true;

            r.level = Mathf.Clamp(level, 1, CreatureLevels.LevelCap);
            r.exp = Math.Max(0L, exp);
            r.coins = Math.Min(MaxCoinsTotal, Math.Max(0L, coins));

            foreach (KeyValuePair<string, int> stack in ParseInventory(invState))
            {
                ItemData data = ItemRegistry.Find(stack.Key);
                if (data == null) continue;
                r.inventory[stack.Key] = Mathf.Clamp(stack.Value, 1, Math.Max(1, data.MaxStack));
            }
        }

        public bool AwardExp(int clientId, string source, long amount,
            out int level, out long exp, out int levelsGained, out string error)
        {
            level = 1; exp = 0; levelsGained = 0; error = null;
            if (!ValidSource(source, ExpSources)) { error = "Bad EXP source."; return false; }
            if (amount < 1 || amount > MaxExpPerCall) { error = "Bad EXP amount."; return false; }

            Record r = GetOrCreate(clientId);
            if (!CheckRate(r)) { error = "Server busy."; return false; }

            r.exp += amount;
            while (r.level < CreatureLevels.LevelCap && r.exp >= CreatureLevels.ExpForNextLevel(r.level))
            {
                r.exp -= CreatureLevels.ExpForNextLevel(r.level);
                r.level++;
                levelsGained++;
            }

            level = r.level; exp = r.exp;
            return true;
        }

        public bool AwardCoins(int clientId, string source, long amount, out long coins, out string error)
        {
            coins = 0; error = null;
            if (!ValidSource(source, CoinSources)) { error = "Bad coin source."; return false; }
            if (amount < 1 || amount > MaxCoinsPerCall) { error = "Bad coin amount."; return false; }

            Record r = GetOrCreate(clientId);
            if (!CheckRate(r)) { error = "Server busy."; return false; }

            r.coins = Math.Min(MaxCoinsTotal, r.coins + amount);
            coins = r.coins;
            return true;
        }

        public bool GrantItem(int clientId, string source, string itemId, int qty, out string error)
        {
            error = null;
            if (!ValidSource(source, ItemSources)) { error = "Bad item source."; return false; }
            if (qty < 1 || qty > MaxQtyPerCall) { error = "Bad item quantity."; return false; }

            ItemData data = ItemRegistry.Find(itemId);
            if (data == null) { error = "Unknown item."; return false; }

            Record r = GetOrCreate(clientId);
            if (!CheckRate(r)) { error = "Server busy."; return false; }

            r.inventory.TryGetValue(itemId, out int current);
            int room = Math.Max(0, data.MaxStack - current);
            if (room <= 0) { error = data.DisplayName + " stack is full."; return false; }
            r.inventory[itemId] = current + Math.Min(room, qty);
            return true;
        }

        public bool TryBuy(int clientId, string itemId, out long price, out string receipt, out string error)
        {
            price = 0; receipt = null; error = null;
            if (defaultStock == null || defaultStock.Entries == null)
            {
                error = "Shop unavailable.";
                return false;
            }

            ShopEntry match = null;
            foreach (ShopEntry e in defaultStock.Entries)
            {
                if (e != null && e.Item != null && e.Item.ItemId == itemId) { match = e; break; }
            }
            if (match == null) { error = "Not sold here."; return false; }

            ItemData data = match.Item;
            price = Math.Max(0, match.Price);

            Record r = GetOrCreate(clientId);
            if (!CheckRate(r)) { error = "Server busy."; return false; }

            r.inventory.TryGetValue(itemId, out int current);
            if (current >= data.MaxStack)
            {
                error = "You already carry the maximum amount of " + data.DisplayName + ".";
                return false;
            }
            if (r.coins < price)
            {
                error = "Not enough coins for " + data.DisplayName + " (" + price + " needed).";
                return false;
            }

            r.coins -= price;
            r.inventory[itemId] = current + 1;
            receipt = "Bought " + data.DisplayName + " for " + price + " coins.";
            return true;
        }

        /// <summary>Serializes a record inventory as "id=count;..." (SyncVar-safe).</summary>
        public string SerializeInventory(Record r)
        {
            if (r == null || r.inventory.Count == 0) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kvp in r.inventory)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0) continue;
                sb.Append(kvp.Key).Append('=').Append(kvp.Value).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Parses "id=count;..." tolerantly (unknown ids filtered later).</summary>
        public static List<KeyValuePair<string, int>> ParseInventory(string s)
        {
            var out_ = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(s)) return out_;
            string[] parts = s.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                int eq = p.IndexOf('=');
                if (eq <= 0 || eq >= p.Length - 1) continue;
                if (!int.TryParse(p.Substring(eq + 1), out int count) || count <= 0) continue;
                out_.Add(new KeyValuePair<string, int>(p.Substring(0, eq), count));
            }
            return out_;
        }
    }
}
