using UnityEditor;
using UnityEngine;
using Davidmon.NPC;

namespace Davidmon.EditorTools
{
    /// <summary>
    /// One-shot generator for the starter-village NPCs. Creates the four prototype
    /// characters (Elder, Merchant Marko, Healer Lin, Trainer Vik) under
    /// ScriptableObjects/NPCs. Safe to re-run — it deletes and regenerates each asset
    /// so the data stays in sync with the code. All content is original placeholder.
    /// </summary>
    public static class NpcDataBuilder
    {
        private const string NpcDataDir = "Assets/ScriptableObjects/NPCs";

        [MenuItem("Davidmon/Data/Rebuild NPC Prototype Data")]
        public static void RebuildAll()
        {
            EnsureFolder(NpcDataDir);

            MakeNpc("npc_elder", "Village Elder", "Elder of Sunderglen",
                new Color(0.13f, 0.35f, 0.30f), new Color(0.95f, 0.80f, 0.65f), true,
                "Talk", 3.5f, false,
                new[]
                {
                    new DialogueLine("Welcome to Sunderglen, traveler. Sit a while."),
                    new DialogueLine("These wild creatures share the land; they can be fought, but never caught. Train what you begin with."),
                    new DialogueLine("The ruins to the east hold a power this village cannot contain. Grow strong before you face it."),
                },
                new[]
                {
                    new DialogueChoice("Tell me about the wilds", DialogueAction.None,
                        "Every creature holds a spark of the wild. Befriend one, grow with it, and it will never forsake you."),
                    new DialogueChoice("I will remember that.", DialogueAction.None),
                });

            MakeNpc("npc_marko", "Marko", "Merchant",
                new Color(0.45f, 0.30f, 0.16f), new Color(0.90f, 0.78f, 0.66f), false,
                "Talk", 3.5f, false,
                new[]
                {
                    new DialogueLine("Need supplies? I trade in odds and ends from the road."),
                },
                new[]
                {
                    new DialogueChoice("Browse your wares", DialogueAction.Shop,
                        "Apologies — I haven't unpacked the cart. Come back when the market opens."),
                    new DialogueChoice("Not today.", DialogueAction.None),
                });

            MakeNpc("npc_lin", "Lin", "Village Healer",
                new Color(0.90f, 0.93f, 0.88f), new Color(0.96f, 0.82f, 0.68f), false,
                "Talk", 3.5f, true,
                new[]
                {
                    new DialogueLine("Let me see your creature. A tired partner is no partner at all."),
                },
                new[]
                {
                    new DialogueChoice("Heal my creature", DialogueAction.Heal,
                        "There we go — hale and whole once more."),
                    new DialogueChoice("Just talking.", DialogueAction.None, "Take care out there, friend."),
                });

            MakeNpc("npc_vik", "Vik", "Battle Trainer",
                new Color(0.55f, 0.18f, 0.16f), new Color(0.93f, 0.80f, 0.68f), false,
                "Talk", 3.5f, false,
                new[]
                {
                    new DialogueLine("The wild creatures here will test you. Stand your ground."),
                },
                new[]
                {
                    new DialogueChoice("Any advice?", DialogueAction.None,
                        "Watch their tells and time your strikes. Victory favors the patient."),
                    new DialogueChoice("Goodbye.", DialogueAction.None),
                });

            AssetDatabase.SaveAssets();
            Debug.Log("[NpcDataBuilder] NPC prototype data rebuilt.");
        }

        private static void MakeNpc(string id, string displayName, string title,
            Color robe, Color skin, bool hat, string hint, float radius, bool healer,
            DialogueLine[] lines, DialogueChoice[] choices)
        {
            string path = NpcDataDir + "/" + id + ".asset";
            if (AssetDatabase.LoadAssetAtPath<NpcData>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var npc = ScriptableObject.CreateInstance<NpcData>();
            AssetDatabase.CreateAsset(npc, path);

            var so = new SerializedObject(npc);
            so.FindProperty("npcId").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("title").stringValue = title;
            so.FindProperty("description").stringValue = displayName + " \u2014 " + title;
            so.FindProperty("robeColor").colorValue = robe;
            so.FindProperty("skinColor").colorValue = skin;
            so.FindProperty("hasHat").boolValue = hat;
            so.FindProperty("interactionHint").stringValue = hint;
            so.FindProperty("interactRadius").floatValue = radius;
            so.FindProperty("isHealer").boolValue = healer;

            SetLines(so, "introLines", lines);
            SetChoices(so, "choices", choices);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(npc);
        }

        private static void SetLines(SerializedObject so, string property, DialogueLine[] lines)
        {
            var p = so.FindProperty(property);
            p.arraySize = lines == null ? 0 : lines.Length;
            for (int i = 0; i < p.arraySize; i++)
            {
                var el = p.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("speaker").stringValue = lines[i].Speaker;
                el.FindPropertyRelative("text").stringValue = lines[i].Text;
            }
        }

        private static void SetChoices(SerializedObject so, string property, DialogueChoice[] choices)
        {
            var p = so.FindProperty(property);
            p.arraySize = choices == null ? 0 : choices.Length;
            for (int i = 0; i < p.arraySize; i++)
            {
                var el = p.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("label").stringValue = choices[i].Label;
                el.FindPropertyRelative("action").enumValueIndex = (int)choices[i].Action;
                el.FindPropertyRelative("reply").stringValue = choices[i].Reply;
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}