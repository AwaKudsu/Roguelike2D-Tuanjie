#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 把这一轮的三样东西一次装配好：
    ///   1. 两张特效贴图（圆环 / 巨剑）的导入参数 —— 圆环 128PPU 正好 1 单位直径
    ///   2. 旋风斩改成「持续判定」：duration 1.2 秒、每 0.2 秒扫一次
    ///   3. 新建战吼技能 Skill_WarCry，并接到玩家的技能栏
    ///
    /// 走菜单「团结 → 生成战斗特效与战吼」。
    /// </summary>
    public static class BattleFxBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string FxDir = "Assets/_Project/Art/Effects";
        private const string SkillsDir = "Assets/_Project/Data/Skills";
        private const string WhirlwindPath = SkillsDir + "/Skill_Whirlwind.asset";
        private const string WarcryPath = SkillsDir + "/Skill_WarCry.asset";
        private const string WarriorClassPath = "Assets/_Project/Data/classes/Class_Warrior.asset";
        private const string WhirlwindAttackPath = "Assets/_Project/Data/Weapons/Attack_Whirlwind.asset";
        private const string ReportPath = "_Tuanjie/battlefx-report.txt";

        [MenuItem("团结/生成战斗特效与战吼")]
        public static void Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[INFO] 战斗特效与战吼装配");

            OpenGameScene(sb);
            EnsureFolder("Assets/_Project/Art", "Effects");

            Sprite ring = ImportFx("Ring.png", 128f, sb);
            Sprite sword = ImportFx("GreatSword.png", 32f, sb);

            AttackData whirlwindAttack = AssetDatabase.LoadAssetAtPath<AttackData>(WhirlwindAttackPath);
            sb.AppendLine(whirlwindAttack != null
                ? $"[PASS] 旋风斩判定数据 | {whirlwindAttack.name} 判定 {whirlwindAttack.hitboxSize.x:0.#}x{whirlwindAttack.hitboxSize.y:0.#}"
                : "[FAIL] 旋风斩判定数据 | 找不到 Attack_Whirlwind");

            SkillData whirlwind = ConfigureWhirlwind(sb);
            SkillData warcry = ConfigureWarcry(ring, sword, whirlwindAttack, sb);

            WirePlayerSkills(whirlwind, warcry, sb);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            bool saved = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            sb.AppendLine(saved ? $"[PASS] 场景已存盘 | {ScenePath}" : "[WARN] 场景没存盘");

            // 统计失败数，报告末尾统一给结论
            string text = sb.ToString();
            int fail = CountFailures(text);
            text += $"[SUMMARY] 失败 | {fail}\n";
            text += $"[SUMMARY] 特效贴图 | Ring + GreatSword\n";
            text += $"[SUMMARY] 技能 | 旋风斩（持续判定） + 战吼（Warcry）\n";

            System.IO.File.WriteAllText(ReportPath, text, new UTF8Encoding(false));
            Debug.Log($"[BattleFxBuilder] 完成，报告写到 {ReportPath}\n{text}");

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("团结",
                    fail == 0 ? "战斗特效与战吼已装配完成。\n详见 _Tuanjie/battlefx-report.txt"
                              : $"有 {fail} 项失败，详见 _Tuanjie/battlefx-report.txt",
                    "好");
            }
        }

        // ---------------- 贴图 ----------------

        private static Sprite ImportFx(string fileName, float pixelsPerUnit, StringBuilder sb)
        {
            string path = $"{FxDir}/{fileName}";

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                sb.AppendLine($"[FAIL] 特效贴图 | 找不到 {path}");
                return null;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = pixelsPerUnit;
            importer.SetTextureSettings(settings);

            importer.textureType = TextureImporterType.Sprite;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                sb.AppendLine($"[FAIL] 特效贴图 | {fileName} 导入后取不到 Sprite");
                return null;
            }

            sb.AppendLine($"[PASS] 特效贴图 | {fileName} {sprite.rect.width:0}x{sprite.rect.height:0} ppu={pixelsPerUnit:0} " +
                          $"（{sprite.rect.width / pixelsPerUnit:0.##} x {sprite.rect.height / pixelsPerUnit:0.##} 单位）");
            return sprite;
        }

        // ---------------- 技能资产 ----------------

        private static SkillData ConfigureWhirlwind(StringBuilder sb)
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(WhirlwindPath);
            if (skill == null)
            {
                sb.AppendLine($"[FAIL] 旋风斩 | 找不到 {WhirlwindPath}");
                return null;
            }

            skill.duration = 1.2f;
            skill.tickInterval = 0.2f;
            EditorUtility.SetDirty(skill);

            sb.AppendLine($"[PASS] 旋风斩改成持续判定 | 持续 {skill.duration:0.##} 秒 / 每 {skill.tickInterval:0.##} 秒扫一次" +
                          $"（约 {Mathf.FloorToInt(skill.duration / skill.tickInterval) + 1} 下）");
            return skill;
        }

        private static SkillData ConfigureWarcry(Sprite ring, Sprite sword, AttackData whirlwindAttack, StringBuilder sb)
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(WarcryPath);
            if (skill == null)
            {
                skill = ScriptableObject.CreateInstance<SkillData>();
                AssetDatabase.CreateAsset(skill, WarcryPath);
                sb.AppendLine($"[PASS] 新建战吼技能 | {WarcryPath}");
            }
            else
            {
                sb.AppendLine($"[INFO] 战吼技能已存在，覆盖字段 | {WarcryPath}");
            }

            skill.skillId = "warrior_warcry";
            skill.displayName = "战吼";
            skill.description = "以自身为圆心炸开一圈冲击波，把范围内的敌人吓跑。它们会丢掉攻击目标、背对着你来回乱跑一阵子。";
            skill.icon = sword;
            skill.manaCost = 15f;
            skill.cooldown = 6f;
            skill.castType = SkillCastType.Warcry;

            // 战吼不打伤害，但保留判定数据方便以后接「战吼也能震一下」的强化
            skill.attack = whirlwindAttack;

            skill.fearDuration = 3.5f;
            skill.fearRadius = 12f;
            skill.ringDuration = 0.45f;
            skill.ringSprite = ring;
            skill.swordSprite = sword;

            EditorUtility.SetDirty(skill);

            sb.AppendLine($"[PASS] 战吼已配置 | 半径 {skill.fearRadius:0.#} 单位 / 恐慌 {skill.fearDuration:0.#} 秒 / 耗蓝 {skill.manaCost:0} / 冷却 {skill.cooldown:0}s");
            sb.AppendLine(skill.ringSprite != null ? "[PASS] 战吼圆环贴图 | Ring" : "[FAIL] 战吼圆环贴图 | 没接上");
            sb.AppendLine(skill.swordSprite != null ? "[PASS] 战吼巨剑贴图 | GreatSword" : "[FAIL] 战吼巨剑贴图 | 没接上");

            return skill;
        }

        // ---------------- 接线 ----------------

        private static void WirePlayerSkills(SkillData whirlwind, SkillData warcry, StringBuilder sb)
        {
            var classData = AssetDatabase.LoadAssetAtPath<ClassData>(WarriorClassPath);
            if (classData == null)
            {
                sb.AppendLine($"[FAIL] 战士职业 | 找不到 {WarriorClassPath}");
                return;
            }

            classData.startingSkills = new System.Collections.Generic.List<SkillData>();
            if (whirlwind != null) classData.startingSkills.Add(whirlwind);
            if (warcry != null) classData.startingSkills.Add(warcry);
            EditorUtility.SetDirty(classData);

            sb.AppendLine($"[PASS] 战士职业技能 | {classData.startingSkills.Count} 个：{string.Join(" / ", classData.startingSkills.ConvertAll(s => s != null ? s.displayName : "空"))}");

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                sb.AppendLine("[FAIL] 玩家 | 场景里找不到 Tag=Player 的物体");
                return;
            }

            var caster = player.GetComponent<SkillCaster>();
            if (caster == null)
            {
                sb.AppendLine("[FAIL] 技能栏 | 玩家身上没有 SkillCaster");
                return;
            }

            var so = new SerializedObject(caster);
            SerializedProperty list = so.FindProperty("startingSkills");
            if (list == null)
            {
                sb.AppendLine("[FAIL] 技能栏 | SkillCaster 上没有 startingSkills 字段");
                return;
            }

            int count = (whirlwind != null ? 1 : 0) + (warcry != null ? 1 : 0);
            list.arraySize = count;
            int index = 0;
            if (whirlwind != null) list.GetArrayElementAtIndex(index++).objectReferenceValue = whirlwind;
            if (warcry != null) list.GetArrayElementAtIndex(index).objectReferenceValue = warcry;

            so.ApplyModifiedPropertiesWithoutUndo();

            sb.AppendLine($"[PASS] 玩家技能栏 | {count} 个（战吼在槽 2，按 L 放）");
        }

        // ---------------- 杂项 ----------------

        private static void OpenGameScene(StringBuilder sb)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine(scene.IsValid() ? $"[PASS] 已打开场景 | {ScenePath}" : $"[FAIL] 打不开场景 | {ScenePath}");
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static int CountFailures(string text)
        {
            int count = 0;
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith("[FAIL]")) count++;
            }
            return count;
        }
    }
}
#endif
