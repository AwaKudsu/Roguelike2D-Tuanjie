#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 一键把「背包 + 装备」装进场景：造几件物品资产、给玩家挂 Inventory / Equipment、
    /// 接好引用、发四件初始物品、再建一个 InventoryUI 物体。
    ///
    /// 为什么要脚本而不是手拖：
    ///   Inventory / Equipment 的字段全是 private [SerializeField]，
    ///   手拖要切五次 Inspector；而且物品资产有 6 个，手建必然漏字段。
    /// </summary>
    public static class Day9Builder
    {
        private const string ScenePath  = "Assets/_Project/Scenes/Game.unity";
        private const string ItemsDir   = "Assets/_Project/Data/Items";
        private const string WeaponsDir = "Assets/_Project/Data/Weapons";

        [MenuItem("团结/生成背包与装备物品")]
        public static void Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== Day 9 背包与装备装配报告 =====");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Line(sb, "PASS", "已打开场景", ScenePath);

            EnsureFolder(ItemsDir);

            AttackData mageBolt = AssetDatabase.LoadAssetAtPath<AttackData>(WeaponsDir + "/Attack_MageBolt.asset");
            if (mageBolt == null) Line(sb, "WARN", "没找到 Attack_MageBolt", "法杖不会改成远程攻击");
            else                  Line(sb, "PASS", "法杖用的远程攻击", mageBolt.name);

            // ---- 六件物品 ----
            ItemData ironSword = MakeItem("Item_IronSword", "铁剑",
                "一把还能用的铁剑。挥砍靠的是它，不是你的胳膊。",
                ItemRarity.Common, EquipSlot.Weapon, null,
                new StatModifier(StatType.Attack, ModifierMode.Flat, 6f));

            ItemData oakStaff = MakeItem("Item_OakStaff", "橡木法杖",
                "装上它以后，普通攻击会变成发射法球——不再近身挥砍。",
                ItemRarity.Rare, EquipSlot.Weapon, mageBolt,
                new StatModifier(StatType.Attack, ModifierMode.PercentAdd, 0.10f),
                new StatModifier(StatType.CritChance, ModifierMode.Flat, 0.05f));

            ItemData leatherArmor = MakeItem("Item_LeatherArmor", "皮甲",
                "硬化的兽皮。挡得住爪击，挡不住火球。",
                ItemRarity.Common, EquipSlot.Armor, null,
                new StatModifier(StatType.MaxHealth, ModifierMode.Flat, 25f),
                new StatModifier(StatType.Defense, ModifierMode.Flat, 0.05f));

            ItemData robe = MakeItem("Item_Robe", "法袍",
                "布料比铁甲更懂法力是怎么流动的。",
                ItemRarity.Rare, EquipSlot.Armor, null,
                new StatModifier(StatType.MaxMana, ModifierMode.Flat, 30f),
                new StatModifier(StatType.ManaRegen, ModifierMode.Flat, 2f),
                new StatModifier(StatType.Defense, ModifierMode.Flat, 0.03f));

            ItemData powerRing = MakeItem("Item_PowerRing", "力量指环",
                "戴上它，你的每一击都更重一点。",
                ItemRarity.Rare, EquipSlot.Trinket, null,
                new StatModifier(StatType.Attack, ModifierMode.PercentAdd, 0.15f));

            ItemData scholarAmulet = MakeItem("Item_ScholarAmulet", "学者护符",
                "冷却更短，暴击更疼。代价是它很丑。",
                ItemRarity.Legendary, EquipSlot.Trinket, null,
                new StatModifier(StatType.CooldownRate, ModifierMode.Flat, 0.10f),
                new StatModifier(StatType.CritMultiplier, ModifierMode.Flat, 0.20f));

            AssetDatabase.SaveAssets();
            Line(sb, "PASS", "物品资产", "6 件（2 武器 / 2 护甲 / 2 饰品）");

            // ---- 玩家身上的两个组件 ----
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                Line(sb, "FAIL", "找不到玩家", "场景里没有 Tag=Player 的物体");
                Finish(sb, 1);
                return;
            }

            CharacterStats stats     = player.GetComponent<CharacterStats>();
            PlayerAttacker attacker  = player.GetComponent<PlayerAttacker>();
            PlayerInputReader reader = player.GetComponent<PlayerInputReader>();

            Inventory inventory = player.GetComponent<Inventory>();
            if (inventory == null) { inventory = player.AddComponent<Inventory>(); Line(sb, "PASS", "新建组件", "Inventory"); }
            else                   Line(sb, "INFO", "组件已存在", "Inventory");

            Equipment equipment = player.GetComponent<Equipment>();
            if (equipment == null) { equipment = player.AddComponent<Equipment>(); Line(sb, "PASS", "新建组件", "Equipment"); }
            else                   Line(sb, "INFO", "组件已存在", "Equipment");

            // ---- 接引用 ----
            var invSo = new SerializedObject(inventory);
            invSo.FindProperty("capacity").intValue = 12;

            SerializedProperty starters = invSo.FindProperty("startingItems");
            starters.arraySize = 4;
            starters.GetArrayElementAtIndex(0).objectReferenceValue = ironSword;
            starters.GetArrayElementAtIndex(1).objectReferenceValue = leatherArmor;
            starters.GetArrayElementAtIndex(2).objectReferenceValue = powerRing;
            starters.GetArrayElementAtIndex(3).objectReferenceValue = oakStaff;
            invSo.ApplyModifiedPropertiesWithoutUndo();
            Line(sb, "PASS", "背包初始物品", "铁剑 / 皮甲 / 力量指环 / 橡木法杖（4 件，容量 12）");

            var eqSo = new SerializedObject(equipment);
            eqSo.FindProperty("stats").objectReferenceValue     = stats;
            eqSo.FindProperty("attacker").objectReferenceValue  = attacker;
            eqSo.FindProperty("inventory").objectReferenceValue = inventory;
            eqSo.ApplyModifiedPropertiesWithoutUndo();
            Line(sb, "PASS", "装备槽引用", "stats / attacker / inventory 已接");

            if (stats == null)    Line(sb, "WARN", "玩家没有 CharacterStats", "穿装备不会加属性");
            if (attacker == null) Line(sb, "WARN", "玩家没有 PlayerAttacker", "武器不会换攻击形态");

            // ---- 界面 ----
            GameObject uiGo = GameObject.Find("InventoryUI");
            if (uiGo == null)
            {
                uiGo = new GameObject("InventoryUI", typeof(RectTransform));
                Line(sb, "PASS", "新建物体", "InventoryUI");
            }

            InventoryUI ui = uiGo.GetComponent<InventoryUI>();
            if (ui == null) { ui = uiGo.AddComponent<InventoryUI>(); Line(sb, "PASS", "挂上组件", "InventoryUI"); }

            var uiSo = new SerializedObject(ui);
            uiSo.FindProperty("inventory").objectReferenceValue   = inventory;
            uiSo.FindProperty("equipment").objectReferenceValue   = equipment;
            uiSo.FindProperty("inputReader").objectReferenceValue = reader;
            uiSo.ApplyModifiedPropertiesWithoutUndo();
            Line(sb, "PASS", "界面引用", "inventory / equipment / inputReader 已接（按 Tab 打开）");

            // ---- 存盘 ----
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            bool saved = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Line(sb, saved ? "PASS" : "FAIL", "场景已存盘", ScenePath);

            Finish(sb, saved ? 0 : 1);
        }

        // ---------------- 工具 ----------------

        private static ItemData MakeItem(string fileName, string displayName, string description,
                                         ItemRarity rarity, EquipSlot slot, AttackData weaponAttack,
                                         params StatModifier[] modifiers)
        {
            string path = ItemsDir + "/" + fileName + ".asset";

            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(item, path);
            }

            item.displayName  = displayName;
            item.description  = description;
            item.rarity       = rarity;
            item.slot         = slot;
            item.weaponAttack = weaponAttack;
            item.modifiers    = modifiers;

            EditorUtility.SetDirty(item);
            return item;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];   // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void Line(StringBuilder sb, string tag, string title, string detail)
        {
            sb.Append('[').Append(tag).Append("] ").Append(title).Append(" | ").AppendLine(detail ?? "");
        }

        private static void Finish(StringBuilder sb, int failed)
        {
            sb.AppendLine("[SUMMARY] 失败 | " + failed);

            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir  = Path.Combine(root, "_Tuanjie");
            Directory.CreateDirectory(dir);

            string reportPath = Path.Combine(dir, "day9-report.txt");
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));

            Debug.Log(sb.ToString());

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Day 9 背包与装备",
                    failed == 0 ? "装配完成。按 Tab 打开背包。\n\n报告已写到 _Tuanjie/day9-report.txt"
                                : "装配有失败项，见 _Tuanjie/day9-report.txt",
                    "好");
            }
        }
    }
}
#endif
