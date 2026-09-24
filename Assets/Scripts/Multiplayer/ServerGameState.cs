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
        private static readonly string[] ExpSources = { "combat", "boss", "quest", "passive" };
        private static readonly string[] CoinSources = { "combat", "boss", "quest" };
        private static readonly string[] ItemSources = { "boss", "quest" };

        private const long MaxExpPerCall = 5000L;
        private const long MaxCoinsPerCall = 20000L;
        private const int MaxQtyPerCall = 5;
        private const long MaxCoinsTotal = 999999999L;
        private const int MaxCallsPerWindow = 60;
        private const float WindowSeconds = 10f;
        private const int MaxDamagePerCall = 5000;
        private const int MaxEnemyDamagePerCall = 2000;
        private const float ReviveSeconds = 4f;

        /// <summary>Server-side per-player record. Never sent to clients wholesale.</summary>
        public sealed class Record
        {
            public int clientId;
            public string creatureId = "";
            public int level = 1;
            public long exp;
            public long coins;
            public int currentHp;
            public float reviveAt;
            public float lastDamageAt = -100f;
            public readonly Dictionary<string, int> inventory = new Dictionary<string, int>();
            public bool baselineAdopted;
            public float windowStart;
            public int windowCalls;
        }

        /// <summary>Server-side enemy record keyed by NetworkObject id.</summary>
        public sealed class EnemyRecord
        {
            public uint netId;
            public string serverId = "";
            public string speciesId = "";
            public int level = 1;
            public int hp = 1;
            public int maxHp = 1;
            public bool dead;
            public bool isBoss;
            public string dropItemId;
            public Vector3 home;
            public float respawnAt;
            public float respawnSeconds = 12f;
        }

        private readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();
        private readonly Dictionary<uint, EnemyRecord> _enemies = new Dictionary<uint, EnemyRecord>();
        private readonly Dictionary<string, EnemyRecord> _enemiesById = new Dictionary<string, EnemyRecord>();

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

        /// <summary>
        /// Adopts the joining player's snapshot once (session trust). Later calls are
        /// ignored for species/progression — species changes must go through TryEvolve
        /// so evolution requirements are server-validated.
        /// </summary>
        public void AdoptBaseline(int clientId, string creatureId, int level, long exp, long coins, string invState)
        {
            Record r = GetOrCreate(clientId);
            if (r.baselineAdopted) return;
            r.baselineAdopted = true;

            if (CreatureRegistry.Find(creatureId) != null)
                r.creatureId = creatureId;

            r.level = Mathf.Clamp(level, 1, CreatureLevels.LevelCap);
            r.exp = Math.Max(0L, exp);
            r.coins = Math.Min(MaxCoinsTotal, Math.Max(0L, coins));
            r.currentHp = MaxHpFor(r.creatureId, r.level);

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
            // Parity with single-player (CreatureInstance.AddExperience heals to
            // full on level-up, even from fainted).
            if (levelsGained > 0)
            {
                r.currentHp = MaxHpFor(r.creatureId, r.level);
                r.reviveAt = 0f;
            }
            else
            {
                // Clamp HP into the (possibly unchanged) max so stale values can't linger.
                r.currentHp = Math.Min(r.currentHp, MaxHpFor(r.creatureId, r.level));
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

        /// <summary>Duplicates the CreatureInstance stat formula for max HP (server-side).</summary>
        public static int MaxHpFor(string speciesId, int level)
        {
            CreatureData data = CreatureRegistry.Find(speciesId);
            if (data == null) return 1;
            int extra = Math.Max(0, level - 1);
            return Math.Max(1, data.BaseStats.maxHp + data.StatGrowth.maxHp * extra);
        }

        // ----- Player damage / healing (authoritative HP) -----

        /// <summary>Client-reported damage intake (bolts). Validated like rewards.</summary>
        public bool DamagePlayer(int clientId, string source, int amount,
            out int hp, out int maxHp, out bool died, out string error)
        {
            hp = 0; maxHp = 1; died = false; error = null;
            if (source != "enemy") { error = "Bad damage source."; return false; }
            if (amount < 1 || amount > MaxDamagePerCall) { error = "Bad damage."; return false; }

            Record r = GetOrCreate(clientId);
            if (!CheckRate(r)) { error = "Server busy."; return false; }
            return ApplyDamageInternal(r, amount, out hp, out maxHp, out died, out error);
        }

        /// <summary>Server-internal damage (contact, slam, hazards). No rate limit.</summary>
        public bool DamagePlayerDirect(int clientId, int amount, out int hp, out int maxHp, out bool died)
        {
            hp = 0; maxHp = 1; died = false;
            string error;
            return ApplyDamageInternal(GetOrCreate(clientId),
                Math.Min(Math.Max(1, amount), MaxDamagePerCall), out hp, out maxHp, out died, out error);
        }

        private static bool ApplyDamageInternal(Record r, int amount,
            out int hp, out int maxHp, out bool died, out string error)
        {
            hp = 0; maxHp = 1; died = false; error = null;
            maxHp = MaxHpFor(r.creatureId, r.level);
            if (string.IsNullOrEmpty(r.creatureId)) { error = "No creature."; return false; }
            if (r.currentHp <= 0) { error = "Already fainted."; return false; }

            r.currentHp = Math.Max(0, r.currentHp - Math.Max(1, amount));
            r.lastDamageAt = Time.time;
            hp = r.currentHp;
            died = hp <= 0;
            if (died) r.reviveAt = Time.time + ReviveSeconds;
            return true;
        }

        /// <summary>Processes fainted-player revives. Call from a server-side Update.</summary>
        public void ProcessRevives()
        {
            float now = Time.time;
            foreach (KeyValuePair<int, Record> kvp in _records)
            {
                Record r = kvp.Value;
                if (r.currentHp <= 0 && r.reviveAt > 0f && now >= r.reviveAt)
                {
                    r.reviveAt = 0f;
                    r.currentHp = MaxHpFor(r.creatureId, r.level);
                    revived.Add(r.clientId);
                }
            }
        }

        private readonly List<int> revived = new List<int>();
        private readonly List<string> _enemyRevived = new List<string>();
        private float _syncTimer;
        private float _regenTimer;

        /// <summary>Out-of-combat HP regen (server-authoritative mirror of the
        /// single-player rule): 1.5% of max, rounded up, every 2 seconds after
        /// 8.5s without damage. Fainted creatures are owned by the revive timer.</summary>
        private const float RegenDelaySeconds = 8.5f;
        private const float RegenTickSeconds = 2f;
        private const float RegenPercent = 0.015f;

        private void Update()
        {
            // Server-only: advance revive timers and keep HP mirrors fresh.
            // (Reward paths push immediately via RPCs; this covers revives and
            // any direct-record grants such as DamageEnemy kill rewards.)
            bool isServer = false;
            try { isServer = FishNet.InstanceFinder.IsServerStarted; }
            catch (Exception) { isServer = false; }
            if (!isServer) return;

            revived.Clear();
            ProcessRevives();

            // Passive HP regen sweep (2s cadence). Results ride the existing
            // 0.5s SyncVar sync below — no extra push needed.
            _regenTimer += Time.deltaTime;
            if (_regenTimer >= RegenTickSeconds)
            {
                _regenTimer = 0f;
                float now = Time.time;
                foreach (KeyValuePair<int, Record> kvp in _records)
                {
                    Record r = kvp.Value;
                    if (string.IsNullOrEmpty(r.creatureId)) continue;
                    if (r.currentHp <= 0) continue; // faint/revive owns 0 HP
                    int maxHp = MaxHpFor(r.creatureId, r.level);
                    if (r.currentHp >= maxHp) continue;
                    if (now - r.lastDamageAt < RegenDelaySeconds) continue;
                    r.currentHp = Math.Min(maxHp, r.currentHp
                        + Math.Max(1, Mathf.CeilToInt(maxHp * RegenPercent)));
                }
            }

            // Enemy respawns -> broadcast revived HP to all clients.
            _enemyRevived.Clear();
            ProcessEnemyRespawns(_enemyRevived);
            if (_enemyRevived.Count > 0)
            {
                try
                {
                    var sm = FishNet.InstanceFinder.ServerManager;
                    for (int k = 0; k < _enemyRevived.Count; k++)
                    {
                        if (_enemiesById.TryGetValue(_enemyRevived[k], out EnemyRecord er) && er != null)
                            sm.Broadcast(new EnemyHpBroadcast
                            {
                                EnemyId = er.serverId,
                                Hp = er.hp,
                                MaxHp = er.maxHp,
                                Died = false
                            });
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Server] Enemy respawn broadcast failed: " + e.Message); }
            }

            _syncTimer -= Time.deltaTime;
            bool periodic = _syncTimer <= 0f;
            if (periodic) _syncTimer = 0.5f;
            if (revived.Count == 0 && !periodic) return;

            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayer p = players[i];
                if (p == null || !p.IsServerInitialized || p.Owner == null) continue;
                if (!_records.TryGetValue(p.Owner.ClientId, out Record r)) continue;
                int maxHp = Mathf.Max(1, MaxHpFor(r.creatureId, r.level));
                // Only touch SyncVars when something actually changed (or revived).
                if (p.AuthMaxHp.Value != maxHp) p.AuthMaxHp.Value = maxHp;
                int hp = Mathf.Clamp(r.currentHp, 0, maxHp);
                if (p.AuthHp.Value != hp) p.AuthHp.Value = hp;
                // Keep level/exp/coins/inventory fresh for server-granted rewards.
                if (periodic)
                {
                    if (p.CreatureLevel.Value != Mathf.Max(1, r.level))
                        p.CreatureLevel.Value = Mathf.Max(1, r.level);
                    if (p.AuthExp.Value != Math.Max(0L, r.exp))
                        p.AuthExp.Value = Math.Max(0L, r.exp);
                    if (p.AuthCoins.Value != Math.Max(0L, r.coins))
                        p.AuthCoins.Value = Math.Max(0L, r.coins);
                    string inv = SerializeInventory(r);
                    if (p.InventoryState.Value != inv) p.InventoryState.Value = inv;
                    if (p.CreatureId.Value != (r.creatureId ?? ""))
                        p.CreatureId.Value = r.creatureId ?? "";
                }
            }
        }

        /// <summary>Full heal (healer NPC). Rate-limited like other requests.</summary>
        public bool HealPlayer(int clientId, out int hp, out int maxHp, out string error)
        {
            hp = 0; maxHp = 1; error = null;
            Record r = GetOrCreate(clientId);
            if (!CheckRate(r)) { error = "Server busy."; return false; }
            maxHp = MaxHpFor(r.creatureId, r.level);
            if (string.IsNullOrEmpty(r.creatureId)) { error = "No creature."; return false; }
            r.reviveAt = 0f;
            r.currentHp = maxHp;
            hp = r.currentHp;
            return true;
        }

        /// <summary>Validates evolution requirements and applies the species change.</summary>
        public bool TryEvolve(int clientId, string targetId, out string error)
        {
            error = null;
            Record r = GetOrCreate(clientId);
            CreatureData cur = CreatureRegistry.Find(r.creatureId);
            CreatureData target = CreatureRegistry.Find(targetId);
            if (cur == null || target == null) { error = "Unknown species."; return false; }

            bool ok = false;
            foreach (EvolutionStage stage in cur.EvolutionStages)
            {
                if (stage == null || stage.nextCreature == null) continue;
                if (stage.nextCreature.CreatureId == targetId
                    && r.level >= stage.requiredLevel
                    && string.IsNullOrEmpty(stage.requiredItemId)) { ok = true; break; }
            }
            if (!ok) { error = "Evolution requirements not met."; return false; }

            int oldMax = Math.Max(1, MaxHpFor(cur.CreatureId, r.level));
            int newMax = Math.Max(1, MaxHpFor(targetId, r.level));
            float ratio = r.currentHp > 0 ? (float)r.currentHp / oldMax : 1f;
            r.creatureId = targetId;
            r.currentHp = Math.Max(1, Mathf.RoundToInt(newMax * Mathf.Clamp01(ratio)));
            return true;
        }

        // ----- Shared enemies -----

        public void RegisterEnemy(uint netId, string speciesId, int level, bool isBoss, string dropItemId, Vector3 home)
        {
            var rec = new EnemyRecord
            {
                netId = netId,
                speciesId = speciesId ?? "",
                level = Math.Max(1, level),
                isBoss = isBoss,
                dropItemId = dropItemId,
                home = home
            };
            rec.maxHp = Math.Max(1, MaxHpFor(rec.speciesId, rec.level));
            rec.hp = rec.maxHp;
            rec.dead = false;
            _enemies[netId] = rec;
        }

        public void UnregisterEnemy(uint netId)
        {
            _enemies.Remove(netId);
        }

        /// <summary>
        /// Applies attacker damage to a shared enemy. On death, rewards are granted
        /// directly to the killer's record (server-computed) and returned for notice.
        /// </summary>
        public bool DamageEnemy(int attackerConn, uint netId, int amount,
            out int hp, out int maxHp, out bool died,
            out int rewardExp, out long rewardCoins, out string rewardItem, out string error)
        {
            hp = 0; maxHp = 1; died = false;
            rewardExp = 0; rewardCoins = 0; rewardItem = null; error = null;

            if (amount < 1 || amount > MaxEnemyDamagePerCall) { error = "Bad damage."; return false; }
            if (!_enemies.TryGetValue(netId, out EnemyRecord e)) { error = "Unknown enemy."; return false; }
            if (e.dead) { error = "Already defeated."; return false; }

            Record a = GetOrCreate(attackerConn);
            if (!CheckRate(a)) { error = "Server busy."; return false; }

            e.hp = Math.Max(0, e.hp - amount);
            hp = e.hp; maxHp = e.maxHp;
            if (e.hp > 0) return true;

            e.dead = true;
            Record killer = GetOrCreate(attackerConn);
            if (e.isBoss)
            {
                rewardExp = 150 + e.level * 25;
                rewardCoins = 400 + e.level * 50L;
                rewardItem = e.dropItemId;
            }
            else
            {
                rewardExp = 15 + e.level * 10;
                rewardCoins = 8 + e.level * 5L;
            }
            GrantExpInternal(killer, rewardExp);
            GrantCoinsInternal(killer, rewardCoins);
            if (!string.IsNullOrEmpty(rewardItem))
                GrantItemInternal(killer, rewardItem, 1);
            died = true;
            return true;
        }

        public bool TryGetEnemy(uint netId, out EnemyRecord rec)
        {
            return _enemies.TryGetValue(netId, out rec);
        }

        /// <summary>Idempotent ensure for deterministic spawns ("roam_0", "boss_0").</summary>
        public void EnsureEnemy(string serverId, string speciesId, int level,
            bool isBoss, string dropItemId, Vector3 home, float respawnSeconds = 12f)
        {
            if (string.IsNullOrEmpty(serverId)) return;
            if (_enemiesById.TryGetValue(serverId, out EnemyRecord existing))
            {
                // Refresh static data (designer may have tweaked), keep live HP.
                existing.speciesId = speciesId ?? existing.speciesId;
                existing.level = Math.Max(1, level);
                existing.isBoss = isBoss;
                existing.dropItemId = dropItemId;
                existing.home = home;
                existing.respawnSeconds = Mathf.Max(2f, respawnSeconds);
                if (!existing.dead)
                {
                    existing.maxHp = Math.Max(1, MaxHpFor(existing.speciesId, existing.level));
                    if (existing.hp > existing.maxHp) existing.hp = existing.maxHp;
                }
                return;
            }
            var rec = new EnemyRecord
            {
                serverId = serverId,
                speciesId = speciesId ?? "",
                level = Math.Max(1, level),
                isBoss = isBoss,
                dropItemId = dropItemId,
                home = home,
                respawnSeconds = Mathf.Max(2f, respawnSeconds)
            };
            rec.maxHp = Math.Max(1, MaxHpFor(rec.speciesId, rec.level));
            rec.hp = rec.maxHp;
            rec.dead = false;
            _enemiesById[serverId] = rec;
        }

        public bool TryGetEnemyById(string serverId, out EnemyRecord rec)
        {
            rec = null;
            return !string.IsNullOrEmpty(serverId) && _enemiesById.TryGetValue(serverId, out rec);
        }

        /// <summary>
        /// String-keyed variant used by the broadcast path (no NetworkObject ids).
        /// Rewards are server-computed and granted to the killer's record.
        /// </summary>
        public bool DamageEnemyById(int attackerConn, string serverId, int amount,
            out int hp, out int maxHp, out bool died,
            out int rewardExp, out long rewardCoins, out string rewardItem, out string error)
        {
            hp = 0; maxHp = 1; died = false;
            rewardExp = 0; rewardCoins = 0; rewardItem = null; error = null;

            if (string.IsNullOrEmpty(serverId)) { error = "Unknown enemy."; return false; }
            if (amount < 1 || amount > MaxEnemyDamagePerCall) { error = "Bad damage."; return false; }
            if (!_enemiesById.TryGetValue(serverId, out EnemyRecord e)) { error = "Unknown enemy."; return false; }
            if (e.dead) { error = "Already defeated."; return false; }

            Record a = GetOrCreate(attackerConn);
            if (!CheckRate(a)) { error = "Server busy."; return false; }

            e.hp = Math.Max(0, e.hp - amount);
            hp = e.hp; maxHp = e.maxHp;
            if (e.hp > 0) return true;

            e.dead = true;
            e.respawnAt = Time.time + e.respawnSeconds;
            Record killer = GetOrCreate(attackerConn);
            if (e.isBoss)
            {
                rewardExp = 150 + e.level * 25;
                rewardCoins = 400 + e.level * 50L;
                rewardItem = e.dropItemId;
            }
            else
            {
                rewardExp = 15 + e.level * 10;
                rewardCoins = 8 + e.level * 5L;
            }
            GrantExpInternal(killer, rewardExp);
            GrantCoinsInternal(killer, rewardCoins);
            if (!string.IsNullOrEmpty(rewardItem))
                GrantItemInternal(killer, rewardItem, 1);
            died = true;
            return true;
        }

        /// <summary>Server-side respawn sweep. Returns ids that revived this tick.</summary>
        public void ProcessEnemyRespawns(List<string> revivedOut)
        {
            float now = Time.time;
            foreach (KeyValuePair<string, EnemyRecord> kvp in _enemiesById)
            {
                EnemyRecord e = kvp.Value;
                if (e != null && e.dead && e.respawnAt > 0f && now >= e.respawnAt)
                {
                    e.dead = false;
                    e.respawnAt = 0f;
                    e.maxHp = Math.Max(1, MaxHpFor(e.speciesId, e.level));
                    e.hp = e.maxHp;
                    if (revivedOut != null) revivedOut.Add(e.serverId);
                }
            }
        }

        /// <summary>
        /// Registers every locally spawned enemy (host scene) into the server table.
        /// Called when the server starts (spawners run before the server exists).
        /// Idempotent: live HP is preserved on re-scan.
        /// </summary>
        public void EnsureFromLocalScene()
        {
            foreach (Davidmon.World.IEnemyTarget target in Davidmon.World.EnemyRegistry.All)
            {
                if (target == null) continue;
                if (target is Component c)
                {
                    string id = c.gameObject != null ? c.gameObject.name : "";
                    if (string.IsNullOrEmpty(id)) continue;
                    if (target is Davidmon.World.RoamingEnemy roam && roam.Data != null)
                        EnsureEnemy(id, roam.Data.CreatureId, roam.Level, false, null, roam.Position, 12f);
                    else if (target is Davidmon.World.BossEnemy boss && boss.Data != null)
                        EnsureEnemy(id, boss.Data.CreatureId, boss.Level, true, "leaf_boss_egg", boss.Position, 60f);
                }
            }
        }

        private static void GrantExpInternal(Record r, long amount)
        {
            if (amount <= 0) return;
            int before = r.level;
            r.exp += amount;
            while (r.level < CreatureLevels.LevelCap && r.exp >= CreatureLevels.ExpForNextLevel(r.level))
            {
                r.exp -= CreatureLevels.ExpForNextLevel(r.level);
                r.level++;
            }
            if (r.level > before)
            {
                r.currentHp = MaxHpFor(r.creatureId, r.level);
                r.reviveAt = 0f;
            }
        }

        private static void GrantCoinsInternal(Record r, long amount)
        {
            if (amount <= 0) return;
            r.coins = Math.Min(MaxCoinsTotal, r.coins + amount);
        }

        private static void GrantItemInternal(Record r, string itemId, int qty)
        {
            ItemData data = ItemRegistry.Find(itemId);
            if (data == null || qty <= 0) return;
            r.inventory.TryGetValue(itemId, out int current);
            r.inventory[itemId] = Math.Min(data.MaxStack, current + qty);
        }

        // ----- Player lookup (server-side targeting) -----

        /// <summary>Nearest connected avatar to a position (server only).</summary>
        public bool NearestPlayer(Vector3 pos, float maxDist, out NetworkPlayer player, out int conn)
        {
            player = null; conn = -1;
            float best = maxDist * maxDist;
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayer p = players[i];
                if (p == null || p.Owner == null || !p.IsServerInitialized) continue;
                float d = (p.transform.position - pos).sqrMagnitude;
                if (d < best) { best = d; player = p; conn = p.Owner.ClientId; }
            }
            return player != null;
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
