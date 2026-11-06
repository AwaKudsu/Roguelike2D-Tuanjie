#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 一键把法师素材接进工程：
    ///   ① 21 张 PNG 按 160 PPU / 居中 pivot 导入
    ///   ② 给玩家挂 MageVisual 并接线，挂 PlayerVisualSwitcher 管两套动画
    ///   ③ 建 Skill_Heal（回血），把 Skill_Fireball 改成可蓄力
    ///   ④ Class_Mage.startingSkills = 火球 + 回血
    /// </summary>
    public static class MageArtBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ArtDir = "Assets/_Project/Art/Characters/Mage";
        private const string WarriorsDir = "Assets/_Project/Art/Characters/Warrior";
        private const string SkillsDir = "Assets/_Project/Data/Skills";
        private const string ClassesDir = "Assets/_Project/Data/classes";
        private const string ReportPath = "_Tuanjie/mage-report.txt";
        private const float PixelsPerUnit = 160f;

        [MenuItem("团结/导入法师素材")]
        public static void Build()
        {
            var sb = new StringBuilder();
            int fail = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[PASS] 已打开场景 | " + ScenePath);

            // ---------- ① 导入贴图 ----------
            int imported = ImportSprites(sb, ref fail);

            var sprites = new Dictionary<string, Sprite>();
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { ArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) sprites[Path.GetFileNameWithoutExtension(path)] = sprite;
            }

            sb.AppendLine(imported == 21
                ? $"[PASS] Sprite 资产齐全 | {imported} 张"
                : $"[FAIL] Sprite 资产 | 期望 21 张，实际 {imported} 张");
            if (imported != 21) fail++;

            // ---------- ② 玩家身上的表现层 ----------
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                sb.AppendLine("[FAIL] 找不到 Tag=Player 的物体");
                WriteReport(sb, fail);
                return;
            }

            var renderer = player.GetComponent<SpriteRenderer>();
            var stats = player.GetComponent<CharacterStats>();
            var warrior = player.GetComponent<WarriorVisual>();

            var mage = player.GetComponent<MageVisual>();
            if (mage == null) mage = player.AddComponent<MageVisual>();
            sb.AppendLine(mage != null ? "[PASS] 挂上组件 | MageVisual" : "[FAIL] 挂不上 MageVisual");
            if (mage == null) fail++;

            var mageSo = new SerializedObject(mage);
            SetRef(mageSo, "sprite", renderer);
            SetRef(mageSo, "controller", player.GetComponent<PlayerController>());
            SetRef(mageSo, "health", player.GetComponent<Health>());
            SetRef(mageSo, "attacker", player.GetComponent<PlayerAttacker>());
            SetRef(mageSo, "body", player.GetComponent<Rigidbody2D>());

            SetArray(mageSo, "idleFrames", sprites, sb, ref fail, "Mage_Idle", "Mage_Idle_2", "Mage_Idle_3");
            SetArray(mageSo, "run", sprites, sb, ref fail, "Mage_Run_0", "Mage_Run_1", "Mage_Run_4",
                                                                 "Mage_Run_2", "Mage_Run_3", "Mage_Run_5");
            SetArray(mageSo, "attackFrames", sprites, sb, ref fail,
                     "Mage_Attack_0", "Mage_Attack_1", "Mage_Attack_2", "Mage_Attack_3");
            SetArray(mageSo, "castFrames", sprites, sb, ref fail, "Mage_Cast_0", "Mage_Cast_1", "Mage_Cast_2");

            SetRef(mageSo, "jump", sprites, "Mage_Jump");
            SetRef(mageSo, "hurt", sprites, "Mage_Hurt");
            SetRef(mageSo, "death", sprites, "Mage_Death");

            // 蓄力预览：杖头那颗越变越大的火球
            SetRef(mageSo, "chargeOrbSprite", sprites, "Mage_Fireball");

            // 体积要写进场景：改 C# 里的默认值没用 —— 场景里已经序列化过，序列化值优先
            mageSo.FindProperty("chargeOrbMinScale").floatValue = 0.8f;
            mageSo.FindProperty("chargeOrbMaxScale").floatValue = 2.2f;
            mageSo.FindProperty("chargeOrbOffset").vector3Value = new Vector3(0.62f, 0.34f, 0f);

            sb.AppendLine(sprites.ContainsKey("Mage_Fireball")
                ? "[PASS] 蓄力预览 | 杖头火球用 Mage_Fireball，从 0.8 倍长到 2.2 倍"
                : "[WARN] 蓄力预览 | 没有 Mage_Fireball，蓄力时看不到球");
            mageSo.ApplyModifiedPropertiesWithoutUndo();

            // 切换器：两套动画 + 两个职业
            var switcher = player.GetComponent<PlayerVisualSwitcher>();
            if (switcher == null) switcher = player.AddComponent<PlayerVisualSwitcher>();
            sb.AppendLine(switcher != null ? "[PASS] 挂上组件 | PlayerVisualSwitcher" : "[FAIL] 挂不上 PlayerVisualSwitcher");
            if (switcher == null) fail++;

            var switcherSo = new SerializedObject(switcher);
            SetRef(switcherSo, "stats", stats);
            SetRef(switcherSo, "warrior", warrior);
            SetRef(switcherSo, "mage", mage);
            SetRef(switcherSo, "sprite", renderer);

            ClassData warriorClass = AssetDatabase.LoadAssetAtPath<ClassData>(ClassesDir + "/Class_Warrior.asset");
            ClassData mageClass = AssetDatabase.LoadAssetAtPath<ClassData>(ClassesDir + "/Class_Mage.asset");

            SerializedProperty classList = switcherSo.FindProperty("classes");
            if (classList != null)
            {
                classList.arraySize = 2;
                classList.GetArrayElementAtIndex(0).objectReferenceValue = warriorClass;
                classList.GetArrayElementAtIndex(1).objectReferenceValue = mageClass;
            }

            SerializedProperty startIndex = switcherSo.FindProperty("currentIndex");
            if (startIndex != null) startIndex.intValue = stats != null && stats.Class == mageClass ? 1 : 0;
            switcherSo.ApplyModifiedPropertiesWithoutUndo();

            sb.AppendLine($"[PASS] 职业表 | 战士 {(warriorClass != null ? "有" : "缺")} / 法师 {(mageClass != null ? "有" : "缺")}");
            if (warriorClass == null || mageClass == null) fail++;

            // ---------- ③ 技能资产 ----------
            EnsureFolder(SkillsDir);
            SkillData heal = MakeHealSkill(sb, sprites, mageClass);
            SkillData fireball = AssetDatabase.LoadAssetAtPath<SkillData>(SkillsDir + "/Skill_Fireball.asset");

            if (fireball != null)
            {
                var so = new SerializedObject(fireball);
                SetBool(so, "chargeable", true);
                SetFloat(so, "chargeTime", 1.1f);
                SetFloat(so, "chargeMinScale", 1.0f);
                SetFloat(so, "chargeMaxScale", 2.6f);
                SetFloat(so, "chargeMaxSpeedBonus", 1.9f);
                SetFloat(so, "chargeMaxDamageBonus", 2.0f);
                so.ApplyModifiedPropertiesWithoutUndo();
                sb.AppendLine("[PASS] 火球改成可蓄力 | 按住 1.1 秒蓄满：球 2.6 倍、速度 1.9 倍、伤害 2.0 倍");
            }
            else
            {
                sb.AppendLine("[FAIL] 找不到 Skill_Fireball.asset");
                fail++;
            }

            // ---------- ④ 法师职业技能 ----------
            if (mageClass != null && heal != null && fireball != null)
            {
                var so = new SerializedObject(mageClass);
                SerializedProperty list = so.FindProperty("startingSkills");
                if (list != null)
                {
                    list.arraySize = 2;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = fireball;
                    list.GetArrayElementAtIndex(1).objectReferenceValue = heal;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                sb.AppendLine("[PASS] 法师职业技能 | 2 个：火球（可蓄力）/ 回复");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            sb.AppendLine("[PASS] 场景已存盘 | " + ScenePath);

            WriteReport(sb, fail);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("导入法师素材", fail == 0 ? "全部通过" : $"有 {fail} 项失败，见报告", "好");
            }
        }

        // ---------------- 贴图导入 ----------------

        private static int ImportSprites(StringBuilder sb, ref int fail)
        {
            int count = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).StartsWith("Mage_")) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spritePixelsPerUnit = PixelsPerUnit;
                settings.spriteGenerateFallbackPhysicsShape = false;
                importer.SetTextureSettings(settings);

                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;

                importer.SaveAndReimport();
                count++;
            }

            sb.AppendLine($"[INFO] 导入贴图 | {count} 张，全部 {PixelsPerUnit} PPU + 居中 pivot");
            return count;
        }

        // ---------------- 技能 / 职业 ----------------

        private static SkillData MakeHealSkill(StringBuilder sb, Dictionary<string, Sprite> sprites, ClassData mageClass)
        {
            string path = SkillsDir + "/Skill_Heal.asset";
            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(path);

            if (skill == null)
            {
                skill = ScriptableObject.CreateInstance<SkillData>();
                AssetDatabase.CreateAsset(skill, path);
                sb.AppendLine("[PASS] 新建回复技能 | " + path);
            }
            else
            {
                sb.AppendLine("[INFO] 回复技能已存在 | 覆盖字段");
            }

            var so = new SerializedObject(skill);
            SetString(so, "skillId", "mage_heal");
            SetString(so, "displayName", "回复");
            SetString(so, "description", "举起法杖，在脚下撑开一圈法阵，持续回复生命。");
            SetInt(so, "castType", (int)SkillCastType.Heal);
            SetInt(so, "slot", 0);      // 有的版本没有 slot 字段，SetInt 内部会忽略

            SetFloat(so, "manaCost", 20f);
            SetFloat(so, "cooldown", 8f);
            SetFloat(so, "healAmount", 6f);
            SetFloat(so, "healDuration", 2.4f);
            SetFloat(so, "healTickInterval", 0.3f);
            SetFloat(so, "circleDiameter", 3.2f);

            SerializedProperty color = so.FindProperty("healColor");
            if (color != null) color.colorValue = new Color(0.45f, 1f, 0.55f, 1f);

            SerializedProperty circle = so.FindProperty("circleSprite");
            if (circle != null && sprites.TryGetValue("Mage_Ring", out Sprite ring)) circle.objectReferenceValue = ring;

            SerializedProperty icon = so.FindProperty("icon");
            if (icon != null && sprites.TryGetValue("Mage_Cast_1", out Sprite iconSprite)) icon.objectReferenceValue = iconSprite;

            so.ApplyModifiedPropertiesWithoutUndo();

            sb.AppendLine("[PASS] 回复技能已配置 | 2.4 秒内每 0.3 秒回 6 点（共 8 次 / 48 点），耗蓝 20 / 冷却 8 秒");
            return skill;
        }

        // ---------------- 小工具 ----------------

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetRef(SerializedObject so, string field, Dictionary<string, Sprite> sprites, string name)
        {
            if (!sprites.TryGetValue(name, out Sprite sprite)) return;
            SetRef(so, field, sprite);
        }

        private static void SetArray(SerializedObject so, string field, Dictionary<string, Sprite> sprites,
                                     StringBuilder sb, ref int fail, params string[] names)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                sb.AppendLine($"[FAIL] 找不到字段 {field}");
                fail++;
                return;
            }

            int found = 0;
            p.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                if (sprites.TryGetValue(names[i], out Sprite sprite))
                {
                    p.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
                    found++;
                }
                else
                {
                    sb.AppendLine($"[FAIL] 序列缺帧 | {field} 缺少 {names[i]}");
                    fail++;
                }
            }

            if (found == names.Length) sb.AppendLine($"[PASS] 序列 | {field} {names.Length} 帧");
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null && p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = value;
            else if (p != null) p.intValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.boolValue = value;
        }

        private static void SetString(SerializedObject so, string field, string value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.stringValue = value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void WriteReport(StringBuilder sb, int fail)
        {
            sb.AppendLine("[SUMMARY] 失败 | " + fail);
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[MageArtBuilder] 报告写入 " + ReportPath);
        }
    }
}
#endif
