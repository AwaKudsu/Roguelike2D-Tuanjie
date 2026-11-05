#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 蓄力诊断：把「按下 → 按住 → 松手」整条链路按真代码走一遍，把每一步的状态打出来。
    ///
    /// 存在的理由：蓄力这种「按时间累积」的东西，光看代码是对的，跑起来不对也看不出在哪一步断的。
    /// 这个诊断伪造 PlayerInputReader 的按键锁存（直接写私有数组），然后反射调 SkillCaster.Update，
    /// 于是可以完全复现「玩家按住 K」这件事，并且把中间状态一条条记下来。
    /// </summary>
    public static class ChargeCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ReportPath = "_Tuanjie/charge-check-report.txt";
        private const string MageClassPath = "Assets/_Project/Data/classes/Class_Mage.asset";

        private static readonly StringBuilder Sb = new StringBuilder();
        private static int _fail;

        [MenuItem("团结/诊断蓄力火球")]
        public static void Run()
        {
            Sb.Clear();
            _fail = 0;

            Sb.AppendLine("[INFO] 蓄力火球诊断（伪造按键锁存，直接驱动 SkillCaster.Update）");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Sb.AppendLine($"[FAIL] 打不开场景 | {ScenePath}");
                Finish();
                return;
            }

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Sb.AppendLine("[FAIL] 找不到玩家");
                Finish();
                return;
            }

            var caster = player.GetComponent<SkillCaster>();
            var reader = player.GetComponent<PlayerInputReader>();
            var stats = player.GetComponent<CharacterStats>();
            var mana = player.GetComponent<ManaPool>();

            // 编辑模式下 Awake 不会自己跑：SkillCaster 的 input / mana 都是在 Awake 里兜底取的，
            // 不叫醒它们，诊断会看到「input 是空的」这种假问题（真按 Play 时它们是有值的）。
            AwakeAll(player);

            Sb.AppendLine($"[INFO] 玩家 | {player.name}");
            Sb.AppendLine($"[INFO] 当前职业 | {(stats != null && stats.Class != null ? stats.Class.className : "无")}");

            if (caster == null || reader == null)
            {
                Sb.AppendLine("[FAIL] 玩家身上缺 SkillCaster 或 PlayerInputReader");
                Finish();
                return;
            }

            // SkillCaster 手里的 input 引用是不是真的接到了这个 reader
            var inputField = typeof(SkillCaster).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic);
            var wired = inputField?.GetValue(caster) as PlayerInputReader;
            Sb.AppendLine(wired != null ? "[PASS] SkillCaster.input 已接 | PlayerInputReader" : "[FAIL] SkillCaster.input 是空的");

            // 换成法师技能栏
            var mage = AssetDatabase.LoadAssetAtPath<ClassData>(MageClassPath);
            if (mage == null)
            {
                Sb.AppendLine($"[FAIL] 找不到 {MageClassPath}");
                Finish();
                return;
            }

            caster.ApplySkills(mage.startingSkills);

            var slots = typeof(SkillCaster).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)
                                         ?.GetValue(caster) as SkillData[];

            for (int i = 0; i < (slots?.Length ?? 0); i++)
            {
                SkillData s = slots[i];
                Sb.AppendLine(s == null
                    ? $"[INFO] 槽 {i + 1} | 空"
                    : $"[INFO] 槽 {i + 1} | {s.displayName} / 类型={s.castType} / 可蓄力={s.chargeable} / 耗蓝={s.manaCost} / 冷却={s.cooldown}");
            }

            Sb.AppendLine($"[INFO] Time.deltaTime | {Time.deltaTime:0.0000}");

            if (mana != null) Sb.AppendLine($"[INFO] 当前法力 | {mana.Current:0.0}");

            // ---------------- 模拟：按下技能 1 ----------------
            ResetLatches(reader);
            SetPressed(reader, 0, true);
            SetHeld(reader, 0, true);
            InvokeUpdate(caster);

            bool charging = caster.IsCharging;
            Report(charging, charging ? "[PASS] 按下后进入蓄力 | 槽位 " + (caster.ChargingSlot + 1) : "[FAIL] 按下后没有进入蓄力（走的应该是瞬发分支）");

            // ---------------- 模拟：按住 5 帧 ----------------
            for (int i = 0; i < 5 && caster.IsCharging; i++)
            {
                SetPressed(reader, 0, false);
                SetHeld(reader, 0, true);
                InvokeUpdate(caster);
            }

            Sb.AppendLine($"[INFO] 按住 5 帧后 | 蓄力={caster.IsCharging} / 进度={caster.ChargeRatio:0.00}");

            // ---------------- 模拟：松手 ----------------
            if (caster.IsCharging)
            {
                SetHeld(reader, 0, false);
                SetReleased(reader, 0, true);

                int before = CountProjectiles();
                InvokeUpdate(caster);
                int after = CountProjectiles();

                Report(!caster.IsCharging, caster.IsCharging ? "[FAIL] 松手后还在蓄力" : "[PASS] 松手后结束蓄力");
                Report(caster.GetCooldownRemaining(0) > 0f, $"[INFO] 松手后冷却 | {caster.GetCooldownRemaining(0):0.00} 秒");
                Report(after > before, $"[INFO] 松手后投射物数量 | {before} → {after}");
            }

            ResetLatches(reader);

            // 回归用例要排在换职业之前：换完职业会轮回到战士，槽 1 就不再是火球了
            CheckStaleReleaseLatch(reader, caster);

            // 换职业链路：技能栏 / 动画层是不是跟着 ClassChanged 一起换了
            CheckClassSwitch(player, caster, "换职业后");

            Finish();
        }

        /// <summary>
        /// 回归用例：「有时候蓄不了力」。
        /// 上一轮松手如果没被消费（按在冷却里 / 蓄力被硬直打断后松手），reader 里会留一枚
        /// 「已松开」锁存。下一次按下若不清掉它，SkillCaster 会在下一帧就当成已松手、以 0% 发射。
        /// </summary>
        private static void CheckStaleReleaseLatch(PlayerInputReader reader, SkillCaster caster)
        {
            // 先造出「上一次松手没人消费」的局面：只置锁存，不跑 Update
            ResetLatches(reader);
            SetReleased(reader, 0, true);

            // 前面的用例刚发射过一发，冷却还挂着：编辑模式下 Time.deltaTime = 0，冷却是不会自己降的。
            // 不清掉它，这条用例会因为「冷却中」而失败 —— 那是另一回事。
            ClearCooldowns(caster);

            // 然后正常按下并按住
            SetPressed(reader, 0, true);
            SetHeld(reader, 0, true);
            InvokeUpdate(caster);

            bool started = caster.IsCharging;

            // 再跑一帧「仍然按住」：不该被旧的松手锁存顶掉
            SetPressed(reader, 0, false);
            SetHeld(reader, 0, true);
            InvokeUpdate(caster);

            bool stillCharging = caster.IsCharging;

            Report(started && stillCharging,
                started && stillCharging
                    ? "[PASS] 旧松手锁存不影响下一次蓄力 | 按住期间不会被立刻放掉"
                    : "[FAIL] 旧松手锁存把蓄力顶掉了 | 按下=" + started + " 第二帧仍在蓄=" + stillCharging);

            // 收尾：正常松手放掉，清干净
            SetHeld(reader, 0, false);
            SetReleased(reader, 0, true);
            InvokeUpdate(caster);
            ResetLatches(reader);
        }

        /// <summary>
        /// 换职业这条链路：Esc →「切换职业」→ CharacterStats.ApplyClass → ClassChanged
        ///   → SkillCaster 换技能栏、PlayerVisualSwitcher 换动画。
        /// 编辑模式下事件订阅要手动补上（OnEnable 不会自己跑），否则会看到「切了但技能栏没变」的假象。
        /// </summary>
        private static void CheckClassSwitch(GameObject player, SkillCaster caster, string header)
        {
            var switcher = player.GetComponent<PlayerVisualSwitcher>();
            var warrior = player.GetComponent<WarriorVisual>();
            var mage = player.GetComponent<MageVisual>();

            if (switcher == null)
            {
                Sb.AppendLine("[FAIL] 切换器 | 玩家身上没有 PlayerVisualSwitcher");
                _fail++;
                return;
            }

            InvokeIfPresent(caster, "OnEnable");      // 订阅 ClassChanged
            InvokeIfPresent(switcher, "OnEnable");

            switcher.CycleClass();

            string slot0 = caster.GetSkill(0) != null ? caster.GetSkill(0).displayName : "空";
            string slot1 = caster.GetSkill(1) != null ? caster.GetSkill(1).displayName : "空";

            Sb.AppendLine($"[INFO] {header} | 职业={switcher.CurrentClassName} / 槽1={slot0} / 槽2={slot1}" +
                          $" / 战士动画={warrior != null && warrior.enabled} / 法师动画={mage != null && mage.enabled}");

            bool isMage = switcher.IsMage;
            bool slotsOk = slot0.Contains("火球");
            bool visualsOk = mage != null && mage.enabled && warrior != null && !warrior.enabled;

            Report(isMage, isMage ? "[PASS] 切到法师 | 职业表里换到了法师" : "[FAIL] 切到法师 | 还是战士");
            Report(slotsOk, slotsOk ? "[PASS] 技能栏跟着换 | 槽 1 变成火球" : "[FAIL] 技能栏没换 | 槽 1 还是 " + slot0);
            Report(visualsOk, visualsOk ? "[PASS] 动画层跟着换 | 法师开、战士关" : "[FAIL] 动画层没换");

            // 换回战士，别把场景留在法师状态
            switcher.CycleClass();
        }

        private static void InvokeIfPresent(object target, string method)
        {
            if (target == null) return;
            MethodInfo m = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, Type.EmptyTypes, null);
            if (m == null) return;
            try { m.Invoke(target, null); }
            catch (Exception) { /* 编辑模式下的订阅失败不影响结论，下面会直接看结果 */ }
        }

        // ---------------- 伪造输入 ----------------

        /// <summary>把物体上这一批脚本的 Awake 都叫一遍（编辑模式不会自己跑）。</summary>
        private static void AwakeAll(GameObject root)
        {
            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            var awak = BindingFlags.Instance | BindingFlags.NonPublic;

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Assembly != typeof(SkillCaster).Assembly) continue;

                MethodInfo awake = behaviour.GetType().GetMethod("Awake", awak, null, Type.EmptyTypes, null);
                if (awake != null && awake.DeclaringType == behaviour.GetType())
                {
                    try { awake.Invoke(behaviour, null); }
                    catch (Exception e) { Sb.AppendLine($"[WARN] {behaviour.GetType().Name}.Awake 抛异常 | {e.InnerException?.Message ?? e.Message}"); }
                }
            }
        }

        private static bool[] Latch(PlayerInputReader reader, string field)
            => typeof(PlayerInputReader).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                                        ?.GetValue(reader) as bool[];

        private static void ResetLatches(PlayerInputReader reader)
        {
            Clear(Latch(reader, "_skillPressedLatch"));
            Clear(Latch(reader, "_skillReleasedLatch"));
            Clear(Latch(reader, "_skillHeld"));
        }

        private static void Clear(bool[] array)
        {
            if (array == null) return;
            for (int i = 0; i < array.Length; i++) array[i] = false;
        }

        /// <summary>把技能冷却清零。编辑模式下 Time.deltaTime = 0，冷却是不会自己降下去的。</summary>
        private static void ClearCooldowns(SkillCaster caster)
        {
            float[] cooldowns = typeof(SkillCaster)
                .GetField("_cooldowns", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(caster) as float[];

            if (cooldowns == null) return;
            for (int i = 0; i < cooldowns.Length; i++) cooldowns[i] = 0f;
        }

        private static void SetPressed(PlayerInputReader reader, int slot, bool value) => Put(Latch(reader, "_skillPressedLatch"), slot, value);

        private static void SetReleased(PlayerInputReader reader, int slot, bool value) => Put(Latch(reader, "_skillReleasedLatch"), slot, value);

        private static void SetHeld(PlayerInputReader reader, int slot, bool value) => Put(Latch(reader, "_skillHeld"), slot, value);

        private static void Put(bool[] array, int slot, bool value)
        {
            if (array == null || slot < 0 || slot >= array.Length) return;
            array[slot] = value;
        }

        private static void InvokeUpdate(SkillCaster caster)
        {
            MethodInfo update = typeof(SkillCaster).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            update?.Invoke(caster, null);
        }

        private static int CountProjectiles()
            => UnityEngine.Object.FindObjectsOfType<Projectile>(true).Length;

        // ---------------- 报告 ----------------

        private static void Report(bool ok, string line)
        {
            Sb.AppendLine(ok ? line : "[FAIL] " + line);
            if (!ok) _fail++;
        }

        private static void Finish()
        {
            Sb.AppendLine($"[SUMMARY] 失败 | {_fail}");
            System.IO.File.WriteAllText(ReportPath, Sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[ChargeCheck] 完成\n{Sb}");

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("蓄力诊断", $"失败 {_fail} 项\n报告：{ReportPath}", "好");
            }
        }
    }
}
#endif
