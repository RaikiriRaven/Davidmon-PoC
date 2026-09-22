using System;
using Davidmon.NPC;

namespace Davidmon.Core
{
    /// <summary>
    /// Central event bus for gameplay communication.
    /// Systems raise events here and listeners subscribe — keeping gameplay systems
    /// decoupled from one another and from the UI.
    /// </summary>
    public static class GameEvents
    {
        // ----- Creature / Progression -----
        public static event Action<string> CreatureSelected;
        public static event Action<string> CreatureSpawned;
        public static event Action<int, long, long> ExpGained;            // creatureId, gained, total
        public static event Action<int, long, long> LevelUp;              // creatureId, newLevel, maxHp
        public static event Action<string, string> CreatureEvolved;       // fromId, toId

        // ----- Economy -----
        public static event Action<long, long> CurrencyChanged;           // oldAmount, newAmount
        public static event Action<string, int> ItemAdded;                // itemId, quantity
        public static event Action<string, int> ItemRemoved;              // itemId, quantity
        public static event Action<string> InventoryChanged;

        // ----- Combat -----
        public static event Action<string, string, int> DamageDealt;      // attackerId, targetId, amount
        public static event Action<string> TargetDied;                    // defeated id
        public static event Action<string> CombatStarted;
        public static event Action<string> CombatEnded;
        public static event Action<int, int> CreatureHpChanged;           // currentHp, maxHp (active creature)
        public static event Action<int> AbilitySelected;                  // 0-based ability slot index
        public static event Action<string> EnemyDefeated;                 // enemyId

        // ----- World / Encounters -----
        public static event Action<string> AreaEntered;                   // areaId
        public static event Action<string> EnemySpawned;                  // enemyId
        public static event Action<string> EnemyEngaged;                  // enemyId
        public static event Action<string> BossDefeated;

        // ----- Dialogue -----
        public static event Action<NpcData> DialogueOpened;
        public static event Action<NpcData, int> DialogueLineChanged;              // npc, 0-based line index
        public static event Action<NpcData> DialogueChoicesShown;
        public static event Action<NpcData, DialogueChoice> DialogueChoiceSelected;
        public static event Action<NpcData, string> DialogueReplyShown;            // npc, reply text
        public static event Action<NpcData> DialogueClosed;

        // ----- UI / Input -----
        public static event Action<bool> ChatInputChanged;                // isChatActive
        public static event Action<string> ShowNotification;              // message
        public static event Action<string> ShowLegendaryNotification;     // message
        public static event Action<string> InteractPrompt;                // prompt text or null to hide
        public static event Action EscapeRequested;                        // raised when Esc is pressed over an open modal

        // ----- Lifecycle -----
        public static event Action GameWorldReady;

        // ----- Channel invocation helpers -----
        public static void RaiseCreatureSelected(string creatureId) => CreatureSelected?.Invoke(creatureId);
        public static void RaiseCreatureSpawned(string creatureId) => CreatureSpawned?.Invoke(creatureId);
        public static void RaiseExpGained(int creatureId, long gained, long total) => ExpGained?.Invoke(creatureId, gained, total);
        public static void RaiseLevelUp(int creatureId, int newLevel, long maxHp) => LevelUp?.Invoke(creatureId, newLevel, maxHp);
        public static void RaiseCreatureEvolved(string fromId, string toId) => CreatureEvolved?.Invoke(fromId, toId);

        public static void RaiseCurrencyChanged(long oldAmount, long newAmount) => CurrencyChanged?.Invoke(oldAmount, newAmount);
        public static void RaiseItemAdded(string itemId, int quantity) => ItemAdded?.Invoke(itemId, quantity);
        public static void RaiseItemRemoved(string itemId, int quantity) => ItemRemoved?.Invoke(itemId, quantity);
        public static void RaiseInventoryChanged() => InventoryChanged?.Invoke(null);

        public static void RaiseDamageDealt(string attackerId, string targetId, int amount) => DamageDealt?.Invoke(attackerId, targetId, amount);
        public static void RaiseTargetDied(string defeatedId) => TargetDied?.Invoke(defeatedId);
        public static void RaiseCombatStarted(string targetId) => CombatStarted?.Invoke(targetId);
        public static void RaiseCombatEnded(string targetId) => CombatEnded?.Invoke(targetId);
        public static void RaiseCreatureHpChanged(int currentHp, int maxHp) => CreatureHpChanged?.Invoke(currentHp, maxHp);
        public static void RaiseAbilitySelected(int slotIndex) => AbilitySelected?.Invoke(slotIndex);
        public static void RaiseEnemyDefeated(string enemyId) => EnemyDefeated?.Invoke(enemyId);

        public static void RaiseAreaEntered(string areaId) => AreaEntered?.Invoke(areaId);
        public static void RaiseEnemySpawned(string enemyId) => EnemySpawned?.Invoke(enemyId);
        public static void RaiseEnemyEngaged(string enemyId) => EnemyEngaged?.Invoke(enemyId);
        public static void RaiseBossDefeated() => BossDefeated?.Invoke(null);

        public static void RaiseDialogueOpened(NpcData npc) => DialogueOpened?.Invoke(npc);
        public static void RaiseDialogueLineChanged(NpcData npc, int index) => DialogueLineChanged?.Invoke(npc, index);
        public static void RaiseDialogueChoicesShown(NpcData npc) => DialogueChoicesShown?.Invoke(npc);
        public static void RaiseDialogueChoiceSelected(NpcData npc, DialogueChoice choice) => DialogueChoiceSelected?.Invoke(npc, choice);
        public static void RaiseDialogueReplyShown(NpcData npc, string text) => DialogueReplyShown?.Invoke(npc, text);
        public static void RaiseDialogueClosed(NpcData npc) => DialogueClosed?.Invoke(npc);

        public static void RaiseChatInputChanged(bool active) => ChatInputChanged?.Invoke(active);
        public static void RaiseShowNotification(string message) => ShowNotification?.Invoke(message);
        public static void RaiseShowLegendaryNotification(string message) => ShowLegendaryNotification?.Invoke(message);
        public static void RaiseInteractPrompt(string prompt) => InteractPrompt?.Invoke(prompt);
        public static void RaiseEscapeRequested() => EscapeRequested?.Invoke();

        public static void RaiseGameWorldReady() => GameWorldReady?.Invoke();
    }
}