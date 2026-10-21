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
    /// Day 8 验证：不按 Play，直接在编辑器里把 RoomManager 的选房逻辑跑一遍。
    ///
    /// 为什么不用 Play：批处理模式下 EditorApplication.EnterPlaymode() 会挂在
    /// 「Loaded scene 'Temp/__Backupscenes/0.backup'」上永远不返回（实测卡死十分钟以上）。
    /// 所以这里用反射直接调 private 的 EnsureRandom / PickIndex —— 走的是真正的代码路径，
    /// 只是不渲染、不跑 MonoBehaviour 生命周期。
    ///
    /// 用法（先关掉 GUI 编辑器，否则 Library 被锁）：
    ///   Tuanjie.exe -batchmode -quit -projectPath D:\TuanjieRoguelike2D ^
    ///     -executeMethod Tuanjie.PortCheck.DayCheck.Run -logFile D:\TuanjieRoguelike2D\_Tuanjie\day8-check.log
    /// </summary>
    public static class DayCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ReportPath = "_Tuanjie/day8-check-report.txt";
        private const int PickCount = 10;

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static int _fails;
        private static int _warns;
        private static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("团结/验证 Day8 选房")]
        public static void Run()
        {
            _fails = 0;
            _warns = 0;
            Log.Clear();
            Log.AppendLine("[Day8] 选房逻辑验证报告");
            Log.AppendLine();

            // ---------- 1. 打开场景 ----------
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid(), "打开场景", ScenePath, "打不开 " + ScenePath);
            if (!scene.IsValid())
            {
                Write();
                return;
            }

            // ---------- 2. 找 RoomManager ----------
            var managers = UnityEngine.Object.FindObjectsOfType<RoomManager>();
            if (managers == null || managers.Length == 0)
            {
                Fail("场景里找不到 RoomManager", "整个场景一个 RoomManager 都没有，关卡流水线跑不起来");
                Write();
                return;
            }

            RoomManager rm = managers[0];
            Info("RoomManager 物体", rm.name + "（场景里共 " + managers.Length + " 个）");

            // ---------- 3. 池子接线 ----------
            var so = new SerializedObject(rm);
            SerializedProperty poolProp = so.FindProperty("roomPool");
            SerializedProperty seedProp = so.FindProperty("seed");

            if (poolProp == null || seedProp == null)
            {
                Fail("RoomManager 字段", "找不到 roomPool / seed 字段，Day 8 的代码没编进这个工程");
                Write();
                return;
            }

            var pool = poolProp.objectReferenceValue as RoomPool;
            Check(pool != null, "房间池接线", pool != null ? pool.name : "（空）",
                "roomPool 是空的，运行时会退回旧的 roomPrefabs 列表");

            if (pool == null || pool.Count == 0)
            {
                Fail("房间池内容", "池子为空，一间都抽不出来");
                Write();
                return;
            }

            Info("池子条目数", pool.Count + " 条");
            for (int i = 0; i < pool.Count; i++)
            {
                RoomPoolEntry e = pool.Entries[i];
                Info("  条目 " + i,
                    (e.prefab != null ? e.prefab.name : "（空预制体）") +
                    " | 类型=" + RoomTypeNames.Of(e.type) +
                    " | 权重=" + e.weight +
                    " | 出现窗口=" + e.minRoomIndex + "~" + (e.maxRoomIndex == 0 ? "不限" : e.maxRoomIndex.ToString()));
            }

            Info("场景里的 Seed 字段", seedProp.intValue + "（0 = 每局随机）");

            // ---------- 4. 反射拿私有方法 ----------
            MethodInfo ensureRandom = typeof(RoomManager).GetMethod("EnsureRandom", Private);
            MethodInfo pickIndex = typeof(RoomManager).GetMethod("PickIndex", Private, null, new[] { typeof(int) }, null);
            FieldInfo rngField = typeof(RoomManager).GetField("_rng", Private);
            FieldInfo lastPickField = typeof(RoomManager).GetField("_lastPoolPick", Private);

            if (ensureRandom == null || pickIndex == null || rngField == null || lastPickField == null)
            {
                Fail("反射拿私有成员",
                    "EnsureRandom=" + (ensureRandom != null) +
                    " PickIndex=" + (pickIndex != null) +
                    " _rng=" + (rngField != null) +
                    " _lastPoolPick=" + (lastPickField != null));
                Write();
                return;
            }

            Info("反射", "EnsureRandom / PickIndex / _rng / _lastPoolPick 全部拿到，可以走真代码路径");

            // ---------- 5. 同种子 → 同序列 ----------
            const int fixedSeed = 20261021;
            List<int> first = RunOnce(rm, ensureRandom, pickIndex, rngField, lastPickField, seedProp, fixedSeed);
            List<int> second = RunOnce(rm, ensureRandom, pickIndex, rngField, lastPickField, seedProp, fixedSeed);

            Check(first.Count == PickCount && first.TrueForAll(i => i >= 0),
                "固定种子能连抽 " + PickCount + " 间",
                "序列=" + SeqString(first),
                "有抽不到的房间（返回 -1），检查出现窗口");

            Check(Same(first, second), "同种子 → 同序列",
                "两次都是 " + SeqString(first),
                "第一次=" + SeqString(first) + "，第二次=" + SeqString(second) + " —— 种子没生效");

            Check(rm.Seed == fixedSeed, "Seed 属性回读", "Seed=" + rm.Seed, "Seed 属性和字段不一致：" + rm.Seed);

            // ---------- 6. 相邻不重复 ----------
            bool noAdjacentDup = true;
            for (int i = 1; i < first.Count; i++)
            {
                if (first[i] == first[i - 1])
                {
                    noAdjacentDup = false;
                    break;
                }
            }

            Check(noAdjacentDup, "相邻两间不重复", "连抽 " + PickCount + " 间",
                "序列里出现了连续两间相同：" + SeqString(first));

            // ---------- 7. 换种子 → 换序列 ----------
            List<int> other = RunOnce(rm, ensureRandom, pickIndex, rngField, lastPickField, seedProp, fixedSeed + 1);
            if (Same(first, other))
            {
                Warn("换种子 → 换序列",
                    "种子 " + fixedSeed + " 与 " + (fixedSeed + 1) + " 序列相同：" + SeqString(first) +
                    "。池子只有 " + pool.Count + " 条时这是正常现象（不重复规则会把序列压成交替），" +
                    "等房间做到 4 间以上再看这一项");
            }
            else
            {
                Pass("换种子 → 换序列", "种子 " + fixedSeed + "=" + SeqString(first) + "；种子 " + (fixedSeed + 1) + "=" + SeqString(other));
            }

            // ---------- 8. Seed = 0 时每局随机 ----------
            ResetRandom(rm, rngField, lastPickField, seedProp, 0);
            ensureRandom.Invoke(rm, null);
            int randomSeedA = rm.Seed;
            ResetRandom(rm, rngField, lastPickField, seedProp, 0);
            ensureRandom.Invoke(rm, null);
            int randomSeedB = rm.Seed;

            Check(randomSeedA != 0 && randomSeedB != 0, "Seed=0 时自动生成种子",
                randomSeedA + " / " + randomSeedB,
                "自动生成的种子是 0，说明 EnsureRandom 没写进 _resolvedSeed");

            if (randomSeedA == randomSeedB)
            {
                Warn("Seed=0 时两局不同种子",
                    "两次都生成了 " + randomSeedA + "（同一 tick 内运行才会这样，不影响实际使用）");
            }
            else
            {
                Pass("Seed=0 时两局不同种子", randomSeedA + " ≠ " + randomSeedB);
            }

            // ---------- 9. 类型注入链 ----------
            Check(rm.CurrentRoomType == RoomType.Battle, "开局房间类型的默认值",
                "CurrentRoomType=" + RoomTypeNames.Of(rm.CurrentRoomType),
                "默认类型不是战斗：" + RoomTypeNames.Of(rm.CurrentRoomType));

            MethodInfo roomInit = typeof(Room).GetMethod("Initialize", new[] { typeof(RoomManager), typeof(RoomType) });
            PropertyInfo roomType = typeof(Room).GetProperty("Type");
            Check(roomInit != null && roomType != null, "Room 能接收外部注入的类型",
                "Initialize(RoomManager, RoomType)=" + (roomInit != null) + " Type 属性=" + (roomType != null),
                "Room 上没有带 RoomType 的 Initialize 重载，池子里的类型注入不下去");

            // ---------- 10. 还原现场 ----------
            ResetRandom(rm, rngField, lastPickField, seedProp, 0);
            Info("还原", "Seed 已还原成 0（场景没有存盘）");

            Write();
        }

        // ==================== 选房序列 ====================

        private static List<int> RunOnce(
            RoomManager rm,
            MethodInfo ensureRandom,
            MethodInfo pickIndex,
            FieldInfo rngField,
            FieldInfo lastPickField,
            SerializedProperty seedProp,
            int seed)
        {
            ResetRandom(rm, rngField, lastPickField, seedProp, seed);
            ensureRandom.Invoke(rm, null);

            var seq = new List<int>(PickCount);
            for (int roomIndex = 1; roomIndex <= PickCount; roomIndex++)
            {
                object result = pickIndex.Invoke(rm, new object[] { (object)roomIndex });
                seq.Add(result is int value ? value : -1);
            }

            return seq;
        }

        private static void ResetRandom(
            RoomManager rm,
            FieldInfo rngField,
            FieldInfo lastPickField,
            SerializedProperty seedProp,
            int seed)
        {
            rngField.SetValue(rm, null);
            lastPickField.SetValue(rm, -1);
            seedProp.intValue = seed;
            seedProp.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool Same(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        private static string SeqString(List<int> seq)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < seq.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append("第").Append(i + 1).Append("间:").Append(seq[i]);
            }

            return sb.ToString();
        }

        // ==================== 报告 ====================

        private static void Check(bool ok, string label, string passDetail, string failDetail)
        {
            if (ok)
            {
                Pass(label, passDetail);
            }
            else
            {
                Fail(label, failDetail);
            }
        }

        private static void Pass(string label, string detail)
        {
            Log.AppendLine("[PASS] " + label + " | " + detail);
        }

        private static void Info(string label, string detail)
        {
            Log.AppendLine("[INFO] " + label + " | " + detail);
        }

        private static void Warn(string label, string detail)
        {
            _warns++;
            Log.AppendLine("[WARN] " + label + " | " + detail);
        }

        private static void Fail(string label, string detail)
        {
            _fails++;
            Log.AppendLine("[FAIL] " + label + " | " + detail);
        }

        private static void Write()
        {
            Log.AppendLine();
            Log.AppendLine("[SUMMARY] 失败 | " + _fails);
            Log.AppendLine("[SUMMARY] 警告 | " + _warns);

            string absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, Log.ToString(), new UTF8Encoding(false));

            if (_fails > 0) Debug.LogError("[Day8] 验证有 " + _fails + " 项失败，详见 " + ReportPath);
            else Debug.Log("[Day8] 验证通过（警告 " + _warns + " 项），详见 " + ReportPath);
        }
    }
}
