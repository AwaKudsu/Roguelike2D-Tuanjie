#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 法师相关的自检：素材参数、表现层接线、蓄力链路、回血链路。
    /// 不按 Play 也能跑（反射直调业务方法），跑完不存盘。
    /// </summary>
    public static class MageCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ArtDir = "Assets/_Project/Art/Characters/Mage";
        private const string ReportPath = "_Tuanjie/mage-check-report.txt";

        private static readonly string[] Frames =
        {
            "Mage_Idle", "Mage_Idle_2", "Mage_Idle_3",
            "Mage_Run_0", "Mage_Run_1", "Mage_Run_2", "Mage_Run_3", "Mage_Run_4", "Mage_Run_5",
            "Mage_Attack_0", "Mage_Attack_1", "Mage_Attack_2", "Mage_Attack_3",
            "Mage_Cast_0", "Mage_Cast_1", "Mage_Cast_2",
            "Mage_Jump", "Mage_Hurt", "Mage_Death",
        };

        [MenuItem("团结/验证法师素材与技能")]
        public static void Run()
        {
            var sb = new StringBuilder();
            int fail = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[PASS] 已打开场景 | " + ScenePath);

            CheckSprites(sb, ref fail);
            CheckPlayerWiring(sb, ref fail);
            CheckChargeChain(sb, ref fail);
            CheckHealChain(sb, ref fail);
            CheckSkills(sb, ref fail);

            WriteReport(sb, fail);

            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("验证法师", fail == 0 ? "全部通过" : $"{fail} 项失败", "好");
        }

        // ---------------- 素材 ----------------

        private static void CheckSprites(StringBuilder sb, ref int fail)
        {
            var sprites = new Dictionary<string, Sprite>();
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { ArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                sprites[Path.GetFileNameWithoutExtension(path)] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            Report(sb, sprites.Count == 21, $"Sprite 资产 | {sprites.Count} 张", ref fail);

            int badParams = 0;
            foreach (string name in Frames)
            {
                if (!sprites.TryGetValue(name, out Sprite sprite) || sprite == null)
                {
                    Report(sb, false, $"缺帧 | {name}", ref fail);
                    continue;
                }

                bool ok = Mathf.Approximately(sprite.pixelsPerUnit, 160f)
                          && Mathf.Approximately(sprite.rect.width, 320f)
                          && Mathf.Approximately(sprite.rect.height, 480f);

                if (!ok)
                {
                    badParams++;
                    sb.AppendLine($"[FAIL] 参数不对 | {name} rect={sprite.rect.width}x{sprite.rect.height} ppu={sprite.pixelsPerUnit}");
                    fail++;
                }
            }

            if (badParams == 0) sb.AppendLine($"[PASS] 人物帧参数一致 | {Frames.Length} 张都是 320x480 / 160 PPU");

            if (sprites.TryGetValue("Mage_Ring", out Sprite ring) && ring != null)
            {
                bool square = Mathf.Approximately(ring.rect.width, ring.rect.height);
                Report(sb, square && Mathf.Approximately(ring.pixelsPerUnit, 160f),
                       $"法阵贴图 | {ring.rect.width}x{ring.rect.height} ppu={ring.pixelsPerUnit}", ref fail);
            }
            else Report(sb, false, "法阵贴图 | 找不到 Mage_Ring", ref fail);

            if (sprites.TryGetValue("Mage_Fireball", out Sprite ball) && ball != null)
                Report(sb, true, $"火球贴图 | {ball.rect.width}x{ball.rect.height} ppu={ball.pixelsPerUnit}", ref fail);
        }

        // ---------------- 玩家接线 ----------------

        private static void CheckPlayerWiring(StringBuilder sb, ref int fail)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Report(sb, false, "找不到玩家", ref fail);
                return;
            }

            var mage = player.GetComponent<MageVisual>();
            Report(sb, mage != null, "玩家身上有 MageVisual", ref fail);
            if (mage == null) return;

            var so = new SerializedObject(mage);
            Report(sb, GetArray(so, "idleFrames") == 3, $"待机序列 | {GetArray(so, "idleFrames")} 帧", ref fail);
            Report(sb, GetArray(so, "run") == 6, $"跑动序列 | {GetArray(so, "run")} 帧", ref fail);
            Report(sb, GetArray(so, "attackFrames") == 4, $"攻击序列 | {GetArray(so, "attackFrames")} 帧", ref fail);
            Report(sb, GetArray(so, "castFrames") == 3, $"施法序列 | {GetArray(so, "castFrames")} 帧", ref fail);

            int refs = 0;
            foreach (string field in new[] { "sprite", "controller", "health", "attacker", "body" })
            {
                SerializedProperty p = so.FindProperty(field);
                if (p != null && p.objectReferenceValue != null) refs++;
            }
            Report(sb, refs == 5, $"组件引用 | {refs}/5 已接", ref fail);

            int singles = 0;
            foreach (string field in new[] { "jump", "hurt", "death" })
            {
                SerializedProperty p = so.FindProperty(field);
                if (p != null && p.objectReferenceValue != null) singles++;
            }
            Report(sb, singles == 3, $"单帧姿势 | {singles}/3 已接（跳/受击/死亡）", ref fail);

            // 两套动画都实现了接口
            bool warriorAnim = typeof(IPlayerAnim).IsAssignableFrom(typeof(WarriorVisual));
            bool mageAnim = typeof(IPlayerAnim).IsAssignableFrom(typeof(MageVisual));
            Report(sb, warriorAnim && mageAnim, "两套动画都实现 IPlayerAnim | 技能层不用区分职业", ref fail);

            var switcher = player.GetComponent<PlayerVisualSwitcher>();
            Report(sb, switcher != null, "玩家身上有 PlayerVisualSwitcher", ref fail);

            if (switcher != null)
            {
                var sso = new SerializedObject(switcher);
                SerializedProperty classes = sso.FindProperty("classes");
                int count = classes != null ? classes.arraySize : 0;
                Report(sb, count == 2, $"职业表 | {count} 个职业可切", ref fail);

                SerializedProperty warrior = sso.FindProperty("warrior");
                SerializedProperty mageVisual = sso.FindProperty("mage");
                Report(sb, warrior != null && warrior.objectReferenceValue != null
                           && mageVisual != null && mageVisual.objectReferenceValue != null,
                       "切换器引用 | 战士 / 法师 两套动画都指到了", ref fail);
            }

            var stats = player.GetComponent<CharacterStats>();
            string className = stats != null && stats.Class != null ? stats.Class.className : "（无）";
            sb.AppendLine($"[INFO] 当前职业 | {className}");
        }

        // ---------------- 蓄力链 ----------------

        private static void CheckChargeChain(StringBuilder sb, ref int fail)
        {
            MethodInfo fire = FindMethod(typeof(ProjectileLauncher), "Fire",
                new[] { typeof(AttackData), typeof(GameObject), typeof(CharacterStats), typeof(GameObject),
                        typeof(Vector2), typeof(int), typeof(int), typeof(float), typeof(float), typeof(float), typeof(float) });
            Report(sb, fire != null, "发射器支持倍率 | ProjectileLauncher.Fire(..., damageMultiplier, sizeScale, speedScale)", ref fail);

            MethodInfo launch = typeof(Projectile).GetMethod("Launch", BindingFlags.Instance | BindingFlags.Public);
            bool launchOk = launch != null && launch.GetParameters().Length == 5;
            Report(sb, launchOk, "投射物支持缩放 | Projectile.Launch(direction, damage, targets, sizeScale, speedScale)", ref fail);

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            var caster = player != null ? player.GetComponent<SkillCaster>() : null;
            Report(sb, caster != null, "玩家身上有 SkillCaster", ref fail);
            if (caster == null) return;

            FieldInfo slot = typeof(SkillCaster).GetField("_chargeSlot", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo timer = typeof(SkillCaster).GetField("_chargeTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            Report(sb, slot != null && timer != null, "蓄力状态字段 | _chargeSlot / _chargeTimer 都在", ref fail);

            if (slot == null || timer == null) return;

            // 手动摆一个「蓄了一半」的状态，看 ChargeRatio 和 IsCharging
            SerializedObject so = new SerializedObject(caster);
            SerializedProperty skills = so.FindProperty("_cooldowns");   // 只为确保 SerializedObject 可读
            if (skills == null) skills = so.FindProperty("startingSkills");

            var slotsField = typeof(SkillCaster).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic);

            // 编辑模式下 ClassChanged 订阅不会触发，所以手动把法师的技能栏装进去看
            ClassData mageClass = AssetDatabase.LoadAssetAtPath<ClassData>("Assets/_Project/Data/classes/Class_Mage.asset");
            ClassData warriorClass = AssetDatabase.LoadAssetAtPath<ClassData>("Assets/_Project/Data/classes/Class_Warrior.asset");

            if (mageClass != null) caster.ApplySkills(mageClass.startingSkills);

            var slots = slotsField?.GetValue(caster) as SkillData[];
            SkillData fireball = slots != null ? slots.FirstOrDefault(s => s != null && s.chargeable) : null;

            Report(sb, fireball != null, $"法师技能栏有可蓄力技能 | {(fireball != null ? fireball.displayName : "没有")}", ref fail);

            SkillData healSkill = slots != null ? slots.FirstOrDefault(s => s != null && s.castType == SkillCastType.Heal) : null;
            Report(sb, healSkill != null, $"法师技能栏有回复技能 | {(healSkill != null ? healSkill.displayName : "没有")}", ref fail);

            if (fireball != null)
            {
                Report(sb, fireball.chargeable && fireball.chargeMaxScale > 1f && fireball.chargeMaxDamageBonus > 1f,
                       $"蓄力参数 | 最大 {fireball.chargeMaxScale:0.0} 倍大小 / {fireball.chargeMaxDamageBonus:0.0} 倍伤害 / {fireball.chargeMaxSpeedBonus:0.0} 倍速度", ref fail);

                int index = Array.IndexOf(slots, fireball);
                slot.SetValue(caster, index);
                timer.SetValue(caster, fireball.chargeTime * 0.5f);
                float ratio = caster.ChargeRatio;
                Report(sb, Mathf.Abs(ratio - 0.5f) < 0.02f && caster.IsCharging,
                       $"蓄力进度 | 按住一半时 ChargeRatio={ratio:0.00}", ref fail);

                slot.SetValue(caster, -1);
                timer.SetValue(caster, 0f);
                Report(sb, !caster.IsCharging && caster.ChargeRatio == 0f, "蓄力复位 | 清掉槽位后不再蓄力", ref fail);
            }

            // 恢复成场景里原本的职业技能，不留痕迹
            if (warriorClass != null) caster.ApplySkills(warriorClass.startingSkills);
        }

        // ---------------- 回血链 ----------------

        private static void CheckHealChain(StringBuilder sb, ref int fail)
        {
            MethodInfo heal = typeof(Health).GetMethod("Heal", BindingFlags.Instance | BindingFlags.Public);
            EventInfo healed = typeof(Health).GetEvent("Healed", BindingFlags.Instance | BindingFlags.Public);
            Report(sb, heal != null && healed != null, "Health 支持回血 | Heal() + Healed 事件都在", ref fail);

            // 造一个临时角色试一条真实的回血路径
            var go = new GameObject("__HealProbe");
            try
            {
                var health = go.AddComponent<Health>();
                var so = new SerializedObject(health);
                SerializedProperty maxProp = so.FindProperty("maxHealth");
                if (maxProp != null) maxProp.floatValue = 100f;
                so.ApplyModifiedPropertiesWithoutUndo();

                // 编辑模式下 Awake 不会自己跑
                Invoke(health, "Awake");

                // Current 是只读属性：用真实伤害把它打下来，而不是硬塞字段
                health.TakeDamage(new DamageInfo
                {
                    Amount = 60f,
                    SourcePosition = Vector2.zero,
                    KnockbackForce = 0f,
                    Attacker = null,
                    IsCritical = false,
                });

                float before = health.Current;
                float got = health.Heal(25f);
                float after = health.Current;

                Report(sb, Mathf.Abs(got - 25f) < 0.01f && Mathf.Abs(after - 65f) < 0.01f,
                       $"一次回血 | {before:0} + 25 -> {after:0}（实回 {got:0}）", ref fail);

                float full = health.Heal(999f);
                Report(sb, Mathf.Abs(health.Current - 100f) < 0.01f && full > 0f,
                       $"回血封顶 | 满血不会超过上限，实回 {full:0}", ref fail);

                Report(sb, Mathf.Abs(health.Heal(10f)) < 0.01f, "满血再回 | 返回 0 且不刷屏", ref fail);

                // 法阵：放一个、直接调 Tick 看回血与飘字
                Sprite ring = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Mage_Ring.png");
                HealCircle circle = HealCircle.Play(health, go.transform, ring,
                                                    healPerTick: 6f, duration: 2.4f, tickInterval: 0.3f,
                                                    diameter: 3.2f,
                                                    ringColor: new Color(1f, 0.86f, 0.45f, 0.95f),
                                                    numberColor: new Color(0.45f, 1f, 0.55f, 1f));

                Report(sb, circle != null, "法阵能放出来 | HealCircle.Play 返回实例", ref fail);

                if (circle != null)
                {
                    health.ResetToFull();
                    health.TakeDamage(new DamageInfo { Amount = 50f, SourcePosition = Vector2.zero, KnockbackForce = 0f, Attacker = null, IsCritical = false });

                    Invoke(circle, "Tick");
                    Invoke(circle, "Tick");

                    Report(sb, circle.Ticks == 2 && Mathf.Abs(circle.TotalHealed - 12f) < 0.01f,
                           $"法阵回血 | 跳了 {circle.Ticks} 次，共回 {circle.TotalHealed:0}", ref fail);

                    Report(sb, GameObject.Find("__FloatingTextCanvas") != null,
                           "回血飘字 | 生成了 FloatingTextCanvas（绿色 +N 会挂在这上面）", ref fail);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);

                GameObject canvas = GameObject.Find("__FloatingTextCanvas");
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        // ---------------- 技能资产 ----------------

        private static void CheckSkills(StringBuilder sb, ref int fail)
        {
            SkillData heal = AssetDatabase.LoadAssetAtPath<SkillData>("Assets/_Project/Data/Skills/Skill_Heal.asset");
            Report(sb, heal != null, "回复技能资产 | Skill_Heal.asset 存在", ref fail);

            if (heal != null)
            {
                Report(sb, heal.castType == SkillCastType.Heal, $"回复技能类型 | {heal.castType}", ref fail);
                Report(sb, heal.circleSprite != null, "回复技能法阵图 | 已接 Mage_Ring", ref fail);
                Report(sb, heal.healDuration > 0f && heal.healTickInterval > 0f && heal.healAmount > 0f,
                       $"回复节奏 | {heal.healDuration:0.0} 秒 / 每 {heal.healTickInterval:0.0} 秒 {heal.healAmount:0} 点", ref fail);
            }

            SkillData fireball = AssetDatabase.LoadAssetAtPath<SkillData>("Assets/_Project/Data/Skills/Skill_Fireball.asset");
            Report(sb, fireball != null && fireball.chargeable, "火球可蓄力 | chargeable = true", ref fail);

            ClassData mageClass = AssetDatabase.LoadAssetAtPath<ClassData>("Assets/_Project/Data/classes/Class_Mage.asset");
            bool hasTwo = mageClass != null && mageClass.startingSkills != null && mageClass.startingSkills.Count == 2;
            Report(sb, hasTwo, $"法师职业技能 | {(mageClass != null ? mageClass.startingSkills.Count : 0)} 个", ref fail);

            CheckProjectileMirror(sb, ref fail);
        }

        /// <summary>
        /// 火球贴图朝向：素材是「球在右、拖尾在左」的朝右画法，
        /// 往左打必须水平翻转，否则看着像倒着飞、和法师面朝对不上。
        /// </summary>
        private static void CheckProjectileMirror(StringBuilder sb, ref int fail)
        {
            const string prefabPath = "Assets/_Project/Prefabs/Fireball.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                Report(sb, false, $"火球预制体 | 找不到 {prefabPath}", ref fail);
                return;
            }

            System.Func<int, bool> flipWhenFiring = facing =>
            {
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    if (!go.TryGetComponent(out Projectile projectile)) return false;

                    projectile.Launch(new Vector2(facing, 0f), default, default, 1f, 1f);

                    SpriteRenderer body = go.GetComponentInChildren<SpriteRenderer>();
                    // 贴图必须真的在：引用断了的表现是「打出去什么都没有」，
                    // 而 flipX 断言照样能过 —— 光看朝向抓不住这种问题。
                    if (body == null || body.sprite == null) return false;

                    return body.flipX == (facing < 0);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[MageCheck] 投射物朝向测试失败：{e.Message}");
                    return false;
                }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
            };

            bool right = flipWhenFiring(1);
            bool left = flipWhenFiring(-1);

            Report(sb, right, "火球贴图朝向 | 往右飞不翻转（素材本来就是朝右）", ref fail);
            Report(sb, left, "火球贴图朝向 | 往左飞自动翻转，倒着飞的观感没了", ref fail);

            var fireballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var fireballBody = fireballPrefab != null ? fireballPrefab.GetComponentInChildren<SpriteRenderer>() : null;
            Report(sb, fireballBody != null && fireballBody.sprite != null,
                   $"火球预制体贴图 | {(fireballBody != null && fireballBody.sprite != null ? fireballBody.sprite.name : "★null（引用断了，打出去看不见）")}",
                   ref fail);

            CheckChargeOrbMirror(sb, ref fail);
        }

        /// <summary>
        /// 蓄力时杖头那颗球跟投射物用的是同一张「朝右画」的火球图，
        /// 朝左蓄力时不翻就是反的 —— 玩家看到的第一颗火球其实是它。
        /// </summary>
        private static void CheckChargeOrbMirror(StringBuilder sb, ref int fail)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            MageVisual mage = player != null ? player.GetComponent<MageVisual>() : null;
            SpriteRenderer sprite = player != null ? player.GetComponent<SpriteRenderer>() : null;

            if (mage == null || sprite == null)
            {
                Report(sb, false, "蓄力球朝向 | 场景里没有法师表现层，跳过", ref fail);
                return;
            }

            bool wasEnabled = mage.enabled;
            bool wasFlip = sprite.flipX;
            mage.enabled = true;

            try
            {
                mage.PlayCharge(true);
                InvokePrivate(mage, "UpdateChargeOrb");

                var field = typeof(MageVisual).GetField("_chargeOrb", BindingFlags.Instance | BindingFlags.NonPublic);
                SpriteRenderer orb = field != null ? field.GetValue(mage) as SpriteRenderer : null;

                sprite.flipX = false;
                InvokePrivate(mage, "UpdateChargeOrb");
                bool facesRight = orb != null && !orb.flipX && orb.transform.localPosition.x > 0f;

                sprite.flipX = true;
                InvokePrivate(mage, "UpdateChargeOrb");
                bool facesLeft = orb != null && orb.flipX && orb.transform.localPosition.x < 0f;

                Report(sb, facesRight, "蓄力球朝向 | 朝右时球在右侧、贴图不翻", ref fail);
                Report(sb, facesLeft, "蓄力球朝向 | 朝左时球在左侧、贴图跟着翻", ref fail);
            }
            catch (System.Exception e)
            {
                Report(sb, false, $"蓄力球朝向 | 测试抛异常：{e.Message}", ref fail);
            }
            finally
            {
                mage.PlayCharge(false);
                sprite.flipX = wasFlip;
                mage.enabled = wasEnabled;
            }
        }

        private static void InvokePrivate(object target, string method)
        {
            target?.GetType()
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }

        // ---------------- 工具 ----------------

        private static int GetArray(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            return p != null ? p.arraySize : -1;
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            m?.Invoke(target, null);
        }

        private static MethodInfo FindMethod(Type type, string name, Type[] signature)
        {
            return type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                       .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == signature.Length);
        }

        private static void Report(StringBuilder sb, bool ok, string message, ref int fail)
        {
            if (!ok) fail++;
            sb.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {message}");
        }

        private static void WriteReport(StringBuilder sb, int fail)
        {
            sb.AppendLine("[SUMMARY] 失败 | " + fail);
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[MageCheck] 报告写入 " + ReportPath);
        }
    }
}
#endif
