using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Davidmon.Combat;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Inventory;
using Davidmon.Player;
using Davidmon.World;

namespace Davidmon.Multiplayer
{
    /// <summary>
    /// Milestones 11-12 — FishNet identity + authoritative progression mirror.
    ///
    /// Rule 13 documentation:
    /// - Authority: server writes ALL SyncVars below. Clients only send requests
    ///   (ServerRpcs); ServerGameState validates and decides.
    /// - Ownership: the owning client drives movement/input; remote copies are
    ///   visual-only (controller/combat/interaction disabled).
    /// - Synchronization: species/level/exp/coins/inventory replicate as SyncVars.
    ///   The owner mirrors them into PlayerManager/Wallet/PlayerInventory so HUD,
    ///   shop, evolution and saves keep working unchanged.
    /// - Validation: whitelisted sources, amount caps, rate limits, server-side
    ///   prices/funds/stack caps in ServerGameState. Client values are requests.
    /// - Persistence: mirrors stay save-compatible; SaveManager is untouched.
    ///   Join-time snapshot adoption is session trust (documented, once per conn).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        /// <summary>Display name assigned by the server (Player + connection id).</summary>
        public readonly SyncVar<string> PlayerName = new SyncVar<string>();

        /// <summary>Active creature species id (server-adopted, then selection-tracked).</summary>
        public readonly SyncVar<string> CreatureId = new SyncVar<string>();

        /// <summary>Authoritative creature level (server only writes).</summary>
        public readonly SyncVar<int> CreatureLevel = new SyncVar<int>();

        /// <summary>Authoritative level progress within the level (server only writes).</summary>
        public readonly SyncVar<long> AuthExp = new SyncVar<long>();

        /// <summary>Authoritative coin balance (server only writes).</summary>
        public readonly SyncVar<long> AuthCoins = new SyncVar<long>();

        /// <summary>Authoritative inventory as "id=count;..." (server only writes).</summary>
        public readonly SyncVar<string> InventoryState = new SyncVar<string>();

        /// <summary>Authoritative current HP (server only writes).</summary>
        public readonly SyncVar<int> AuthHp = new SyncVar<int>();

        /// <summary>Authoritative max HP (server only writes).</summary>
        public readonly SyncVar<int> AuthMaxHp = new SyncVar<int>();

        private ThirdPersonController _controller;
        private CharacterController _characterController;
        private PlayerInputProvider _input;
        private PlayerAvatar _avatar;
        private PlayerManager _manager;
        private AbilityCaster _caster;
        private PlayerInteraction _interaction;

        private string _appliedCreatureId = "";
        private int _appliedLevel = -1;

        private bool _applyingServerState;
        private int _lastAuthLevel = -1;

        /// <summary>
        /// Settled mirror application. SyncVars replicate independently, so a fresh
        /// snapshot arrives as staggered partial states (species first, level/exp/HP
        /// later). Applying each instantly builds transient garbage: a Lv-1 creature,
        /// a 0-HP "faint", even a zeroed wallet. Instead every change schedules one
        /// apply 0.25s out, which runs only once the snapshot reads complete.
        /// </summary>
        private float _mirrorPendingAt = -1f;
        private const float MirrorSettleSeconds = 0.25f;
        private int _lastMirroredHp = -1;
        private long _lastAuthExpMirror = -1;

        private void Awake()
        {
            _controller = GetComponent<ThirdPersonController>();
            _characterController = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputProvider>();
            _avatar = GetComponent<PlayerAvatar>();
            _manager = GetComponent<PlayerManager>();
            _caster = GetComponent<AbilityCaster>();
            _interaction = GetComponent<PlayerInteraction>();

            PlayerName.OnChange += OnPlayerNameChanged;
            CreatureId.OnChange += OnCreatureSyncChanged;
            CreatureLevel.OnChange += OnLevelSyncChanged;
            AuthExp.OnChange += OnAuthExpChanged;
            AuthCoins.OnChange += OnAuthCoinsChanged;
            InventoryState.OnChange += OnInventoryStateChanged;
            AuthHp.OnChange += OnAuthHpChanged;
            AuthMaxHp.OnChange += OnAuthHpChanged;
        }

        private void OnDestroy()
        {
            PlayerName.OnChange -= OnPlayerNameChanged;
            CreatureId.OnChange -= OnCreatureSyncChanged;
            CreatureLevel.OnChange -= OnLevelSyncChanged;
            AuthExp.OnChange -= OnAuthExpChanged;
            AuthCoins.OnChange -= OnAuthCoinsChanged;
            InventoryState.OnChange -= OnInventoryStateChanged;
            AuthHp.OnChange -= OnAuthHpChanged;
            AuthMaxHp.OnChange -= OnAuthHpChanged;

            if (ServerApi.Local == this) ServerApi.Local = null;
            if (IsOwner)
            {
                GameEvents.CreatureSelected -= OnLocalProgressionChanged;
                GameEvents.CreatureEvolved -= OnLocalEvolved;
                GameEvents.LevelUp -= OnLocalLevelUp;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            PlayerName.Value = "Player" + Owner.ClientId;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            EnemyNetwork.EnsureClientHandler();

            if (IsOwner)
            {
                // This is the local player: claim the shared services (a remote
                // avatar may have registered itself first) and drive the camera.
                if (_input != null) PlayerInputProvider.Local = _input;
                if (_manager != null) ServiceLocator.Register(_manager);
                if (_caster != null) ServiceLocator.Register(_caster);
                ServerApi.Local = this;

                ThirdPersonCamera cam = FindAnyObjectByType<ThirdPersonCamera>();
                if (cam != null) cam.SetTarget(transform);

                gameObject.tag = "Player";

                GameEvents.CreatureSelected += OnLocalProgressionChanged;
                GameEvents.CreatureEvolved += OnLocalEvolved;
                GameEvents.LevelUp += OnLocalLevelUp;

                PushCreatureToServer();
                SeedFromStash();
                ApplySavedTransform();
                ApplyServerProgression();
                ApplyServerCoins();
                ApplyServerInventory();
                ApplyServerHp();
            }
            else
            {
                // Remote avatar: visual only. The input provider stays enabled on
                // purpose — the InputActionAsset is shared, so disabling it would
                // kill the local player's input as well.
                if (_controller != null) _controller.enabled = false;
                if (_characterController != null) _characterController.enabled = false;
                if (_caster != null) _caster.enabled = false;
                if (_interaction != null) _interaction.enabled = false;

                RestoreLocalServices();
                ApplyCreatureVisual(CreatureId.Value, CreatureLevel.Value);
            }
        }

        /// <summary>
        /// Re-points the shared services at the locally owned player. Called when a
        /// remote avatar spawns, since its PlayerManager/AbilityCaster register
        /// themselves in Awake and would otherwise hijack the local HUD/combat.
        /// </summary>
        private void RestoreLocalServices()
        {
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null || !players[i].IsOwner) continue;
                if (players[i]._manager != null) ServiceLocator.Register(players[i]._manager);
                if (players[i]._caster != null) ServiceLocator.Register(players[i]._caster);
                if (players[i]._input != null) PlayerInputProvider.Local = players[i]._input;
                return;
            }
        }

        private void OnPlayerNameChanged(string prev, string next, bool asServer)
        {
            gameObject.name = string.IsNullOrEmpty(next) ? "Player" : "Player_" + next;
        }

        private void OnCreatureSyncChanged(string prev, string next, bool asServer)
        {
            if (!IsOwner) ApplyCreatureVisual(next, CreatureLevel.Value);
        }

        private void OnLevelSyncChanged(int prev, int next, bool asServer)
        {
            if (!IsOwner) ApplyCreatureVisual(CreatureId.Value, next);
            else ScheduleMirrorApply();
        }

        private void OnAuthExpChanged(long prev, long next, bool asServer)
        {
            if (IsOwner) ScheduleMirrorApply();
        }

        private void OnAuthCoinsChanged(long prev, long next, bool asServer)
        {
            if (IsOwner) ScheduleMirrorApply();
        }

        private void OnInventoryStateChanged(string prev, string next, bool asServer)
        {
            if (IsOwner) ScheduleMirrorApply();
        }

        private void OnAuthHpChanged(int prev, int next, bool asServer)
        {
            if (IsOwner) ScheduleMirrorApply();
        }

        /// <summary>
        /// Mirrors authoritative (level, exp) into the local creature, preserving
        /// HP ratio and raising the same notifications/events as a local level-up.
        /// Skipped until the snapshot reads complete (see <see cref="ScheduleMirrorApply"/>).
        /// </summary>
        private void ApplyServerProgression()
        {
            if (!IsOwner || _applyingServerState || _manager == null) return;
            if (!IsSnapshotComplete()) return;
            CreatureData species = CreatureRegistry.Find(CreatureId.Value);
            if (species == null) return;

            int level = Mathf.Max(1, CreatureLevel.Value);
            long exp = Math.Max(0L, AuthExp.Value);

            if (!_manager.HasCreature)
                _manager.SetCreature(species, 1);

            CreatureInstance active = _manager.ActiveCreature;
            if (active == null) return;

            bool speciesChanged = active.Data == null || active.Data.CreatureId != species.CreatureId;
            if (!speciesChanged && active.Level == level && active.Exp == exp && _lastAuthLevel == level)
                return;

            _applyingServerState = true;
            try
            {
                int evoBefore = _manager.AvailableEvolutionCount();
                int hpPct = active.MaxHp > 0
                    ? Mathf.Clamp(Mathf.RoundToInt((float)active.CurrentHp / active.MaxHp * 100f), 0, 100)
                    : 0;
                active.LoadState(species, level, exp, hpPct);
                _manager.RaiseHpChanged();

                if (_lastAuthLevel >= 0 && level > _lastAuthLevel)
                {
                    GameEvents.RaiseLevelUp(0, level, active.MaxHp);
                    GameEvents.RaiseShowNotification(species.DisplayName + " reached Lv." + level + "!");
                    if (_manager.AvailableEvolutionCount() > evoBefore)
                        GameEvents.RaiseShowLegendaryNotification(species.DisplayName
                            + " can evolve now! Open the Evolutions menu.");
                }
                _lastAuthLevel = level;

                if (speciesChanged && _avatar != null && _avatar.CurrentData != species)
                    _avatar.SetCreature(species);
            }
            finally { _applyingServerState = false; }

            // Mirror ticks (e.g. passive EXP) change progression without any local
            // event — repaint UI the same way a local gain would, or bars freeze.
            long prevMirrorExp = _lastAuthExpMirror;
            _lastAuthExpMirror = exp;
            if (prevMirrorExp >= 0 && exp != prevMirrorExp)
                GameEvents.RaiseExpGained(0, exp - prevMirrorExp, exp);

            ApplyServerHp();
        }

        /// <summary>Mirrors authoritative HP into the local creature + HUD.</summary>
        private void ApplyServerHp()
        {
            if (!IsOwner || _applyingServerState || _manager == null || !_manager.HasCreature) return;
            if (!IsSnapshotComplete()) return;
            int hp = AuthHp.Value;
            int maxHp = Mathf.Max(1, AuthMaxHp.Value);
            // SyncVars default to 0 before the first server push; ignore until set.
            if (maxHp <= 1 && hp <= 0) return;
            _applyingServerState = true;
            try
            {
                _manager.ApplyServerHp(hp, maxHp);
                // Toast only on a real alive -> fainted transition, never on join
                // or re-application of an already-fainted state.
                if (hp <= 0 && _lastMirroredHp > 0)
                    GameEvents.RaiseShowNotification("Your creature fainted!");
                _lastMirroredHp = hp;
            }
            finally { _applyingServerState = false; }
        }

        /// <summary>Schedules one settled mirror apply (see _mirrorPendingAt).</summary>
        private void ScheduleMirrorApply()
        {
            if (!IsOwner) return;
            _mirrorPendingAt = Time.time + MirrorSettleSeconds;
        }

        /// <summary>
        /// True once the server snapshot has fully arrived. The server always writes
        /// CreatureLevel >= 1 and AuthMaxHp >= 1, so 0/empty means "not replicated
        /// yet" rather than a real value.
        /// </summary>
        private bool IsSnapshotComplete()
        {
            return !string.IsNullOrEmpty(CreatureId.Value)
                && CreatureLevel.Value >= 1
                && AuthMaxHp.Value >= 1;
        }

        private void Update()
        {
            if (!IsOwner || _mirrorPendingAt < 0f || Time.time < _mirrorPendingAt) return;
            _mirrorPendingAt = -1f;
            if (!IsSnapshotComplete())
            {
                // Stragglers still in flight — retry shortly.
                _mirrorPendingAt = Time.time + MirrorSettleSeconds;
                return;
            }
            ApplyServerProgression();
            ApplyServerHp();
            ApplyServerCoins();
            ApplyServerInventory();
        }

        /// <summary>
        /// Mirrors the authoritative coin balance into the local wallet. Gated on
        /// snapshot completeness so a default 0 never wipes pre-join coins
        /// (which would also dirty a bad autosave).
        /// </summary>
        private void ApplyServerCoins()
        {
            if (!IsOwner || _applyingServerState) return;
            if (!IsSnapshotComplete()) return;
            Wallet wallet = ServiceLocator.GetOrCreate(() => new Wallet());
            if (wallet.Coins != AuthCoins.Value)
                wallet.SetCoins(AuthCoins.Value);
        }

        /// <summary>
        /// Mirrors the authoritative inventory into the local inventory. Gated on
        /// snapshot completeness so a default empty state never wipes pre-join items.
        /// </summary>
        private void ApplyServerInventory()
        {
            if (!IsOwner || _applyingServerState) return;
            if (!IsSnapshotComplete()) return;
            PlayerInventory inventory = ServiceLocator.GetOrCreate(() => new PlayerInventory());
            inventory.RestoreAll(ServerGameState.ParseInventory(InventoryState.Value));
        }

        /// <summary>Applies the replicated species to the remote avatar visual.</summary>
        private void ApplyCreatureVisual(string creatureId, int level)
        {
            if (string.IsNullOrEmpty(creatureId)) return;
            if (creatureId == _appliedCreatureId && level == _appliedLevel) return;
            CreatureData data = CreatureRegistry.Find(creatureId);
            if (data == null) return;

            _appliedCreatureId = creatureId;
            _appliedLevel = level;
            if (_avatar != null) _avatar.SetCreature(data);
        }

        private void OnLocalProgressionChanged(string creatureId) => PushCreatureToServer();
        private void OnLocalEvolved(string fromId, string toId) => PushCreatureToServer();
        private void OnLocalLevelUp(int creatureId, long newLevel, long maxHp) => PushCreatureToServer();

        /// <summary>
        /// Instant local seeding from the join snapshot. The authoritative mirror
        /// needs a server round trip + settle (~1s of "No creature"), so display the
        /// stashed scene-player state immediately: it is the same snapshot just sent
        /// via CmdSubmitCreature, and the server adopts it verbatim (session trust),
        /// so the mirror confirms flicker-free. Must run AFTER PushCreatureToServer
        /// (which sends the stash while the manager is still empty); the re-push
        /// triggered by SetCreature below is ignored server-side (baseline once-only).
        /// </summary>
        private void SeedFromStash()
        {
            if (!IsOwner || _manager == null || _manager.HasCreature) return;
            NetworkBootstrap.SceneSnapshot stash = NetworkBootstrap.LastSceneSnapshot;
            if (!stash.valid || string.IsNullOrEmpty(stash.creatureId)) return;
            CreatureData species = CreatureRegistry.Find(stash.creatureId);
            if (species == null) return;

            _applyingServerState = true;
            try
            {
                _manager.SetCreature(species, 1);
                CreatureInstance active = _manager.ActiveCreature;
                // Join state is full HP server-side (AdoptBaseline), so seed full.
                if (active != null)
                    active.LoadState(species, Mathf.Max(1, stash.level), Math.Max(0L, stash.exp), 100);
                _manager.RaiseHpChanged();
                if (_avatar != null && _avatar.CurrentData != species)
                    _avatar.SetCreature(species);
            }
            finally { _applyingServerState = false; }
        }

        /// <summary>
        /// Auto-loads the saved world position on join. Progression/coins/inventory
        /// arrive via the server baseline; the transform is client-local (driven by
        /// the owner and replicated out), so the owner simply resumes where the save
        /// was written. Skipped for fresh games with no save on disk.
        /// </summary>
        private void ApplySavedTransform()
        {
            if (!IsOwner) return;
            Vector3 pos;
            float rotY;
            if (!Davidmon.Save.SaveManager.TryGetSavedTransform(out pos, out rotY)) return;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, rotY, 0f);
        }

        /// <summary>
        /// Sends the owner's snapshot (selection + baseline) to the server.
        /// Fresh avatars spawn creature-less, so when the local manager is empty
        /// the stashed scene-player snapshot (captured at park time) is sent.
        /// </summary>
        private void PushCreatureToServer()
        {
            if (!IsOwner) return;
            if (_manager != null && _manager.HasCreature)
            {
                CreatureData data = _manager.ActiveCreature.Data;
                if (data == null) return;
                Wallet wallet = ServiceLocator.GetOrCreate(() => new Wallet());
                CmdSubmitCreature(data.CreatureId, _manager.ActiveCreature.Level,
                    _manager.ActiveCreature.Exp, wallet.Coins, ServerApi.SnapshotInventory());
                return;
            }
            NetworkBootstrap.SceneSnapshot stash = NetworkBootstrap.LastSceneSnapshot;
            if (stash.valid && !string.IsNullOrEmpty(stash.creatureId))
                CmdSubmitCreature(stash.creatureId, Mathf.Max(1, stash.level),
                    Math.Max(0L, stash.exp), Math.Max(0L, stash.coins), stash.invState ?? "");
        }

        /// <summary>
        /// Owner upload: species selection plus a join-time progression snapshot.
        /// The server adopts numbers ONLY for fresh connections (session trust),
        /// then owns level/exp/coins/inventory and mirrors them back as SyncVars.
        /// </summary>
        [ServerRpc]
        public void CmdSubmitCreature(string creatureId, int level, long exp, long coins, string invState)
        {
            if (ServerGameState.Instance == null) return;
            ServerGameState.Instance.AdoptBaseline(Owner.ClientId, creatureId ?? "", level, exp, coins, invState);
            PushRecordToSyncVars(Owner.ClientId);
        }

        /// <summary>Client EXP request (rule 3: amount is a request, server decides).</summary>
        [ServerRpc]
        public void CmdAwardExp(string source, long amount)
        {
            if (ServerGameState.Instance == null) return;
            if (ServerGameState.Instance.AwardExp(Owner.ClientId, source, amount,
                out int level, out long exp, out int gained, out string error))
            {
                CreatureLevel.Value = level;
                AuthExp.Value = exp;
                PushHpToSyncVars(Owner.ClientId);
            }
            else Debug.LogWarning("[Server] EXP denied for client " + Owner.ClientId + ": " + error);
        }

        /// <summary>Client coin request (rule 3: amount is a request, server decides).</summary>
        [ServerRpc]
        public void CmdAwardCoins(string source, long amount)
        {
            if (ServerGameState.Instance == null) return;
            if (ServerGameState.Instance.AwardCoins(Owner.ClientId, source, amount, out long coins, out string error))
                AuthCoins.Value = coins;
            else Debug.LogWarning("[Server] Coins denied for client " + Owner.ClientId + ": " + error);
        }

        /// <summary>Client item-grant request (boss/quest drops flow through here).</summary>
        [ServerRpc]
        public void CmdGrantItem(string source, string itemId, int qty)
        {
            if (ServerGameState.Instance == null) return;
            if (ServerGameState.Instance.GrantItem(Owner.ClientId, source, itemId, qty, out string error))
                PushInventoryToSyncVars(Owner.ClientId);
            else Debug.LogWarning("[Server] Item denied for client " + Owner.ClientId + ": " + error);
        }

        /// <summary>Client purchase request. Price/funds/stock all checked server-side.</summary>
        [ServerRpc]
        public void CmdBuyItem(string itemId)
        {
            if (ServerGameState.Instance == null)
            {
                RpcNotice(Owner, "Shop unavailable.");
                return;
            }
            if (ServerGameState.Instance.TryBuy(Owner.ClientId, itemId ?? "",
                out long price, out string receipt, out string error))
            {
                ServerGameState.Record r = ServerGameState.Instance.GetOrCreate(Owner.ClientId);
                AuthCoins.Value = r.coins;
                PushInventoryToSyncVars(Owner.ClientId);
                RpcNotice(Owner, receipt);
            }
            else RpcNotice(Owner, error ?? "Purchase failed.");
        }

        /// <summary>Server-to-client notice (receipts, denials). Shown as a HUD toast.</summary>
        [TargetRpc]
        public void RpcNotice(NetworkConnection target, string message)
        {
            if (!string.IsNullOrEmpty(message))
                GameEvents.RaiseShowNotification(message);
        }

        /// <summary>Client-reported damage intake. Server validates via DamagePlayer.</summary>
        [ServerRpc]
        public void CmdReportDamage(string source, int amount)
        {
            if (ServerGameState.Instance == null) return;
            if (ServerGameState.Instance.DamagePlayer(Owner.ClientId, source ?? "enemy", amount,
                out int hp, out int maxHp, out bool died, out string error))
            {
                PushHpToSyncVars(Owner.ClientId);
                if (died) RpcNotice(Owner, "Your creature fainted!");
            }
            else Debug.LogWarning("[Server] Damage denied for client " + Owner.ClientId + ": " + error);
        }

        /// <summary>Full-heal request (healer NPC). Server validates via HealPlayer.</summary>
        [ServerRpc]
        public void CmdRequestHeal()
        {
            if (ServerGameState.Instance == null) return;
            if (ServerGameState.Instance.HealPlayer(Owner.ClientId,
                out int hp, out int maxHp, out string error))
            {
                PushHpToSyncVars(Owner.ClientId);
                RpcNotice(Owner, "Your creature is fully restored!");
            }
            else RpcNotice(Owner, error ?? "Heal failed.");
        }

        /// <summary>Evolution request. Server validates requirements via TryEvolve.</summary>
        [ServerRpc]
        public void CmdRequestEvolve(string targetId)
        {
            if (ServerGameState.Instance == null) return;
            if (ServerGameState.Instance.TryEvolve(Owner.ClientId, targetId ?? "",
                out string error))
            {
                PushRecordToSyncVars(Owner.ClientId);
                RpcNotice(Owner, "Evolution complete!");
            }
            else RpcNotice(Owner, error ?? "Evolution failed.");
        }

        /// <summary>
        /// Player -&gt; enemy damage. Server owns HP; mirrors to all via broadcast.
        /// Killer rewards are server-computed (DamageEnemyById grants to record).
        /// </summary>
        [ServerRpc]
        public void CmdDamageEnemy(string enemyId, int amount)
        {
            if (ServerGameState.Instance == null) return;
            if (ServerGameState.Instance.DamageEnemyById(Owner.ClientId, enemyId ?? "", amount,
                out int hp, out int maxHp, out bool died,
                out int rewardExp, out long rewardCoins, out string rewardItem, out string error))
            {
                try
                {
                    ServerManager.Broadcast(new EnemyHpBroadcast
                    {
                        EnemyId = enemyId,
                        Hp = hp,
                        MaxHp = maxHp,
                        Died = died
                    });
                }
                catch (Exception e) { Debug.LogWarning("[Server] Enemy broadcast failed: " + e.Message); }
                // Killer rewards were granted directly to the record; push immediately
                // (the periodic sync in ServerGameState.Update is the fallback).
                ServerGameState.Record r = ServerGameState.Instance.GetOrCreate(Owner.ClientId);
                CreatureLevel.Value = Mathf.Max(1, r.level);
                AuthExp.Value = Math.Max(0L, r.exp);
                AuthCoins.Value = Math.Max(0L, r.coins);
                PushInventoryToSyncVars(Owner.ClientId);
                PushHpToSyncVars(Owner.ClientId);
                if (died)
                    RpcNotice(Owner, "Enemy defeated! +" + rewardExp + " Exp, +" + rewardCoins + " coins"
                        + (!string.IsNullOrEmpty(rewardItem) ? ", drop: " + rewardItem : ""));
            }
            else Debug.LogWarning("[Server] Enemy damage denied for client " + Owner.ClientId + ": " + error);
        }

        private void PushRecordToSyncVars(int clientId)
        {
            if (ServerGameState.Instance == null) return;
            ServerGameState.Record r = ServerGameState.Instance.GetOrCreate(clientId);
            CreatureId.Value = r.creatureId ?? "";
            CreatureLevel.Value = Mathf.Max(1, r.level);
            AuthExp.Value = Math.Max(0L, r.exp);
            AuthCoins.Value = Math.Max(0L, r.coins);
            PushInventoryToSyncVars(clientId);
            PushHpToSyncVars(clientId);
        }

        private void PushHpToSyncVars(int clientId)
        {
            if (ServerGameState.Instance == null) return;
            ServerGameState.Record r = ServerGameState.Instance.GetOrCreate(clientId);
            AuthMaxHp.Value = Mathf.Max(1, ServerGameState.MaxHpFor(r.creatureId, r.level));
            AuthHp.Value = Mathf.Clamp(r.currentHp, 0, AuthMaxHp.Value);
        }

        private void PushInventoryToSyncVars(int clientId)
        {
            if (ServerGameState.Instance == null) return;
            ServerGameState.Record r = ServerGameState.Instance.GetOrCreate(clientId);
            InventoryState.Value = ServerGameState.Instance.SerializeInventory(r);
        }
    }
}
