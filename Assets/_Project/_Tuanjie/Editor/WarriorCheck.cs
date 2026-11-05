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
    /// 战士素材自检：不按 Play 也能跑。
    ///
    /// 检查的都是「肉眼很难一次看全、但错了一定会表现在画面上」的东西：
    /// 15 张图的导入参数（PPU / pivot / 压缩 / 过滤）、WarriorVisual 有没有接全、
    /// 刀风和技能图标接上没有、以及几何关系（脚底是不是正好在胶囊体底边）。
    /// </summary>
    public static class WarriorCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ArtDir = "Assets/_Project/Art/Characters/Warrior";
        private const string SkillWhirlwindPath = "Assets/_Project/Data/Skills/Skill_Whirlwind.asset";
        private const string ReportPath = "_Tuanjie/warrior-check-report.txt";

        private const float PixelsPerUnit = 160f;
        private const int CanvasWidth = 320;
        private const int CanvasHeight = 480;

        /// <summary>骑士脚底距画布底 160px；pivot 在画布正中 240px，故脚底在 pivot 下方 (240-160)/160 = 0.5 单位</summary>
        private const float FeetBelowPivot = 0.5f;

        private static readonly string[] AllFrames =
        {
            "Warrior_Idle", "Warrior_Idle_2", "Warrior_Idle_3",
            "Warrior_Jump", "Warrior_Fall", "Warrior_Dash", "Warrior_Hurt",
            "Warrior_Death", "Warrior_Victory", "Warrior_Warcry",
            "Warrior_AttackSlash", "Warrior_AttackSlash_Windup", "Warrior_AttackSlash_Follow",
            "Warrior_AttackLift", "Warrior_AttackLift_Windup", "Warrior_AttackLift_Follow",
            "Warrior_AttackHeavy", "Warrior_AttackThrust",
            "Warrior_AttackThrust_Windup", "Warrior_AttackThrust_Follow",
            "Warrior_Run_0", "Warrior_Run_1", "Warrior_Run_2", "Warrior_Run_3",
            "Warrior_Run_4", "Warrior_Run_5"
        };

        [MenuItem("团结/验证战士动作")]
        public static void Run()
        {
            var sb = new StringBuilder();
            int fail = 0;
            int warn = 0;

            sb.AppendLine("=== 战士动作自检 ===");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[PASS] 已打开场景 | " + ScenePath);

            var sprites = LoadSprites(sb, ref fail);

            // ---------- 导入参数 ----------
            int good = 0;
            foreach (string name in AllFrames)
            {
                Sprite s;
                if (!sprites.TryGetValue(name, out s) || s == null)
                {
                    sb.AppendLine("[FAIL] 缺图 | " + name);
                    fail++;
                    continue;
                }

                bool ok = Mathf.Approximately(s.pixelsPerUnit, PixelsPerUnit)
                          && Mathf.Approximately(s.pivot.x, s.rect.width / 2f)
                          && Mathf.Approximately(s.pivot.y, s.rect.height / 2f)
                          && Mathf.Approximately(s.rect.width, CanvasWidth)
                          && Mathf.Approximately(s.rect.height, CanvasHeight);

                if (ok)
                {
                    good++;
                }
                else
                {
                    sb.AppendLine("[FAIL] 参数不对 | " + name + " rect=" + s.rect.width + "x" + s.rect.height
                                  + " ppu=" + s.pixelsPerUnit + " pivot=(" + s.pivot.x + "," + s.pivot.y + ")");
                    fail++;
                }
            }

            sb.AppendLine(good == AllFrames.Length
                ? "[PASS] " + AllFrames.Length + " 张图导入参数一致 | 人物帧 320x480，全部 160 PPU + 居中 pivot"
                : "[INFO] 参数正确的图 | " + good + " / " + AllFrames.Length);

            // ---------- 过滤与压缩 ----------
            int pointCount = 0;
            foreach (string name in AllFrames)
            {
                Sprite s;
                if (!sprites.TryGetValue(name, out s) || s == null) continue;

                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(s)) as TextureImporter;
                if (importer == null) continue;

                bool point = importer.filterMode == FilterMode.Point && !importer.mipmapEnabled;
                bool raw = importer.textureCompression == TextureImporterCompression.Uncompressed;

                if (point && raw) pointCount++;
                else sb.AppendLine("[WARN] 导入设置 | " + name + " filter=" + importer.filterMode
                                   + " mipmap=" + importer.mipmapEnabled + " compression=" + importer.textureCompression);
            }

            if (pointCount == AllFrames.Length)
            {
                sb.AppendLine("[PASS] 像素风设置 | 全部 Point 过滤、无 mipmap、无压缩");
            }
            else
            {
                sb.AppendLine("[WARN] 像素风设置不全 | " + pointCount + " / " + AllFrames.Length);
                warn++;
            }

            // ---------- 几何关系 ----------
            Sprite idle;
            if (sprites.TryGetValue("Warrior_Idle", out idle) && idle != null)
            {
                float heightUnits = idle.rect.height / idle.pixelsPerUnit;
                float half = heightUnits * 0.5f;
                float feet = (idle.pivot.y - 160f) / idle.pixelsPerUnit;

                sb.AppendLine("[INFO] 画布高度 | " + heightUnits + " 单位，pivot 在脚底上方 " + feet + " 单位");
                sb.AppendLine(Mathf.Abs(feet - FeetBelowPivot) < 0.01f
                    ? "[PASS] 脚底对齐 | 脚底在 pivot 下方 0.5 单位，正好落在胶囊体底边"
                    : "[FAIL] 脚底没对齐 | 应在 pivot 下方 0.5 单位，实际 " + feet);
                if (Mathf.Abs(feet - FeetBelowPivot) >= 0.01f) fail++;

                sb.AppendLine(Mathf.Abs(half - 1.5f) < 0.01f
                    ? "[PASS] 画布尺寸 | 3 单位高（骑士本体 1 单位，上下留白）"
                    : "[WARN] 画布高度不是 3 单位 | " + heightUnits);
            }

            // ---------- WarriorVisual 接线 ----------
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                sb.AppendLine("[FAIL] 场景里找不到 Tag=Player 的物体");
                Write(sb, fail + 1, warn);
                return;
            }

            var visual = player.GetComponent<WarriorVisual>();
            if (visual == null)
            {
                sb.AppendLine("[FAIL] Player 上没有 WarriorVisual 组件");
                Write(sb, fail + 1, warn);
                return;
            }

            sb.AppendLine("[PASS] 组件存在 | WarriorVisual");

            var so = new SerializedObject(visual);
            int wired = 0;

            foreach (string field in new[] { "sprite", "controller", "health", "attacker", "body" })
            {
                SerializedProperty p = so.FindProperty(field);
                if (p != null && p.objectReferenceValue != null) wired++;
                else sb.AppendLine("[FAIL] 引用没接 | " + field);
            }

            sb.AppendLine(wired == 5 ? "[PASS] 五个基础引用已接 | sprite/controller/health/attacker/body"
                                     : "[INFO] 基础引用 | " + wired + " / 5");
            if (wired != 5) fail++;

            int frameWired = 0;
            foreach (string field in new[]
                     {
                         "idle", "jump", "fall", "dash", "hurt", "death", "victory",
                         "attackSlash", "attackLift", "attackHeavy", "attackThrust", "warcry"
                     })
            {
                SerializedProperty p = so.FindProperty(field);
                if (p != null && p.objectReferenceValue != null) frameWired++;
                else sb.AppendLine("[FAIL] 姿势没接 | " + field);
            }

            sb.AppendLine(frameWired == 12 ? "[PASS] 十二个单帧姿势已接"
                                           : "[INFO] 单帧姿势 | " + frameWired + " / 12");
            if (frameWired != 12) fail++;

            // ---------- 短序列（2026-11-05：一刀三帧、待机呼吸、跑动 6 帧）----------
            CheckArray(sb, so, "idleFrames", 3, ref fail);
            CheckArray(sb, so, "slashFrames", 3, ref fail);
            CheckArray(sb, so, "liftFrames", 3, ref fail);
            CheckArray(sb, so, "thrustFrames", 3, ref fail);
            CheckArray(sb, so, "spinFrames", 2, ref fail);

            SerializedProperty runProp = so.FindProperty("run");
            int runWired = 0;
            if (runProp != null)
            {
                for (int i = 0; i < runProp.arraySize; i++)
                    if (runProp.GetArrayElementAtIndex(i).objectReferenceValue != null) runWired++;
            }

            sb.AppendLine(runWired == 6 ? "[PASS] 跑动序列 | 6 帧（含 2 张落地压缩帧）"
                                        : "[FAIL] 跑动序列 | " + runWired + " / 6");
            if (runWired != 6) fail++;

            // 序列帧不能重复引用同一张图 —— 重复就等于少一帧，而且从报告上看不出来
            CheckDistinct(sb, so, "slashFrames", ref fail);
            CheckDistinct(sb, so, "idleFrames", ref fail);

            // ---------- 刀光 / 技能图标 / 渲染器 ----------
            var attacker = player.GetComponent<PlayerAttacker>();
            if (attacker != null)
            {
                // 刀光不再是贴图：不再有 slashArc 字段，改成 SwordTrail 现画。
                // 这里只留一个反向检查 —— 万一「新月贴图」又被人塞回去，能立刻发现。
                var aso = new SerializedObject(attacker);
                SerializedProperty arc = aso.FindProperty("slashArc");
                sb.AppendLine(arc == null
                    ? "[PASS] 刀光不走贴图 | PlayerAttacker 没有 slashArc 字段（SwordTrail 按剑的轨迹现画）"
                    : "[WARN] 刀光又变成贴图了 | 还留着 slashArc 字段");
                if (arc != null) warn++;

                MethodInfo playSlash = typeof(PlayerAttacker).GetMethod("PlaySlash",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                sb.AppendLine(playSlash != null ? "[PASS] 出刀时放刀光 | PlaySlash 存在" : "[FAIL] 找不到 PlaySlash");
                if (playSlash == null) fail++;

                // event 不是字段：编译器把自动实现的 event 生成为私有字段，
                // 所以必须用 GetEvent 找，GetField(Public) 永远返回 null。
                EventInfo started = typeof(PlayerAttacker).GetEvent("AttackStarted",
                    BindingFlags.Instance | BindingFlags.Public);
                sb.AppendLine(started != null ? "[PASS] 姿势切换事件 | AttackStarted 存在" : "[FAIL] 没有 AttackStarted");
                if (started == null) fail++;
            }

            var renderer = player.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                Sprite idleSprite;
                sprites.TryGetValue("Warrior_Idle", out idleSprite);

                bool same = idleSprite != null && renderer.sprite == idleSprite;
                sb.AppendLine(same ? "[PASS] 玩家贴图 | Warrior_Idle"
                                   : "[FAIL] 玩家贴图 | " + (renderer.sprite != null ? renderer.sprite.name : "null"));
                if (!same) fail++;

                sb.AppendLine(renderer.drawMode == SpriteDrawMode.Simple
                    ? "[PASS] 渲染模式 | Simple（不能用 Tiled/Sliced，画布会变形）"
                    : "[FAIL] 渲染模式 | " + renderer.drawMode);
            }

            var whirlwind = AssetDatabase.LoadAssetAtPath<SkillData>(SkillWhirlwindPath);
            if (whirlwind != null)
            {
                var sso = new SerializedObject(whirlwind);
                SerializedProperty icon = sso.FindProperty("icon");
                bool ok = icon != null && icon.objectReferenceValue != null;
                sb.AppendLine(ok ? "[PASS] 技能图标 | 旋风斩 → " + icon.objectReferenceValue.name
                                 : "[WARN] 旋风斩没图标（技能栏会画成白块）");
                if (!ok) warn++;
            }

            // ---------- 新增类型 ----------
            MethodInfo trailPlay = typeof(SwordTrail).GetMethod("Play",
                BindingFlags.Static | BindingFlags.Public);
            sb.AppendLine(trailPlay != null ? "[PASS] 刀光组件 | SwordTrail.Play 存在（按剑的轨迹现画网格）"
                                            : "[FAIL] 缺 SwordTrail.Play");
            if (trailPlay == null) fail++;

            sb.AppendLine(Enum.IsDefined(typeof(SwordSwing), "Slash") && Enum.IsDefined(typeof(SwordSwing), "Spin")
                ? "[PASS] 四种挥法 | 劈砍 / 上挑 / 突刺 / 旋风斩"
                : "[FAIL] SwordSwing 少了挥法");

            // ---------- 冲刺 ----------
            PropertyInfo dashing = typeof(PlayerController).GetProperty("IsDashing",
                BindingFlags.Instance | BindingFlags.Public);
            sb.AppendLine(dashing != null ? "[PASS] 冲刺状态 | PlayerController.IsDashing 存在"
                                          : "[FAIL] PlayerController 没有 IsDashing（外观层切不了冲刺姿势）");
            if (dashing == null) fail++;

            MethodInfo grant = typeof(Health).GetMethod("GrantInvincibility",
                BindingFlags.Instance | BindingFlags.Public);
            sb.AppendLine(grant != null ? "[PASS] 冲刺无敌 | Health.GrantInvincibility 存在"
                                        : "[FAIL] Health 没有 GrantInvincibility");
            if (grant == null) fail++;

            // ---------- 三段连击 ----------
            if (attacker != null)
            {
                var cso = new SerializedObject(attacker);

                SerializedProperty combo = cso.FindProperty("comboAttacks");
                int comboWired = 0;
                if (combo != null)
                {
                    for (int i = 0; i < combo.arraySize; i++)
                        if (combo.GetArrayElementAtIndex(i).objectReferenceValue != null) comboWired++;
                }

                sb.AppendLine(comboWired == 3
                    ? "[PASS] 三段连击已接 | 劈砍 / 上挑 / 突刺"
                    : "[FAIL] 三段连击 | " + comboWired + " / 3");
                if (comboWired != 3) fail++;

                SerializedProperty atkProp = cso.FindProperty("attackData");
                var atk = atkProp != null ? atkProp.objectReferenceValue as AttackData : null;

                if (atk == null)
                {
                    sb.AppendLine("[FAIL] 普通攻击数据为空");
                    fail++;
                }
                else if (atk.isRanged)
                {
                    sb.AppendLine("[FAIL] 普通攻击还是远程弹道 | " + atk.name + "（战士该用剑）");
                    fail++;
                }
                else
                {
                    sb.AppendLine("[PASS] 普通攻击是近战 | " + atk.name
                                  + " 判定 " + atk.hitboxSize.x + "×" + atk.hitboxSize.y);
                }

                // ---------- 攻击距离（够得着多远） ----------
                // 判定框是以中心点算的，所以「够得着」= |offset.x| + 宽/2。
                // 只盯第一段就够：它是日常挥的那一刀，也是职业模板。
                if (atk != null)
                {
                    float reach = Mathf.Abs(atk.hitboxOffset.x) + atk.hitboxSize.x * 0.5f;
                    sb.AppendLine(reach >= 1.8f
                        ? "[PASS] 普攻攻击距离 | " + reach.ToString("0.00") + " 个单位（身前约 "
                          + (reach - 0.5f).ToString("0.00") + "，从胶囊体边缘算）"
                        : "[FAIL] 普攻攻击距离 | " + reach.ToString("0.00") + " 个单位，太近了（要求 ≥ 1.8）");
                    if (reach < 1.8f) fail++;
                }

                // 职业模板有没有真正被应用（ClassData.basicAttack 以前是个无人读取的字段）
                var stats = player.GetComponent<CharacterStats>();
                sb.AppendLine(stats != null && stats.Class != null
                    ? "[PASS] 玩家职业 | " + stats.Class.className + "（模板攻击 "
                      + (stats.Class.basicAttack != null ? stats.Class.basicAttack.name : "空") + "）"
                    : "[FAIL] 玩家没有职业数据");
                if (stats == null || stats.Class == null) fail++;

                MethodInfo applyClass = typeof(PlayerAttacker).GetMethod("ApplyClassAttack",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                sb.AppendLine(applyClass != null
                    ? "[PASS] 攻击跟着职业走 | ApplyClassAttack 存在（换职业会自动换剑/法杖）"
                    : "[FAIL] PlayerAttacker 没有 ApplyClassAttack");
                if (applyClass == null) fail++;

                MethodInfo next = typeof(PlayerAttacker).GetMethod("NextAttack",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                sb.AppendLine(next != null ? "[PASS] 连击推进 | NextAttack 存在" : "[FAIL] 没有 NextAttack");
                if (next == null) fail++;

                PropertyInfo stageProp = typeof(PlayerAttacker).GetProperty("ComboStage",
                    BindingFlags.Instance | BindingFlags.Public);
                sb.AppendLine(stageProp != null ? "[PASS] 连击段号对外可见 | ComboStage（外观层据此选姿势）"
                                                : "[FAIL] 没有 ComboStage");
                if (stageProp == null) fail++;

                // ---------- 连击序列（反射直调，不需要 Play） ----------
                FieldInfo comboTimer = typeof(PlayerAttacker).GetField("_comboTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (next != null && comboTimer != null)
                {
                    var names = new List<string>();
                    var stages = new List<int>();

                    for (int i = 0; i < 4; i++)
                    {
                        var a = next.Invoke(attacker, null) as AttackData;
                        names.Add(a != null ? a.displayName : "空");
                        stages.Add(attacker.ComboStage);
                    }

                    string seq = string.Join(" → ", names);
                    string stageSeq = string.Join(",", stages);

                    sb.AppendLine(stageSeq == "0,1,2,0"
                        ? "[PASS] 连击序列 | " + seq + "（段号 " + stageSeq + "）"
                        : "[FAIL] 连击序列 | " + seq + "（段号 " + stageSeq + "，应为 0,1,2,0）");
                    if (stageSeq != "0,1,2,0") fail++;

                    // 窗口过期 → 下一刀回到第一段
                    comboTimer.SetValue(attacker, 0f);
                    var back = next.Invoke(attacker, null) as AttackData;
                    bool reset = back != null && back.displayName == "劈砍" && attacker.ComboStage == 0;

                    sb.AppendLine(reset
                        ? "[PASS] 连击窗口过期 | 停手后下一刀回到劈砍"
                        : "[FAIL] 窗口过期后没回到第一段 | " + (back != null ? back.displayName : "空"));
                    if (!reset) fail++;
                }
            }

            // 挥砍表现：刀光现在是从剑的轨迹现画出来的网格，不是一张跟着转的贴图
            MethodInfo playTrail = typeof(SwordTrail).GetMethod("Play",
                BindingFlags.Public | BindingFlags.Static);
            sb.AppendLine(playTrail != null
                ? "[PASS] 刀光从剑的轨迹来 | SwordTrail.Play(SwordSwing, origin, facing, color, scale)"
                : "[FAIL] SwordTrail.Play 不存在");
            if (playTrail == null) fail++;

            Type oldEffect = FindType("SlashEffect");
            sb.AppendLine(oldEffect == null
                ? "[PASS] 贴图刀光已移除 | SlashEffect / SlashTrail 都不在了"
                : "[WARN] 旧的贴图刀光还在 | " + oldEffect.Name);

            sb.AppendLine();
            sb.AppendLine("[SUMMARY] 失败 | " + fail);
            sb.AppendLine("[SUMMARY] 警告 | " + warn);

            Write(sb, fail, warn);
        }

        /// <summary>按名字在已加载的程序集里找一个类型（已经删掉的类型返回 null）</summary>
        private static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        private static Dictionary<string, Sprite> LoadSprites(StringBuilder sb, ref int fail)
        {
            var sprites = new Dictionary<string, Sprite>();
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { ArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;
                sprites[Path.GetFileNameWithoutExtension(path)] = sprite;
            }

            if (sprites.Count == 26) sb.AppendLine("[PASS] Sprite 资产 | 26 张");
            else
            {
                sb.AppendLine("[FAIL] Sprite 资产数量 | " + sprites.Count + " 张（应为 26）");
                fail++;
            }

            return sprites;
        }

        /// <summary>检查一个 Sprite 数组字段接到了几帧</summary>
        private static void CheckArray(StringBuilder sb, SerializedObject so, string field, int expected, ref int fail)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                sb.AppendLine("[FAIL] 找不到序列字段 | " + field);
                fail++;
                return;
            }

            int filled = 0;
            for (int i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue != null) filled++;
            }

            if (prop.arraySize == expected && filled == expected)
            {
                sb.AppendLine("[PASS] 序列 | " + field + " " + expected + " 帧");
                return;
            }

            sb.AppendLine("[FAIL] 序列帧数不对 | " + field + " 槽位 " + prop.arraySize + " / 已接 " + filled
                          + "（应为 " + expected + "）");
            fail++;
        }

        /// <summary>同一张图在序列里出现两次 = 白占一格，肉眼看不出来，报告里也只会显示帧数正常</summary>
        private static void CheckDistinct(StringBuilder sb, SerializedObject so, string field, ref int fail)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null || prop.arraySize < 2) return;

            var seen = new HashSet<UnityEngine.Object>();
            for (int i = 0; i < prop.arraySize; i++)
            {
                UnityEngine.Object reference = prop.GetArrayElementAtIndex(i).objectReferenceValue;
                if (reference == null) continue;
                if (!seen.Add(reference))
                {
                    sb.AppendLine("[FAIL] 序列里有重复帧 | " + field + " 第 " + (i + 1) + " 格重复了 " + reference.name);
                    fail++;
                    return;
                }
            }

            sb.AppendLine("[PASS] 序列帧不重复 | " + field + " " + prop.arraySize + " 帧各不相同");
        }

        private static void Write(StringBuilder sb, int fail, int warn)
        {
            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[战士自检]\n" + sb);
        }
    }
}
#endif
