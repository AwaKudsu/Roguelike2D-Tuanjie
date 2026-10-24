#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 不按 Play 也能跑的背包/装备自检。
    ///
    /// 做法：先反射调用 Awake()（编辑模式下场景物体的 Awake 不会自己跑），
    /// 把 CharacterStats 的基础值装好，然后直接调 Equipment.Equip / Unequip，
    /// 比对 CharacterStats.Get() 的差值。全程不存盘，跑完把状态还原。
    /// </summary>
    public static class Day9Check
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ItemsDir  = "Assets/_Project/Data/Items";

        private static int _passed;
        private static int _failed;

        [MenuItem("团结/验证背包与装备")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== Day 9 背包与装备自检 =====");
            _passed = 0;
            _failed = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Info(sb, "已打开场景", ScenePath);

            GameObject player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                Check(sb, false, "找到玩家", "场景里没有 Tag=Player");
                Finish(sb);
                return;
            }
            Check(sb, true, "找到玩家", player.name);

            CharacterStats stats     = player.GetComponent<CharacterStats>();
            PlayerAttacker attacker  = player.GetComponent<PlayerAttacker>();
            Inventory inventory      = player.GetComponent<Inventory>();
            Equipment equipment      = player.GetComponent<Equipment>();

            Check(sb, stats != null,     "CharacterStats", stats != null ? "有" : "缺");
            Check(sb, attacker != null,  "PlayerAttacker", attacker != null ? "有" : "缺");
            Check(sb, inventory != null, "Inventory", inventory != null ? "有" : "缺");
            Check(sb, equipment != null, "Equipment", equipment != null ? "有" : "缺");

            if (stats == null || attacker == null || inventory == null || equipment == null)
            {
                Finish(sb);
                return;
            }

            // 编辑模式下 Awake 不会自动跑（基础值、出厂攻击都在里面）
            CallAwake(stats);
            CallAwake(attacker);
            CallAwake(equipment);

            // ---- 物品资产 ----
            string[] guids = AssetDatabase.FindAssets("t:ItemData", new[] { ItemsDir });
            Check(sb, guids.Length == 6, "物品资产数量", guids.Length + " 个（期望 6）");

            ItemData sword        = Load("Item_IronSword");
            ItemData staff        = Load("Item_OakStaff");
            ItemData leatherArmor = Load("Item_LeatherArmor");
            ItemData robe         = Load("Item_Robe");
            ItemData ring         = Load("Item_PowerRing");
            ItemData amulet       = Load("Item_ScholarAmulet");
            AttackData mageBolt   = AssetDatabase.LoadAssetAtPath<AttackData>(
                                        "Assets/_Project/Data/Weapons/Attack_MageBolt.asset");

            Check(sb, sword != null && staff != null && leatherArmor != null
                   && robe != null && ring != null && amulet != null,
                  "六件物品都能加载", "铁剑/橡木法杖/皮甲/法袍/力量指环/学者护符");

            ItemData[] all = { sword, staff, leatherArmor, robe, ring, amulet };
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                string desc = all[i].DescribeModifiers().Replace('\n', ' ');
                Check(sb, !string.IsNullOrEmpty(desc), "属性文案 · " + all[i].displayName, desc);
            }

            // ---- 基线 ----
            float atk0    = stats.Get(StatType.Attack);
            float hp0     = stats.Get(StatType.MaxHealth);
            float def0    = stats.Get(StatType.Defense);
            float mana0   = stats.Get(StatType.MaxMana);
            float cd0     = stats.Get(StatType.CooldownRate);
            float critMul0 = stats.Get(StatType.CritMultiplier);
            AttackData originalAttack = attacker.CurrentAttack;

            Info(sb, "基线", string.Format("攻击 {0:0.##} / 生命 {1:0.##} / 防御 {2:0.###} / 法力 {3:0.##}",
                                            atk0, hp0, def0, mana0));

            // ---- 1. 穿武器：+6 攻击 ----
            equipment.Equip(sword);
            Approx(sb, stats.Get(StatType.Attack), atk0 + 6f, "穿铁剑 → 攻击 +6",
                   stats.Get(StatType.Attack), atk0 + 6f);
            Check(sb, ReferenceEquals(equipment.Get(EquipSlot.Weapon), sword), "武器槽是铁剑", "槽位已占用");

            // ---- 2. 换武器：法杖 +10% 且换成远程攻击 ----
            equipment.Equip(staff);
            Approx(sb, stats.Get(StatType.Attack), atk0 * 1.10f, "换橡木法杖 → 攻击 +10%",
                   stats.Get(StatType.Attack), atk0 * 1.10f);
            Check(sb, ReferenceEquals(attacker.CurrentAttack, mageBolt), "法杖接管普通攻击",
                  attacker.CurrentAttack != null ? attacker.CurrentAttack.name : "null");
            Check(sb, ReferenceEquals(equipment.Get(EquipSlot.Weapon), staff), "武器槽是橡木法杖", "已换");
            Check(sb, inventory.Contains(sword), "铁剑被自动退回背包", "同槽位旧装备回收");

            // ---- 3. 脱武器：还原成出厂攻击 ----
            equipment.Unequip(EquipSlot.Weapon);
            Check(sb, ReferenceEquals(attacker.CurrentAttack, originalAttack), "脱下武器 → 攻击形态还原",
                  (originalAttack != null ? originalAttack.name : "null") + " → "
                  + (attacker.CurrentAttack != null ? attacker.CurrentAttack.name : "null"));
            Approx(sb, stats.Get(StatType.Attack), atk0, "脱武器 → 攻击力还原",
                   stats.Get(StatType.Attack), atk0);
            Check(sb, equipment.Get(EquipSlot.Weapon) == null, "武器槽已空", "空");

            // ---- 4. 护甲：+25 生命 / +5% 防御 ----
            equipment.Equip(leatherArmor);
            Approx(sb, stats.Get(StatType.MaxHealth), hp0 + 25f, "穿皮甲 → 生命 +25",
                   stats.Get(StatType.MaxHealth), hp0 + 25f);
            Approx(sb, stats.Get(StatType.Defense), def0 + 0.05f, "穿皮甲 → 防御 +5%",
                   stats.Get(StatType.Defense), def0 + 0.05f);

            // ---- 5. 换护甲：法袍撤掉生命加成 ----
            equipment.Equip(robe);
            Approx(sb, stats.Get(StatType.MaxHealth), hp0, "换法袍 → 生命回到基线",
                   stats.Get(StatType.MaxHealth), hp0);
            Approx(sb, stats.Get(StatType.MaxMana), mana0 + 30f, "法袍 → 法力 +30",
                   stats.Get(StatType.MaxMana), mana0 + 30f);

            equipment.Unequip(EquipSlot.Armor);
            Approx(sb, stats.Get(StatType.MaxMana), mana0, "脱护甲 → 法力还原",
                   stats.Get(StatType.MaxMana), mana0);

            // ---- 6. 饰品：+15% 攻击，换饰品后不叠加 ----
            equipment.Equip(ring);
            Approx(sb, stats.Get(StatType.Attack), atk0 * 1.15f, "戴力量指环 → 攻击 +15%",
                   stats.Get(StatType.Attack), atk0 * 1.15f);

            equipment.Equip(amulet);
            Approx(sb, stats.Get(StatType.Attack), atk0, "换学者护符 → 指环加成消失",
                   stats.Get(StatType.Attack), atk0);
            Approx(sb, stats.Get(StatType.CooldownRate), cd0 + 0.10f, "学者护符 → 冷却缩减 +10%",
                   stats.Get(StatType.CooldownRate), cd0 + 0.10f);
            Approx(sb, stats.Get(StatType.CritMultiplier), critMul0 + 0.20f, "学者护符 → 暴击伤害 +20%",
                   stats.Get(StatType.CritMultiplier), critMul0 + 0.20f);
            Check(sb, inventory.Contains(ring), "力量指环退回背包", "同槽位回收");

            equipment.Unequip(EquipSlot.Trinket);
            Approx(sb, stats.Get(StatType.CooldownRate), cd0, "脱饰品 → 冷却缩减还原",
                   stats.Get(StatType.CooldownRate), cd0);

            // ---- 7. 背包容量 ----
            int capacity = inventory.Capacity;
            var dummies = new List<ItemData>();
            inventory.Clear();

            for (int i = 0; i < capacity; i++)
            {
                ItemData dummy = ScriptableObject.CreateInstance<ItemData>();
                dummy.displayName = "测试物品_" + i;
                dummies.Add(dummy);
                inventory.Add(dummy);
            }

            Check(sb, inventory.Count == capacity && inventory.IsFull, "背包能装满",
                  inventory.Count + "/" + capacity);

            ItemData overflow = ScriptableObject.CreateInstance<ItemData>();
            Check(sb, !inventory.Add(overflow), "第 " + (capacity + 1) + " 件被拒绝", "容量上限生效");

            inventory.Clear();
            for (int i = 0; i < dummies.Count; i++) UnityEngine.Object.DestroyImmediate(dummies[i]);
            UnityEngine.Object.DestroyImmediate(overflow);
            Check(sb, inventory.Count == 0, "背包已清空", "测试数据已清理");

            // ---- 8. 界面接线 ----
            GameObject uiGo = GameObject.Find("InventoryUI");
            InventoryUI ui = uiGo != null ? uiGo.GetComponent<InventoryUI>() : null;
            Check(sb, ui != null, "场景里有 InventoryUI", ui != null ? uiGo.name : "缺");

            if (ui != null)
            {
                var so = new SerializedObject(ui);
                bool wired = so.FindProperty("inventory").objectReferenceValue != null
                          && so.FindProperty("equipment").objectReferenceValue != null
                          && so.FindProperty("inputReader").objectReferenceValue != null;
                Check(sb, wired, "界面三个引用都接上了", "inventory / equipment / inputReader");
            }

            // ---- 9. 文案工具 ----
            string swordLine = StatText.Describe(new StatModifier(StatType.Attack, ModifierMode.Flat, 6f));
            string critLine  = StatText.Describe(new StatModifier(StatType.CritChance, ModifierMode.Flat, 0.05f));
            string multLine  = StatText.Describe(new StatModifier(StatType.Attack, ModifierMode.PercentMultiply, 0.4f));

            Check(sb, swordLine.Contains("攻击力") && swordLine.Contains("+6"), "平值文案", swordLine);
            Check(sb, critLine.Contains("暴击率") && critLine.Contains("5%"), "比例属性显示成百分比", critLine);
            Check(sb, multLine.Contains("×1.4"), "乘算文案", multLine);

            // ---- 还原现场（不存盘，只是别把编辑器里的对象改脏） ----
            equipment.Unequip(EquipSlot.Weapon);
            equipment.Unequip(EquipSlot.Armor);
            equipment.Unequip(EquipSlot.Trinket);
            attacker.SetAttack(originalAttack);

            Finish(sb);
        }

        // ---------------- 工具 ----------------

        private static ItemData Load(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<ItemData>(ItemsDir + "/" + fileName + ".asset");
        }

        private static void CallAwake(MonoBehaviour behaviour)
        {
            if (behaviour == null) return;

            MethodInfo awake = behaviour.GetType().GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);

            if (awake != null) awake.Invoke(behaviour, null);
        }

        private static void Approx(StringBuilder sb, float actual, float expected, string title,
                                   float shownActual, float shownExpected)
        {
            bool ok = Mathf.Abs(actual - expected) < 0.001f;
            Check(sb, ok, title, string.Format("实际 {0:0.####} / 期望 {1:0.####}", shownActual, shownExpected));
        }

        private static void Check(StringBuilder sb, bool ok, string title, string detail)
        {
            if (ok) _passed++; else _failed++;

            sb.Append(ok ? "[PASS] " : "[FAIL] ")
              .Append(title)
              .Append(" | ")
              .AppendLine(detail ?? "");
        }

        private static void Info(StringBuilder sb, string title, string detail)
        {
            sb.Append("[INFO] ").Append(title).Append(" | ").AppendLine(detail ?? "");
        }

        private static void Finish(StringBuilder sb)
        {
            sb.AppendLine("[SUMMARY] 通过 | " + _passed);
            sb.AppendLine("[SUMMARY] 失败 | " + _failed);

            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir  = Path.Combine(root, "_Tuanjie");
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, "day9-check-report.txt"),
                              sb.ToString(), new UTF8Encoding(false));

            Debug.Log(sb.ToString());

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Day 9 自检",
                    _failed == 0 ? "全部通过（" + _passed + " 项）" : "有 " + _failed + " 项失败",
                    "好");
            }
        }
    }
}
#endif
