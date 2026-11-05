#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 「挥砍残影 / 旋风斩持续判定 / 战吼 / 跑动」这一轮的自检。
    ///
    /// 和 Day9Check / Day8Check 一个套路：不按 Play，直接反射调业务方法。
    /// 能断言的都断言到「行为」而不只是「字段存在」：
    ///   · 战吼真的把房间里的敌人吓住了（IsFeared = true，且 _target 被清空）
    ///   · 旋风斩的 Setup 真的把计时器点着了（IsRunning = true）
    ///   · 跑动帧序真的是 0→1→2→1
    /// </summary>
    public static class BattleCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string RoomPrefabPath = "Assets/_Project/Prefabs/Rooms/Room_01.prefab";
        private const string WhirlwindPath = "Assets/_Project/Data/Skills/Skill_Whirlwind.asset";
        private const string WarcryPath = "Assets/_Project/Data/Skills/Skill_WarCry.asset";
        private const string ReportPath = "_Tuanjie/battle-check-report.txt";

        private static StringBuilder _sb;
        private static int _fail;
        private static int _warn;

        [MenuItem("团结/验证战斗特效与战吼")]
        public static void Run()
        {
            _sb = new StringBuilder();
            _fail = 0;
            _warn = 0;

            _sb.AppendLine("[INFO] 战斗特效 / 旋风斩 / 战吼 / 跑动 自检");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid(), "打开场景", ScenePath, ScenePath);

            CheckEffectScripts();
            CheckWhirlwind();
            CheckWarcry();
            CheckFear();
            CheckRunCycle();
            CheckNoFlyingWave();

            _sb.AppendLine($"[SUMMARY] 失败 | {_fail}");
            _sb.AppendLine($"[SUMMARY] 警告 | {_warn}");

            System.IO.File.WriteAllText(ReportPath, _sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[BattleCheck] 完成\n{_sb}");

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("团结",
                    _fail == 0 ? "战斗特效自检全部通过。\n详见 _Tuanjie/battle-check-report.txt"
                               : $"有 {_fail} 项失败，详见 _Tuanjie/battle-check-report.txt",
                    "好");
            }
        }

        // ---------------- 脚本 ----------------

        private static void CheckEffectScripts()
        {
            Type trail = FindType("SlashTrail");
            Check(trail != null, "挥砍残影脚本", "SlashTrail 存在", "找不到 SlashTrail");

            if (trail != null)
            {
                MethodInfo setup = trail.GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public);
                Check(setup != null, "挥砍残影脚本", "Setup 可用", "SlashTrail 上没有 Setup");
            }

            Type effect = FindType("SlashEffect");
            if (effect == null)
            {
                Check(false, "弧光工厂", "SlashEffect 存在", "找不到 SlashEffect");
                return;
            }

            Check(effect.GetMethod("Swing", BindingFlags.Static | BindingFlags.Public) != null,
                  "弧光工厂", "有 Swing（绕角色扫）", "没有 Swing");
            Check(effect.GetMethod("Thrust", BindingFlags.Static | BindingFlags.Public) != null,
                  "弧光工厂", "有 Thrust（突刺前闪）", "没有 Thrust");
            Check(effect.GetMethod("Spin", BindingFlags.Static | BindingFlags.Public) != null,
                  "弧光工厂", "有 Spin（旋风斩绕一圈）", "没有 Spin");
        }

        private static void CheckNoFlyingWave()
        {
            Type wave = FindType("SlashWave");
            Check(wave == null, "飞行刀风已移除", "SlashWave 已删（改成贴着武器扫）",
                  "SlashWave 还在 —— 飞行刀风应该被残影取代");
        }

        // ---------------- 旋风斩 ----------------

        private static void CheckWhirlwind()
        {
            Type spin = FindType("WhirlwindCast");
            Check(spin != null, "旋风斩", "WhirlwindCast 存在", "找不到 WhirlwindCast");

            if (spin != null)
            {
                Check(spin.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public) != null,
                      "旋风斩", "有 IsRunning", "没有 IsRunning");
                Check(spin.GetProperty("Ticks", BindingFlags.Instance | BindingFlags.Public) != null,
                      "旋风斩", "有 Ticks", "没有 Ticks");
            }

            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(WhirlwindPath);
            if (skill == null)
            {
                Check(false, "旋风斩技能", "能找到资产", $"找不到 {WhirlwindPath}");
                return;
            }

            Check(skill.duration > 0f, "旋风斩技能",
                  $"持续时间 {skill.duration:0.##} 秒 / 每 {skill.tickInterval:0.##} 秒一次",
                  "duration 还是 0 —— 那就还是一次性判定");

            Check(skill.castType == SkillCastType.Melee, "旋风斩技能", "施放方式是 Melee", $"施放方式是 {skill.castType}");
            Check(skill.attack != null, "旋风斩技能", "有判定数据", "没有 Attack Data");

            // 真把组件挂上去点着，确认 Setup 之后计时器是跑起来的
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null || spin == null)
            {
                Check(false, "旋风斩", "能实测 Setup", "找不到玩家或脚本");
                return;
            }

            var caster = player.GetComponent(spin);
            if (caster == null) caster = player.AddComponent(spin);

            MethodInfo setup = spin.GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public);
            var attacker = player.GetComponent<PlayerAttacker>();
            setup.Invoke(caster, new object[]
            {
                attacker, skill.attack, skill.duration, skill.tickInterval, null, Color.white, 1f
            });

            bool running = (bool)spin.GetProperty("IsRunning").GetValue(caster);
            Check(running, "旋风斩", "Setup 之后进入「正在转」状态", "Setup 之后没在转");

            // 清理：把这个临时组件拆掉，别让场景里多出东西
            UnityEngine.Object.DestroyImmediate(caster);
        }

        // ---------------- 战吼 ----------------

        private static void CheckWarcry()
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(WarcryPath);
            if (skill == null)
            {
                Check(false, "战吼技能", "能找到资产", $"找不到 {WarcryPath}");
                return;
            }

            Check(skill.castType == SkillCastType.Warcry, "战吼技能",
                  "施放方式是 Warcry", $"施放方式是 {skill.castType}");
            Check(skill.fearDuration > 0f, "战吼技能",
                  $"恐慌 {skill.fearDuration:0.#} 秒", "恐慌时长是 0");
            Check(skill.fearRadius > 0f, "战吼技能",
                  $"半径 {skill.fearRadius:0.#} 单位", "半径是 0");
            Check(skill.ringSprite != null, "战吼技能", $"圆环贴图 | {skill.ringSprite?.name}", "没接圆环贴图");
            Check(skill.swordSprite != null, "战吼技能", $"巨剑贴图 | {skill.swordSprite?.name}", "没接巨剑贴图");

            Check(FindType("WarCryEffect") != null, "战吼表现", "WarCryEffect 存在", "找不到 WarCryEffect");

            var classData = AssetDatabase.LoadAssetAtPath<ClassData>("Assets/_Project/Data/classes/Class_Warrior.asset");
            if (classData != null)
            {
                bool has = classData.startingSkills != null && classData.startingSkills.Contains(skill);
                Check(has, "战士技能栏", "战吼在战士的起始技能里", "战士的起始技能里没有战吼");
            }
        }

        // ---------------- 恐慌行为 ----------------

        private static void CheckFear()
        {
            Type enemyType = FindType("EnemyController");
            if (enemyType == null)
            {
                Check(false, "恐慌", "EnemyController 存在", "找不到 EnemyController");
                return;
            }

            Check(enemyType.GetMethod("ApplyFear", BindingFlags.Instance | BindingFlags.Public) != null,
                  "恐慌", "有 ApplyFear(duration, source)", "没有 ApplyFear");
            Check(enemyType.GetProperty("IsFeared", BindingFlags.Instance | BindingFlags.Public) != null,
                  "恐慌", "有 IsFeared", "没有 IsFeared");

            // 从房间预制体里拉一个敌人出来实测 —— 编辑模式下 Awake 不跑，得手动叫醒
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefabPath);
            if (prefab == null)
            {
                Check(false, "恐慌实测", "能加载房间预制体", $"找不到 {RoomPrefabPath}");
                return;
            }

            var room = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (room == null)
            {
                Check(false, "恐慌实测", "能实例化房间", "实例化失败");
                return;
            }

            try
            {
                var enemy = room.GetComponentInChildren(enemyType, true) as MonoBehaviour;
                if (enemy == null)
                {
                    Check(false, "恐慌实测", "房间里有敌人", "房间预制体里找不到 EnemyController");
                    return;
                }

                CallAwake(enemy);

                // 编辑模式下整棵树都不会自己 Awake —— 敌人的 Health 也得叫醒，
                // 否则它 Current = 0、IsAlive = false，ApplyFear 会在第一行就 return
                AwakeAll(room);

                MethodInfo apply = enemyType.GetMethod("ApplyFear", BindingFlags.Instance | BindingFlags.Public);
                apply.Invoke(enemy, new object[] { 2f, (Vector2)enemy.transform.position + Vector2.left * 3f });

                bool feared = (bool)enemyType.GetProperty("IsFeared").GetValue(enemy);
                Check(feared, "恐慌实测", "ApplyFear 之后 IsFeared = true", "ApplyFear 之后没进入恐慌");

                FieldInfo target = enemyType.GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic);
                bool targetCleared = target == null || target.GetValue(enemy) == null;
                Check(targetCleared, "恐慌实测", "攻击目标被清空（丢失目标）", "恐慌后 _target 还指着玩家");

                // 战吼效果真跑一遍：半径内的敌人应该被集体吓住
                Type effectType = FindType("WarCryEffect");
                var skill = AssetDatabase.LoadAssetAtPath<SkillData>(WarcryPath);
                if (effectType != null && skill != null)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    var effect = player != null
                        ? player.AddComponent(effectType)
                        : room.AddComponent(effectType);

                    MethodInfo setup = effectType.GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public);
                    // 让战吼以敌人为圆心放，这样无论玩家在哪都在半径内
                    setup.Invoke(effect, new object[] { skill, (Vector2)enemy.transform.position });

                    MethodInfo applyFear = effectType.GetMethod("ApplyFear", BindingFlags.Instance | BindingFlags.NonPublic);
                    applyFear.Invoke(effect, null);

                    int count = (int)effectType.GetProperty("FrightenedCount").GetValue(effect);
                    Check(count > 0, "恐慌实测", $"战吼半径内吓到 {count} 个敌人", "战吼一个都没吓到");

                    UnityEngine.Object.DestroyImmediate(effect);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(room);
            }
        }

        // ---------------- 跑动 ----------------

        private static void CheckRunCycle()
        {
            Type visual = FindType("WarriorVisual");
            if (visual == null)
            {
                Check(false, "跑动", "WarriorVisual 存在", "找不到 WarriorVisual");
                return;
            }

            FieldInfo cycle = visual.GetField("RunCycle", BindingFlags.Static | BindingFlags.NonPublic);
            var expected = new[] { 0, 1, 2, 1 };

            if (cycle == null)
            {
                Check(false, "跑动帧序", "有 RunCycle", "没有 RunCycle 字段");
                return;
            }

            var actual = cycle.GetValue(null) as int[];
            bool same = actual != null && actual.Length == expected.Length;
            if (same)
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    if (actual[i] != expected[i]) same = false;
                }
            }

            Check(same, "跑动帧序",
                  $"往复播放 {string.Join("→", actual ?? Array.Empty<int>())}（不是 0→1→2→3）",
                  "RunCycle 不是 0→1→2→1");

            Check(visual.GetField("runFpsMin", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                  "跑动帧率", "有 runFpsMin（慢走时帧率）", "没有 runFpsMin");
            Check(visual.GetField("runFpsMax", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                  "跑动帧率", "有 runFpsMax（全速时帧率）", "没有 runFpsMax");
            Check(visual.GetField("runFps", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                  "跑动帧率", "旧的定速 runFps 已移除", "旧的 runFps 还留着（会和速度自适应打架）");
        }

        // ---------------- 工具 ----------------

        private static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(name);
                if (type != null) return type;
            }
            return null;
        }

        private static void CallAwake(MonoBehaviour behaviour)
        {
            MethodInfo awake = behaviour.GetType().GetMethod("Awake",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (awake != null) awake.Invoke(behaviour, null);
        }

        /// <summary>
        /// 把一棵树里所有 MonoBehaviour 的 Awake 挨个调一遍。
        /// 编辑模式实例化的预制体不会跑 Awake，而 Health 这类组件的「活着」状态就是在 Awake 里建起来的。
        /// </summary>
        private static void AwakeAll(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Assembly != typeof(EnemyController).Assembly) continue;  // 只叫醒游戏自己的脚本
                CallAwake(behaviour);
            }
        }

        private static void Check(bool ok, string group, string pass, string fail)
        {
            if (ok)
            {
                _sb.AppendLine($"[PASS]|{group}|{pass}");
            }
            else
            {
                _fail++;
                _sb.AppendLine($"[FAIL]|{group}|{fail}");
            }
        }
    }
}
#endif
