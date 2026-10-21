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
    /// Day 8 一键装配：把「房间池」从 Inspector 里的一个 List 升级成 ScriptableObject 资产，
    /// 并接进场景里的 RoomManager。
    ///
    /// 为什么要写脚本而不是手拖：
    ///   拖引用这件事一次只有 3 秒，但漏拖一次要查半小时（表现是「池子空了，一间房都生不出来」）。
    ///   脚本能顺手把每一项都验证一遍，还会写一份报告，出问题直接看报告。
    /// </summary>
    public static class Day8Builder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string RoomsDir = "Assets/_Project/Prefabs/Rooms";
        private const string PoolDir = "Assets/_Project/Data/Rooms";
        private const string PoolPath = PoolDir + "/RoomPool_Default.asset";

        [MenuItem("团结/生成房间池")]
        public static void Build()
        {
            var log = new StringBuilder();
            var fails = new List<string>();

            log.AppendLine("[Day8] 房间池装配报告");
            log.AppendLine();

            // ---------- 1. 打开场景 ----------
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            log.AppendLine(scene.IsValid()
                ? $"[PASS] 已打开场景 | {ScenePath}"
                : $"[FAIL] 打不开场景 | {ScenePath}");

            // ---------- 2. 房间预制体清单 ----------
            var prefabs = new List<GameObject>();
            if (AssetDatabase.IsValidFolder(RoomsDir))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { RoomsDir }))
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (go == null) continue;
                    prefabs.Add(go);
                    log.AppendLine($"[INFO] 发现房间预制体 | {prefabPath}");
                }
            }

            if (prefabs.Count == 0)
            {
                fails.Add($"{RoomsDir} 下没有房间预制体");
                log.AppendLine($"[FAIL] 房间预制体 | {RoomsDir} 下一个都没有");
            }

            // 按名字排序，保证每次跑出来的池子顺序一致（可复现的前提之一）
            prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            // ---------- 3. 房间池资产 ----------
            EnsureFolder(PoolDir);

            var pool = AssetDatabase.LoadAssetAtPath<RoomPool>(PoolPath);
            bool created = false;

            if (pool == null)
            {
                pool = ScriptableObject.CreateInstance<RoomPool>();
                AssetDatabase.CreateAsset(pool, PoolPath);
                created = true;
                log.AppendLine($"[PASS] 新建房间池资产 | {PoolPath}");
            }
            else
            {
                log.AppendLine($"[INFO] 房间池已存在 | {PoolPath}");
            }

            // 池子已经配好内容就不要覆盖 —— 那是用户手工调的权重，跑一次脚本就没了会很糟
            if (pool.Count == 0)
            {
                foreach (var prefab in prefabs)
                {
                    // 现在只有普通战斗房；精英/商店/宝箱等有了内容再加，权重先留 1
                    pool.Add(prefab, RoomType.Battle, 1f, 1, 0);
                }

                EditorUtility.SetDirty(pool);
                AssetDatabase.SaveAssets();
                log.AppendLine($"[PASS] 写入条目 | {pool.Count} 条（全部「战斗」，权重 1，第 1 间起可出）");
            }
            else
            {
                log.AppendLine($"[INFO] 池子已有 {pool.Count} 条，保持不动");
            }

            // ---------- 4. 逐条体检 ----------
            for (int i = 0; i < pool.Count; i++)
            {
                var e = pool.Entries[i];
                if (e == null)
                {
                    fails.Add($"第 {i + 1} 条是 null");
                    log.AppendLine($"[FAIL] 第 {i + 1} 条 | 是 null");
                    continue;
                }

                if (e.prefab == null)
                {
                    fails.Add($"第 {i + 1} 条没有预制体");
                    log.AppendLine($"[FAIL] 第 {i + 1} 条 | 没有预制体");
                    continue;
                }

                bool hasRoom = e.prefab.GetComponent<Room>() != null;
                log.AppendLine(
                    $"[{(hasRoom ? "PASS" : "FAIL")}] 第 {i + 1} 条 | {e.prefab.name}　" +
                    $"类型={RoomTypeNames.Of(e.type)}　权重={e.weight}　" +
                    $"第 {e.minRoomIndex}~{(e.maxRoomIndex <= 0 ? "∞" : e.maxRoomIndex.ToString())} 间");

                if (!hasRoom) fails.Add($"{e.prefab.name} 上没有 Room 组件");
            }

            // ---------- 5. 接进场景 ----------
            var rm = FindRoomManager();
            if (rm == null)
            {
                fails.Add("场景里找不到 RoomManager");
                log.AppendLine("[FAIL] RoomManager | 场景里找不到");
            }
            else
            {
                var so = new SerializedObject(rm);

                var poolProp = so.FindProperty("roomPool");
                if (poolProp == null)
                {
                    fails.Add("RoomManager 上没有 roomPool 字段");
                    log.AppendLine("[FAIL] 字段 roomPool | 不存在（脚本没编译？）");
                }
                else
                {
                    poolProp.objectReferenceValue = pool;
                    log.AppendLine($"[PASS] 接入 RoomManager | Room Pool = {PoolPath}");
                }

                // 种子填 0 = 每局随机。想复现就把 Console 打印的那个数字填进来
                var seedProp = so.FindProperty("seed");
                if (seedProp != null)
                {
                    seedProp.intValue = 0;
                    log.AppendLine("[PASS] 种子 | 0（每局随机，开局会打印到 Console）");
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rm);

                var legacy = so.FindProperty("roomPrefabs");
                if (legacy != null && legacy.arraySize > 0)
                {
                    log.AppendLine(
                        $"[INFO] 旧列表还有 {legacy.arraySize} 条 —— 池子非空时会被忽略，" +
                        "留着不影响，想干净可以手动清空");
                }
            }

            // ---------- 6. 存盘 ----------
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (EditorSceneManager.SaveScene(scene))
                {
                    log.AppendLine("[PASS] 场景已存盘");
                }
                else
                {
                    fails.Add("场景存盘失败");
                    log.AppendLine("[FAIL] 场景存盘失败");
                }
            }

            // ---------- 7. 出报告 ----------
            log.AppendLine();
            if (fails.Count == 0)
            {
                log.AppendLine("[SUMMARY] 全部通过");
            }
            else
            {
                log.AppendLine($"[SUMMARY] 失败 {fails.Count} 项：");
                foreach (string f in fails) log.AppendLine("  - " + f);
            }

            log.AppendLine();
            log.AppendLine("下一步：回到编辑器按 Play。Console 里会打印「本局种子 = xxxx」和「进入第 1 间：战斗「Room_xx」」。");

            string path = WriteReport(log.ToString());
            Debug.Log($"[Day8] 装配完成，报告：{path}\n{log}");

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    fails.Count == 0 ? "房间池装配完成" : $"房间池装配有 {fails.Count} 项失败",
                    log.ToString(),
                    "好");
            }
        }

        /// <summary>
        /// 校验「同一个种子 → 同一套关卡顺序」。
        ///
        /// 这是 00-行动清单.md 2.1 里点名要求的一条，也是将来写单元测试的雏形：
        /// 纯逻辑、不开场景、不依赖帧率，几十毫秒就能跑完。
        /// </summary>
        [MenuItem("团结/校验关卡随机")]
        public static void VerifyDeterminism()
        {
            var log = new StringBuilder();
            log.AppendLine("[Day8] 关卡随机校验");
            log.AppendLine();

            var pool = AssetDatabase.LoadAssetAtPath<RoomPool>(PoolPath);
            if (pool == null || pool.Count == 0)
            {
                log.AppendLine($"[FAIL] 房间池为空 | {PoolPath}（先跑一次「团结/生成房间池」）");
                WriteSeedReport(log.ToString());
                Debug.Log(log.ToString());
                return;
            }

            // ① 同一个种子跑两遍，序列必须完全一致
            string a = Simulate(pool, 20261021, 10);
            string b = Simulate(pool, 20261021, 10);
            log.AppendLine($"[{(a == b ? "PASS" : "FAIL")}] 同种子 → 同序列");
            log.AppendLine($"        种子 20261021：{a}");
            if (a != b) log.AppendLine($"        第二次却得到：{b}");

            // ② 换个种子应该得到不同的序列（池子里只有一种房间时这条不成立，所以只是 INFO）
            string c = Simulate(pool, 20261022, 10);
            log.AppendLine();
            log.AppendLine($"[INFO] 种子 20261022：{c}");
            log.AppendLine($"[{(a != c ? "PASS" : "INFO")}] 不同种子 → 不同序列 | {(a != c ? "是" : "池子里可选项太少，两次碰巧一样")}");

            // ③ 相邻两间不该是同一间
            bool noRepeat = true;
            var rng = new System.Random(777);
            int last = -1;
            for (int i = 1; i <= 20; i++)
            {
                int pick = pool.Pick(rng, i, last);
                if (pick >= 0 && pick == last) noRepeat = false;
                last = pick;
            }

            log.AppendLine();
            log.AppendLine($"[{(noRepeat ? "PASS" : "FAIL")}] 相邻两间不重复 | 连抽 20 间");
            log.AppendLine($"[INFO] 池子条目 | {pool.Count} 条");

            log.AppendLine();
            log.AppendLine("[SUMMARY] 见上方逐条结果");
            Debug.Log(log.ToString());
            string path = WriteSeedReport(log.ToString());
            Debug.Log($"[Day8] 随机校验报告：{path}");
        }

        private static string Simulate(RoomPool pool, int seed, int rooms)
        {
            var rng = new System.Random(seed);
            var names = new List<string>();
            int last = -1;

            for (int i = 1; i <= rooms; i++)
            {
                int pick = pool.Pick(rng, i, last);
                last = pick;

                if (pick < 0) { names.Add("无"); continue; }

                var prefab = pool.Entries[pick].prefab;
                names.Add(prefab != null ? prefab.name : $"空槽{pick}");
            }

            return string.Join(" → ", names);
        }

        private static string WriteSeedReport(string content)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string dir = Path.Combine(root, "_Tuanjie");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "day8-seed-report.txt");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private static RoomManager FindRoomManager()
        {
            // 场景里的物体可能被禁用，FindObjectOfType 找不到被禁用的 —— 这里两者都试
            var rm = Object.FindObjectOfType<RoomManager>();
            if (rm != null) return rm;

            foreach (var candidate in Resources.FindObjectsOfTypeAll<RoomManager>())
            {
                if (candidate == null) continue;
                if (candidate.gameObject.scene.IsValid()) return candidate;
            }

            return null;
        }

        private static void EnsureFolder(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;

            string parent = Path.GetDirectoryName(assetDir)?.Replace('\\', '/');

            // 逐层建，因为 CreateFolder 不会自动建父目录
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            if (!string.IsNullOrEmpty(parent))
            {
                AssetDatabase.CreateFolder(parent, Path.GetFileName(assetDir));
            }
        }

        private static string WriteReport(string content)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string dir = Path.Combine(root, "_Tuanjie");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "day8-report.txt");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
    }
}
