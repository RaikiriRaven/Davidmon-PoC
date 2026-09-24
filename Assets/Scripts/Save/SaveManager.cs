using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Inventory;
using Davidmon.Player;

namespace Davidmon.Save
{
    /// <summary>
    /// Milestone 11: JSON save/load to a file under Application.persistentDataPath.
    /// Stores coins, the inventory, the active creature's progression and the player's
    /// world position. Saves on F5, autosaves on an interval once anything
    /// noteworthy changed, saves when a boss is dropped, and saves on quit so progress
    /// is never lost. The file is parsed in Awake so the starter-selection UI can gate
    /// itself on <see cref="HasSave"/> before any Start runs. There is no manual load:
    /// the save auto-applies at scene start (offline) and again on network join
    /// (avatar baseline + spawn position).
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        public const int Version = 1;

        private const string FileName = "davidmon_save.json";
        private const string BackupName = "davidmon_save.json.tmp";
        private const float AutoSaveInterval = 30f;

        /// <summary>True when a readable save exists on disk (parsed in Awake).</summary>
        public static bool HasSave { get; private set; }

        private static SaveData _loaded;

        private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        private static string TempPath => Path.Combine(Application.persistentDataPath, BackupName);

        private float _autoSaveTimer = AutoSaveInterval;
        private bool _dirty;
        private PlayerManager _pm;

        private void Awake()
        {
            _loaded = ReadFromDisk();
            HasSave = _loaded != null;
            ServiceLocator.Register(this);
        }

        private void OnEnable()
        {
            GameEvents.BossDefeated += OnBossDefeated;
            GameEvents.LevelUp += OnLevelUp;
            GameEvents.CreatureHpChanged += OnHpChanged;
            GameEvents.ItemAdded += OnInventoryChanged;
            GameEvents.ItemRemoved += OnInventoryChanged;
            GameEvents.CurrencyChanged += OnWalletChanged;
        }

        private void OnDisable()
        {
            GameEvents.BossDefeated -= OnBossDefeated;
            GameEvents.LevelUp -= OnLevelUp;
            GameEvents.CreatureHpChanged -= OnHpChanged;
            GameEvents.ItemAdded -= OnInventoryChanged;
            GameEvents.ItemRemoved -= OnInventoryChanged;
            GameEvents.CurrencyChanged -= OnWalletChanged;
        }

        private void OnDestroy()
        {
            if (ServiceLocator.Get<SaveManager>() == this)
                ServiceLocator.Unregister<SaveManager>();
        }

        private void Start()
        {
            ResolveManager();
            if (HasSave) ApplyLoaded(_loaded);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[Key.F5].wasPressedThisFrame)
                {
                    SaveNow();
                    GameEvents.RaiseShowNotification("Game saved.");
                }
            }

            if (!_dirty) return;
            _autoSaveTimer -= Time.deltaTime;
            if (_autoSaveTimer <= 0f)
            {
                _autoSaveTimer = AutoSaveInterval;
                SaveNow();
            }
        }

        private void OnApplicationQuit() => SaveNowGeometryOnlyIfUsable();
        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveNowGeometryOnlyIfUsable();
        }

        // The quit path must not throw if play is torn down mid-frame.
        private void SaveNowGeometryOnlyIfUsable()
        {
            try { SaveNow(); }
            catch (Exception e) { Debug.LogWarning("[SaveManager] Quit save skipped: " + e.Message); }
        }

        private void OnBossDefeated(string _) => SaveNow();
        private void OnLevelUp(int _a, long _b, long _c) => MarkDirty();
        private void OnHpChanged(int _hp, int _maxHp) => MarkDirty();
        private void OnInventoryChanged(string _id, int _count) => MarkDirty();
        private void OnWalletChanged(long _old, long _new) => MarkDirty();

        private void MarkDirty()
        {
            _dirty = true;
            _autoSaveTimer = AutoSaveInterval;
        }

        /// <summary>Forces an immediate save and reports success through the HUD.</summary>
        public void QuickSave()
        {
            bool ok = TrySave();
            GameEvents.RaiseShowNotification(ok ? "Game saved." : "Save failed.");
        }

        private bool TrySave()
        {
            try
            {
                SaveNow();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveManager] Save failed: " + e.Message);
                return false;
            }
        }

        public void SaveNow()
        {
            ResolveManager();

            // Never persist a parked/inactive avatar (e.g. teardown ordering on
            // quit while hosting): its progression is stale next to the mirror.
            bool pmUsable = _pm != null && _pm.gameObject.activeInHierarchy;

            var data = new SaveData
            {
                version = Version,
                timestamp = DateTime.UtcNow.ToString("o"),
                coins = ServiceLocator.GetOrCreate(() => new Wallet()).Coins
            };

            if (pmUsable && _pm.HasCreature)
            {
                CreatureInstance c = _pm.ActiveCreature;
                data.activeCreature = new CreatureSave
                {
                    creatureId = c.Data != null ? c.Data.CreatureId : "",
                    level = c.Level,
                    exp = c.Exp,
                    hpPercent = c.MaxHp > 0 && c.CurrentHp >= c.MaxHp ? 0
                        : Mathf.Clamp(Mathf.RoundToInt((float)c.CurrentHp / c.MaxHp * 100f), 1, 100)
                };
            }

            var inventory = ServiceLocator.GetOrCreate(() => new PlayerInventory());
            data.inventory = new List<ItemStackSave>();
            foreach (KeyValuePair<string, int> stack in inventory.Items)
                data.inventory.Add(new ItemStackSave { itemId = stack.Key, count = stack.Value });

            if (pmUsable)
            {
                data.posX = _pm.transform.position.x;
                data.posY = _pm.transform.position.y;
                data.posZ = _pm.transform.position.z;
                data.rotY = _pm.transform.eulerAngles.y;
            }
            else if (_loaded != null)
            {
                // Keep the last known good transform instead of zeroing it.
                data.posX = _loaded.posX;
                data.posY = _loaded.posY;
                data.posZ = _loaded.posZ;
                data.rotY = _loaded.rotY;
                if (data.activeCreature == null) data.activeCreature = _loaded.activeCreature;
            }

            WriteToDisk(data);
            _loaded = data;
            HasSave = true;
            _dirty = false;
        }

        private void ApplyLoaded(SaveData data)
        {
            if (data == null) return;
            ResolveManager();

            var wallet = ServiceLocator.GetOrCreate(() => new Wallet());
            wallet.SetCoins(data.coins);

            var inventory = ServiceLocator.GetOrCreate(() => new PlayerInventory());
            var stacks = new List<KeyValuePair<string, int>>();
            if (data.inventory != null)
                foreach (ItemStackSave stack in data.inventory)
                    stacks.Add(new KeyValuePair<string, int>(stack.itemId, stack.count));
            inventory.RestoreAll(stacks);

            if (_pm != null && data.activeCreature != null)
            {
                CreatureData species = CreatureRegistry.Find(data.activeCreature.creatureId);
                if (species != null)
                {
                    var instance = new CreatureInstance();
                    instance.LoadState(species,
                        Mathf.Max(1, data.activeCreature.level),
                        Math.Max(0L, data.activeCreature.exp),
                        data.activeCreature.hpPercent);
                    _pm.LoadCreature(instance);
                }
            }

            if (_pm != null)
            {
                Vector3 position = new Vector3(data.posX, data.posY, data.posZ);
                if (position.sqrMagnitude > 0.0001f)
                    _pm.transform.position = position;
                _pm.transform.rotation = Quaternion.Euler(0f, data.rotY, 0f);
            }

            GameEvents.RaiseGameWorldReady();
        }

        /// <summary>
        /// Prefers the locally controlled avatar (multiplayer-safe) so saves follow
        /// the owned mirror instead of a parked scene player.
        /// </summary>
        private void ResolveManager()
        {
            _pm = Davidmon.Multiplayer.PlayerLookup.LocalManager();
            if (_pm == null) _pm = ServiceLocator.Get<PlayerManager>();
            if (_pm == null) _pm = FindFirstObjectByType<PlayerManager>();
        }

        private static void WriteToDisk(SaveData data)
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(TempPath, json);
            // Copy-overwrite (not delete+move): a crash mid-write keeps the last
            // good save instead of losing everything.
            File.Copy(TempPath, SavePath, true);
            try { File.Delete(TempPath); } catch (Exception) { }
        }

        /// <summary>
        /// Reads the saved world transform fresh from disk (for network join).
        /// Returns false when there is no save or no meaningful position stored.
        /// </summary>
        public static bool TryGetSavedTransform(out Vector3 position, out float rotY)
        {
            position = Vector3.zero;
            rotY = 0f;
            SaveData data = ReadFromDisk();
            if (data == null) return false;
            position = new Vector3(data.posX, data.posY, data.posZ);
            if (position.sqrMagnitude <= 0.0001f) return false;
            rotY = data.rotY;
            return true;
        }

        private static SaveData ReadFromDisk()
        {
            try
            {
                string path = File.Exists(SavePath) ? SavePath
                    : File.Exists(TempPath) ? TempPath
                    : null;
                if (path == null) return null;
                string json = File.ReadAllText(path);
                return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveManager] Could not read save: " + e.Message);
                return null;
            }
        }
    }
}