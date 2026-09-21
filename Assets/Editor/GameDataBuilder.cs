using UnityEditor;
using UnityEngine;
using Davidmon.Creatures;

namespace Davidmon.EditorTools
{
    /// <summary>
    /// One-shot data generator for the prototype. Creates every placeholder creature
    /// prefab, ability asset, species asset and the catalog. Safe to re-run — it
    /// deletes and regenerates each asset so the data stays in sync with the code.
    /// All content is original placeholder material.
    /// </summary>
    public static class GameDataBuilder
    {
        private const string CreaturePrefabDir = "Assets/Prefabs/Creatures";
        private const string CreatureDataDir = "Assets/ScriptableObjects/Creatures";
        private const string AbilityDataDir = "Assets/ScriptableObjects/Abilities";
        private const string ElementMatDir = "Assets/Materials/Creatures";

        [MenuItem("Davidmon/Data/Rebuild All Prototype Data")]
        public static void RebuildAll()
        {
            BuildElementMaterials();
            BuildCreaturePrefabs();
            BuildAbilities();
            BuildCreatures();
            BuildCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log("[GameDataBuilder] All prototype data rebuilt.");
        }

        private static void BuildElementMaterials()
        {
            EnsureFolder(ElementMatDir);
            CreateMaterial(ElementMatDir + "/Mat_Fire.mat", new Color(0.92f, 0.35f, 0.15f));
            CreateMaterial(ElementMatDir + "/Mat_Water.mat", new Color(0.15f, 0.45f, 0.92f));
            CreateMaterial(ElementMatDir + "/Mat_Nature.mat", new Color(0.25f, 0.75f, 0.35f));
            CreateMaterial(ElementMatDir + "/Mat_Electric.mat", new Color(0.95f, 0.80f, 0.20f));
            CreateMaterial(ElementMatDir + "/Mat_Stone.mat", new Color(0.58f, 0.52f, 0.46f));
            CreateMaterial(ElementMatDir + "/Mat_Neutral.mat", new Color(0.62f, 0.62f, 0.68f));
        }

        private static Shader LitShader()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            return urp != null ? urp : Shader.Find("Standard");
        }

        private static void CreateMaterial(string path, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                if (existing.shader == null || existing.shader.name != LitShader().name)
                    existing.shader = LitShader();
                EditorUtility.SetDirty(existing);
                return;
            }
            var material = new Material(LitShader()) { color = color };
            AssetDatabase.CreateAsset(material, path);
        }

        private static Material ElementMaterial(ElementType element)
        {
            string name;
            switch (element)
            {
                case ElementType.Fire: name = "Mat_Fire.mat"; break;
                case ElementType.Water: name = "Mat_Water.mat"; break;
                case ElementType.Nature: name = "Mat_Nature.mat"; break;
                case ElementType.Electric: name = "Mat_Electric.mat"; break;
                case ElementType.Stone: name = "Mat_Stone.mat"; break;
                default: name = "Mat_Neutral.mat"; break;
            }
            return AssetDatabase.LoadAssetAtPath<Material>(ElementMatDir + "/" + name);
        }

        private static void BuildCreaturePrefabs()
        {
            CreateCreaturePrefab("emberling", ElementType.Fire, 1f, true);
            CreateCreaturePrefab("emberclaw", ElementType.Fire, 1.4f, false);
            CreateCreaturePrefab("infernodrake", ElementType.Fire, 2.2f, false);

            CreateCreaturePrefab("aquaphin", ElementType.Water, 1f, true);
            CreateCreaturePrefab("aquatide", ElementType.Water, 1.4f, false);
            CreateCreaturePrefab("leviathor", ElementType.Water, 2.2f, false);

            CreateCreaturePrefab("leafhorn", ElementType.Nature, 1f, true);
            CreateCreaturePrefab("bramblehart", ElementType.Nature, 1.4f, false);
            CreateCreaturePrefab("verdantaur", ElementType.Nature, 2.2f, false);

            CreateCreaturePrefab("voltling", ElementType.Electric, 0.9f, true);
            CreateCreaturePrefab("voltaranda", ElementType.Electric, 1.3f, false);
            CreateCreaturePrefab("thunderbeast", ElementType.Electric, 2.1f, false);

            CreateCreaturePrefab("stoneback", ElementType.Stone, 1f, true);
            CreateCreaturePrefab("boulderback", ElementType.Stone, 1.4f, false);
            CreateCreaturePrefab("stonecolossus", ElementType.Stone, 2.2f, false);
        }

        private static void CreateCreaturePrefab(string id, ElementType element, float scale, bool hasLegs)
        {
            var root = new GameObject(id);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.55f * scale, 0f);
            body.transform.localScale = new Vector3(1f * scale, 0.9f * scale, 1f * scale);
            body.GetComponent<MeshRenderer>().sharedMaterial = ElementMaterial(element);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.5f * scale, 0.15f * scale);
            head.transform.localScale = Vector3.one * (0.55f * scale);
            head.GetComponent<MeshRenderer>().sharedMaterial = ElementMaterial(element);

            if (hasLegs)
            {
                for (int i = 0; i < 2; i++)
                {
                    var foot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    foot.name = "Foot" + (i + 1);
                    foot.transform.SetParent(root.transform, false);
                    float side = i == 0 ? -1f : 1f;
                    foot.transform.localPosition = new Vector3(side * 0.35f * scale, 0.12f * scale, 0f);
                    foot.transform.localScale = Vector3.one * (0.28f * scale);
                    foot.GetComponent<MeshRenderer>().sharedMaterial = ElementMaterial(element);
                }
            }

            var eyeL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeL.name = "EyeL";
            eyeL.transform.SetParent(head.transform, false);
            eyeL.transform.localPosition = new Vector3(-0.14f, 0.1f, 0.42f);
            eyeL.transform.localScale = Vector3.one * 0.12f;
            eyeL.GetComponent<MeshRenderer>().sharedMaterial = CreateOrGetWhite();

            var eyeR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeR.name = "EyeR";
            eyeR.transform.SetParent(head.transform, false);
            eyeR.transform.localPosition = new Vector3(0.14f, 0.1f, 0.42f);
            eyeR.transform.localScale = Vector3.one * 0.12f;
            eyeR.GetComponent<MeshRenderer>().sharedMaterial = CreateOrGetWhite();

            string path = CreaturePrefabDir + "/" + id + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static Material CreateOrGetWhite()
        {
            string path = ElementMatDir + "/Mat_White.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(LitShader()) { color = new Color(0.95f, 0.95f, 0.95f) };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void BuildAbilities()
        {
            CreateAbility("tackle", "Tackle", "A simple charging hit.", ElementType.Neutral, 20, 0.8f, CastType.Melee, 2.5f, 2.5f, 0f, true);

            CreateAbility("ember_breath", "Ember Breath", "Scorching flames burst forward.", ElementType.Fire, 35, 2.2f, CastType.Cone, 8f, 5f, 0f, false);
            CreateAbility("heat_wave", "Heat Wave", "A rolling ball of superheated air.", ElementType.Fire, 42, 3.0f, CastType.Projectile, 12f, 3f, 16f, false);
            CreateAbility("flame_dash", "Flame Dash", "A blazing lunge that scorches everything nearby.", ElementType.Fire, 38, 2.6f, CastType.Melee, 3.5f, 3.5f, 0f, false);

            CreateAbility("aqua_jet", "Aqua Jet", "A pressurized blast of water.", ElementType.Water, 32, 2.2f, CastType.Projectile, 12f, 2.5f, 18f, false);
            CreateAbility("whirlpool", "Whirlpool", "A spiraling torrent that sweeps the area ahead.", ElementType.Water, 38, 3.2f, CastType.Cone, 7f, 5f, 0f, false);
            CreateAbility("tide_bolt", "Tide Bolt", "A fast sphere of pure tide water.", ElementType.Water, 44, 2.8f, CastType.Projectile, 14f, 2f, 20f, false);

            CreateAbility("leaf_slash", "Leaf Slash", "Razor sharp leaves cut the target.", ElementType.Nature, 30, 2.0f, CastType.Melee, 3f, 3.5f, 0f, false);
            CreateAbility("thorn_volley", "Thorn Volley", "A burst of hardened thorns.", ElementType.Nature, 36, 3.0f, CastType.Projectile, 12f, 2.5f, 15f, false);
            CreateAbility("vine_swipe", "Vine Swipe", "Vines lash out in a wide arc.", ElementType.Nature, 40, 2.8f, CastType.Cone, 5f, 4f, 0f, false);

            CreateAbility("spark_bolt", "Spark Bolt", "A jolt of crackling electricity.", ElementType.Electric, 35, 2.2f, CastType.Hitscan, 16f, 0f, 0f, false);
            CreateAbility("zap_field", "Zap Field", "A static shockwave crackling forward.", ElementType.Electric, 34, 3.0f, CastType.Cone, 6f, 5f, 0f, false);
            CreateAbility("lightning_arc", "Lightning Arc", "A wild arc of chain lightning.", ElementType.Electric, 48, 3.4f, CastType.Projectile, 14f, 2.5f, 22f, false);

            CreateAbility("rock_smash", "Rock Smash", "A heavy boulder slam.", ElementType.Stone, 30, 2.2f, CastType.Melee, 2.5f, 3.5f, 0f, false);
            CreateAbility("rock_toss", "Rock Toss", "A lobbed boulder that tumbles at impact.", ElementType.Stone, 42, 3.0f, CastType.Projectile, 13f, 3f, 14f, false);
            CreateAbility("quake", "Quake", "A ground-shaking tremor ahead.", ElementType.Stone, 40, 3.4f, CastType.Cone, 6f, 6f, 0f, false);
        }

        private static void CreateAbility(string id, string name, string desc, ElementType element, int power,
            float cooldown, CastType castType, float range, float radius, float projectileSpeed, bool basic)
        {
            string path = AbilityDataDir + "/" + id + ".asset";
            EnsureScriptableFolder();
            if (AssetDatabase.LoadAssetAtPath<AbilityData>(path) != null)
                AssetDatabase.DeleteAsset(path);
            var ability = ScriptableObject.CreateInstance<AbilityData>();
            AssetDatabase.CreateAsset(ability, path);

            var so = new SerializedObject(ability);
            so.FindProperty("abilityId").stringValue = id;
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("description").stringValue = desc;
            so.FindProperty("element").enumValueIndex = (int)element;
            so.FindProperty("basePower").intValue = power;
            so.FindProperty("cooldown").floatValue = cooldown;
            so.FindProperty("castType").enumValueIndex = (int)castType;
            so.FindProperty("range").floatValue = range;
            so.FindProperty("radius").floatValue = radius;
            so.FindProperty("projectileSpeed").floatValue = projectileSpeed;
            so.FindProperty("isBasicAttack").boolValue = basic;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ability);
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

        private static void EnsureScriptableFolder()
        {
            EnsureFolder(AbilityDataDir);
        }

        private static void BuildCreatures()
        {
            var emberling = MakeCreature("emberling", "Emberling", "A curious flame spirit born from a campfire spark.",
                ElementType.Fire, new StatBlock(90, 24, 16, 12), new StatBlock(6, 2, 2, 1), true,
                "tackle", "ember_breath", "heat_wave", "flame_dash");
            var emberclaw = MakeCreature("emberclaw", "Emberclaw", "The Emberling's fiery claws have fully ignited.",
                ElementType.Fire, new StatBlock(130, 42, 28, 20), new StatBlock(8, 3, 2, 1), false,
                "tackle", "ember_breath", "heat_wave", "flame_dash");
            var infernodrake = MakeCreature("infernodrake", "InfernoDrake", "A living furnace whose wings scorch the sky.",
                ElementType.Fire, new StatBlock(210, 78, 50, 34), new StatBlock(11, 4, 3, 2), false,
                "tackle", "ember_breath", "heat_wave", "flame_dash");

            var aquaphin = MakeCreature("aquaphin", "Aquaphin", "A playful finling that trails streams of fresh water.",
                ElementType.Water, new StatBlock(100, 20, 18, 14), new StatBlock(7, 2, 2, 1), true,
                "tackle", "aqua_jet", "whirlpool", "tide_bolt");
            var aquatide = MakeCreature("aquatide", "Aquatide", "Its fins now carry the deep pull of the tide.",
                ElementType.Water, new StatBlock(150, 34, 32, 24), new StatBlock(9, 2, 3, 1), false,
                "tackle", "aqua_jet", "whirlpool", "tide_bolt");
            var leviathor = MakeCreature("leviathor", "Leviathor", "Rumored to be an ocean that decided to become a creature.",
                ElementType.Water, new StatBlock(240, 58, 58, 40), new StatBlock(12, 3, 3, 2), false,
                "tackle", "aqua_jet", "whirlpool", "tide_bolt");

            var leafhorn = MakeCreature("leafhorn", "Leafhorn", "A gentle guardian whose antlers bloom with young leaves.",
                ElementType.Nature, new StatBlock(110, 22, 22, 12), new StatBlock(7, 2, 2, 1), true,
                "tackle", "leaf_slash", "thorn_volley", "vine_swipe");
            var bramblehart = MakeCreature("bramblehart", "Bramblehart", "Its antlers have hardened into thorny bramble.",
                ElementType.Nature, new StatBlock(165, 38, 38, 21), new StatBlock(9, 3, 3, 1), false,
                "tackle", "leaf_slash", "thorn_volley", "vine_swipe");
            var verdantaur = MakeCreature("verdantaur", "Verdantaur", "A walking grove that can raise a forest in a single season.",
                ElementType.Nature, new StatBlock(260, 66, 66, 36), new StatBlock(12, 3, 3, 2), false,
                "tackle", "leaf_slash", "thorn_volley", "vine_swipe");

            var voltling = MakeCreature("voltling", "Voltling", "A spark creature that hums with static energy.",
                ElementType.Electric, new StatBlock(85, 26, 14, 18), new StatBlock(6, 3, 1, 2), true,
                "tackle", "spark_bolt", "zap_field", "lightning_arc");
            var voltaranda = MakeCreature("voltaranda", "Voltaranda", "Charged to a crackling frenzy at high speed.",
                ElementType.Electric, new StatBlock(125, 44, 24, 30), new StatBlock(8, 3, 2, 2), false,
                "tackle", "spark_bolt", "zap_field", "lightning_arc");
            var thunderbeast = MakeCreature("thunderbeast", "Thunderbeast", "A storm given form; lightning answers its call.",
                ElementType.Electric, new StatBlock(205, 80, 44, 52), new StatBlock(10, 4, 2, 3), false,
                "tackle", "spark_bolt", "zap_field", "lightning_arc");

            var stoneback = MakeCreature("stoneback", "Stoneback", "A calm rockling with a coat of smooth grey pebbles.",
                ElementType.Stone, new StatBlock(140, 18, 30, 8), new StatBlock(8, 1, 3, 1), true,
                "tackle", "rock_smash", "rock_toss", "quake");
            var boulderback = MakeCreature("boulderback", "Boulderback", "Each step now shakes the ground beneath it.",
                ElementType.Stone, new StatBlock(205, 30, 52, 14), new StatBlock(10, 2, 4, 1), false,
                "tackle", "rock_smash", "rock_toss", "quake");
            var stonecolossus = MakeCreature("stonecolossus", "Stonecolossus", "An ancient mountain that learned to walk.",
                ElementType.Stone, new StatBlock(330, 55, 92, 24), new StatBlock(14, 3, 5, 1), false,
                "tackle", "rock_smash", "rock_toss", "quake");

            AddEvolution(emberling, emberclaw, 10);
            AddEvolution(emberclaw, infernodrake, 25);
            AddEvolution(aquaphin, aquatide, 10);
            AddEvolution(aquatide, leviathor, 25);
            AddEvolution(leafhorn, bramblehart, 10);
            AddEvolution(bramblehart, verdantaur, 25);
            AddEvolution(voltling, voltaranda, 10);
            AddEvolution(voltaranda, thunderbeast, 25);
            AddEvolution(stoneback, boulderback, 10);
            AddEvolution(boulderback, stonecolossus, 25);
        }

        private static CreatureData MakeCreature(string id, string displayName, string description,
            ElementType element, StatBlock baseStats, StatBlock growth, bool starter,
            params string[] abilityIds)
        {
            string path = CreatureDataDir + "/" + id + ".asset";
            if (AssetDatabase.LoadAssetAtPath<CreatureData>(path) != null)
                AssetDatabase.DeleteAsset(path);
            var creature = ScriptableObject.CreateInstance<CreatureData>();
            AssetDatabase.CreateAsset(creature, path);

            var so = new SerializedObject(creature);
            so.FindProperty("creatureId").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("description").stringValue = description;
            so.FindProperty("element").enumValueIndex = (int)element;
            so.FindProperty("isStarter").boolValue = starter;
            so.FindProperty("walkSpeedMultiplier").floatValue = 1f;
            so.PropertyStatBlock("baseStats", baseStats);
            so.PropertyStatBlock("statGrowth", growth);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CreaturePrefabDir + "/" + id + ".prefab");
            so.FindProperty("prefab").objectReferenceValue = prefab;

            var abilities = so.FindProperty("abilities");
            abilities.arraySize = abilityIds.Length;
            for (int i = 0; i < abilityIds.Length; i++)
            {
                var ability = AssetDatabase.LoadAssetAtPath<AbilityData>(AbilityDataDir + "/" + abilityIds[i] + ".asset");
                abilities.GetArrayElementAtIndex(i).objectReferenceValue = ability;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(creature);
            return creature;
        }

        private static void AddEvolution(CreatureData from, CreatureData to, int level)
        {
            var so = new SerializedObject(from);
            var stages = so.FindProperty("evolutionStages");
            stages.arraySize = 1;
            var stage = stages.GetArrayElementAtIndex(0);
            stage.FindPropertyRelative("nextCreature").objectReferenceValue = to;
            stage.FindPropertyRelative("requiredLevel").intValue = level;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(from);
        }

        private static void BuildCatalog()
        {
            string path = "Assets/ScriptableObjects/Creatures/CreatureCatalog.asset";
            if (AssetDatabase.LoadAssetAtPath<CreatureCatalog>(path) != null)
                AssetDatabase.DeleteAsset(path);
            var catalog = ScriptableObject.CreateInstance<CreatureCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
            var so = new SerializedObject(catalog);

            string[] ids = {
                "emberling", "emberclaw", "infernodrake",
                "aquaphin", "aquatide", "leviathor",
                "leafhorn", "bramblehart", "verdantaur",
                "voltling", "voltaranda", "thunderbeast",
                "stoneback", "boulderback", "stonecolossus"
            };

            var starters = so.FindProperty("starters");
            starters.arraySize = 0;
            var all = so.FindProperty("allCreatures");
            all.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
            {
                var creature = AssetDatabase.LoadAssetAtPath<CreatureData>(CreatureDataDir + "/" + ids[i] + ".asset");
                all.GetArrayElementAtIndex(i).objectReferenceValue = creature;
                if (creature != null && creature.IsStarter)
                {
                    starters.arraySize++;
                    starters.GetArrayElementAtIndex(starters.arraySize - 1).objectReferenceValue = creature;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void PropertyStatBlock(this SerializedObject so, string property, StatBlock value)
        {
            var p = so.FindProperty(property);
            p.FindPropertyRelative("maxHp").intValue = value.maxHp;
            p.FindPropertyRelative("attack").intValue = value.attack;
            p.FindPropertyRelative("defense").intValue = value.defense;
            p.FindPropertyRelative("speed").intValue = value.speed;
        }

        private static void FloatProperty(this SerializedObject so, string property, float value)
        {
            so.FindProperty(property).floatValue = value;
        }
    }
}