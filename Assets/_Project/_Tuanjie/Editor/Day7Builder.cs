using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// Day 7 一键装配：把 8 个词条资产生成出来，并在场景里挂上 RelicManager。
    ///
    /// 为什么要做成菜单而不是让你手点：
    ///   词条资产要填「属性 + 叠加方式 + 数值」三件套，手填 8 个 × 平均 1.5 条 = 12 条修饰符，
    ///   任何一条枚举选错都不会报错、只是数值不生效，排查起来非常费劲。
    ///   交给脚本生成，数值和描述文字永远一致。
    ///
    /// 用法：菜单栏 团结 → 生成 Day7 词条与三选一
    /// 副作用：会在 Assets/_Project/Data/Relics/ 下创建 8 个 .asset；
    ///        会在场景根节点创建（或复用）RelicManager 物体；会把场景存盘。
    /// </summary>
    public static class Day7Builder
    {
        private const string DataRoot = "Assets/_Project/Data";
        private const string RelicFolder = DataRoot + "/Relics";
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ReportPath = "_Tuanjie/day7-report.txt";

        [MenuItem("团结/生成 Day7 词条与三选一")]
        public static void Build()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("===== Day 7 装配报告 =====");
            sb.AppendLine("时间：" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            OpenGameScene(sb);

            List<RelicData> relics = CreateRelicAssets(sb);
            RelicManager manager = EnsureManager(sb, relics);
            SaveScene(sb, manager);

            sb.AppendLine("[SUMMARY] 词条资产：" + relics.Count + " 个");
            sb.AppendLine("[SUMMARY] 词条池已接：" + (manager != null ? "是" : "否"));

            WriteReport(sb.ToString());

            // 批处理（-batchmode）下弹窗会阻塞，必须跳过
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "Day 7 装配完成",
                    "已生成 " + relics.Count + " 个词条，并在场景里挂好 RelicManager。\n\n" +
                    "详细报告写在工程根目录的 " + ReportPath,
                    "好");
            }
        }

        /// <summary>
        /// 批处理（-batchmode）启动时活跃场景是一个空的未命名场景。
        /// 不先打开 Game 场景的话，物体找到的是空的、存盘也是空的（或者根本没路径可存），
        /// 上次就是这么白跑了一轮。
        /// </summary>
        private static void OpenGameScene(StringBuilder sb)
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == ScenePath && active.isLoaded)
            {
                sb.AppendLine("[PASS] 场景已打开 | " + ScenePath);
                return;
            }

            Scene opened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine(opened.path == ScenePath
                ? "[PASS] 已打开场景 | " + ScenePath
                : "[FAIL] 打开后路径不符 | " + opened.path);
        }

        // ---------------- 词条资产 ----------------

        private static List<RelicData> CreateRelicAssets(StringBuilder sb)
        {
            EnsureFolder(RelicFolder);
            sb.AppendLine();
            sb.AppendLine("----- 词条资产 -----");

            List<RelicData> list = new List<RelicData>
            {
                Make(sb, "Relic_SharpClaw", "锋利的爪", "攻击力 +10", RelicRarity.Common,
                    Mod(StatType.Attack, ModifierMode.Flat, 10f)),

                Make(sb, "Relic_ThickHide", "厚皮", "最大生命 +30，防御 +8%", RelicRarity.Common,
                    Mod(StatType.MaxHealth, ModifierMode.Flat, 30f),
                    Mod(StatType.Defense, ModifierMode.Flat, 0.08f)),

                Make(sb, "Relic_SwiftBoots", "疾风靴", "移动速度 +12%", RelicRarity.Common,
                    Mod(StatType.MoveSpeed, ModifierMode.PercentAdd, 0.12f)),

                Make(sb, "Relic_DeepWell", "深井", "最大法力 +30，每秒回蓝 +2", RelicRarity.Common,
                    Mod(StatType.MaxMana, ModifierMode.Flat, 30f),
                    Mod(StatType.ManaRegen, ModifierMode.Flat, 2f)),

                Make(sb, "Relic_Berserk", "狂怒", "攻击力 +25%，但最大生命 -15", RelicRarity.Rare,
                    Mod(StatType.Attack, ModifierMode.PercentAdd, 0.25f),
                    Mod(StatType.MaxHealth, ModifierMode.Flat, -15f)),

                Make(sb, "Relic_KeenEye", "锐眼", "暴击率 +12%", RelicRarity.Rare,
                    Mod(StatType.CritChance, ModifierMode.Flat, 0.12f)),

                Make(sb, "Relic_ColdBlood", "冷静", "冷却缩减 +15%", RelicRarity.Rare,
                    Mod(StatType.CooldownRate, ModifierMode.Flat, 0.15f)),

                Make(sb, "Relic_Overload", "超载", "攻击力 ×1.4，但冷却缩减 -10%", RelicRarity.Legendary,
                    Mod(StatType.Attack, ModifierMode.PercentMultiply, 0.4f),
                    Mod(StatType.CooldownRate, ModifierMode.Flat, -0.10f)),
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return list;
        }

        private static StatModifier Mod(StatType type, ModifierMode mode, float value)
        {
            return new StatModifier(type, mode, value);
        }

        private static RelicData Make(StringBuilder sb, string fileName, string displayName,
            string description, RelicRarity rarity, params StatModifier[] modifiers)
        {
            string path = RelicFolder + "/" + fileName + ".asset";
            RelicData relic = AssetDatabase.LoadAssetAtPath<RelicData>(path);

            bool isNew = relic == null;
            if (isNew) relic = ScriptableObject.CreateInstance<RelicData>();

            relic.displayName = displayName;
            relic.description = description;
            relic.rarity = rarity;
            relic.modifiers = modifiers;

            if (isNew) AssetDatabase.CreateAsset(relic, path);
            EditorUtility.SetDirty(relic);

            sb.AppendLine(string.Format("[PASS] {0}（{1}）| {2} | 修饰符 {3} 条 | {4}",
                displayName, FileNameOf(path), description, modifiers.Length, isNew ? "新建" : "已存在，覆盖"));
            return relic;
        }

        // ---------------- 场景装配 ----------------

        private static RelicManager EnsureManager(StringBuilder sb, List<RelicData> relics)
        {
            sb.AppendLine();
            sb.AppendLine("----- 场景装配 -----");

            GameObject go = GameObject.Find("RelicManager");
            if (go == null)
            {
                go = new GameObject("RelicManager");
                Undo.RegisterCreatedObjectUndo(go, "创建 RelicManager");
                sb.AppendLine("[PASS] 新建物体 | RelicManager");
            }
            else
            {
                sb.AppendLine("[PASS] 复用物体 | RelicManager");
            }

            RelicManager manager = go.GetComponent<RelicManager>();
            if (manager == null)
            {
                manager = go.AddComponent<RelicManager>();
                sb.AppendLine("[PASS] 挂上组件 | RelicManager");
            }
            else
            {
                sb.AppendLine("[PASS] 组件已存在 | RelicManager");
            }

            // pool 是 private + SerializeField，只能走 SerializedObject
            SerializedObject so = new SerializedObject(manager);
            SerializedProperty pool = so.FindProperty("pool");
            if (pool == null)
            {
                sb.AppendLine("[FAIL] 找不到 pool 字段，词条池没接上");
            }
            else
            {
                pool.arraySize = relics.Count;
                for (int i = 0; i < relics.Count; i++)
                    pool.GetArrayElementAtIndex(i).objectReferenceValue = relics[i];

                so.ApplyModifiedPropertiesWithoutUndo();
                sb.AppendLine("[PASS] 词条池 | " + relics.Count + " 个");
            }

            // stats 留空时运行时自动找同物体 / 子物体的 CharacterStats。
            // RelicManager 挂在根节点上找不到 Player 的 stats，所以这里直接把它接上。
            SerializedProperty statsProp = so.FindProperty("stats");
            if (statsProp != null && statsProp.objectReferenceValue == null)
            {
                CharacterStats playerStats = FindPlayerStats();
                if (playerStats != null)
                {
                    statsProp.objectReferenceValue = playerStats;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    sb.AppendLine("[PASS] 数值来源 | " + PathOf(playerStats.transform));
                }
                else
                {
                    sb.AppendLine("[WARN] 没找到玩家的 CharacterStats，词条数值不会生效");
                }
            }

            return manager;
        }

        private static CharacterStats FindPlayerStats()
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                CharacterStats found = player.GetComponent<CharacterStats>();
                if (found != null) return found;
            }

            CharacterStats[] all = Object.FindObjectsOfType<CharacterStats>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].GetComponent<PlayerController>() != null) return all[i];
            }
            return null;
        }

        private static void SaveScene(StringBuilder sb, RelicManager manager)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                sb.AppendLine("[WARN] 当前场景没有路径，没存盘");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            sb.AppendLine(saved
                ? "[PASS] 场景已存盘 | " + scene.path
                : "[FAIL] 场景存盘失败 | " + scene.path);

            if (manager != null)
                sb.AppendLine("[INFO] RelicManager 位置 | " + PathOf(manager.transform));
        }

        // ---------------- 杂项 ----------------

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);

            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string FileNameOf(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            Transform cur = t.parent;
            while (cur != null)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return path;
        }

        private static void WriteReport(string content)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "_Tuanjie");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "day7-report.txt"), content, new UTF8Encoding(false));
            Debug.Log("[Day7] 报告已写入 " + ReportPath);
        }
    }
}
