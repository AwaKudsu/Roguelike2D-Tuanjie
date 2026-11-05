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
    /// 把战士素材接进工程：设置导入参数 → 挂 WarriorVisual → 接刀风和技能图标。
    ///
    /// 素材是「一整张姿势表」，已经用 Python 按连通块切成了 15 张独立的 PNG
    /// （后来又补了一张 Warrior_AttackLift —— 上挑那一段没有专门姿势，借用举剑胜利帧，共 16 张）
    /// （切法见 docs/14，画布统一 320x480、脚底固定在距底 160px）。
    /// 这里只负责让团结按像素风正确导入，并把它们接到场景里的 Player 上。
    /// </summary>
    public static class WarriorArtBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ArtDir = "Assets/_Project/Art/Characters/Warrior";
        private const string SkillWhirlwindPath = "Assets/_Project/Data/Skills/Skill_Whirlwind.asset";
        private const string ReportPath = "_Tuanjie/warrior-report.txt";

        /// <summary>320x480 画布 / 160 PPU = 2x3 单位；骑士本体 1 单位高，脚底落在 pivot 下方 0.5</summary>
        private const float PixelsPerUnit = 160f;

        [MenuItem("团结/导入战士素材")]
        public static void Build()
        {
            var sb = new StringBuilder();
            int fail = 0;

            sb.AppendLine("=== 战士素材接入 ===");
            sb.AppendLine("[INFO] 工程 | " + Application.dataPath);

            if (!File.Exists(ScenePath))
            {
                Write(sb, 1);
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[PASS] 已打开场景 | " + ScenePath);

            // ---------- 1. 导入参数 ----------
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir });
            var sprites = new Dictionary<string, Sprite>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("Warrior_")) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;

                // pivot 与 mesh type 只在 setting 结构体上，TextureImporter 本身没有这两个属性
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spritePixelsPerUnit = PixelsPerUnit;
                importer.SetTextureSettings(settings);

                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh();

            // ---------- 2. 重新读一遍，确认每张都能当 Sprite 载入 ----------
            guids = AssetDatabase.FindAssets("t:Sprite", new[] { ArtDir });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;
                sprites[Path.GetFileNameWithoutExtension(path)] = sprite;
            }

            sb.AppendLine(sprites.Count == 27
                ? "[PASS] Sprite 资产齐全 | 27 张（16 张基础姿势 + 11 张派生帧：呼吸 2、跑动落地压缩 2、三段攻击的起手/收招 6、战吼 1）"
                : "[FAIL] Sprite 资产数量不对 | " + sprites.Count + " 张（应为 27）");

            if (sprites.Count != 27) fail++;

            foreach (var pair in sprites)
            {
                Sprite s = pair.Value;
                sb.AppendLine("[INFO] " + pair.Key + " | " + s.rect.width + "x" + s.rect.height
                              + " ppu=" + s.pixelsPerUnit + " pivot=(" + s.pivot.x + "," + s.pivot.y + ")");
            }

            // ---------- 3. 接线 ----------
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                sb.AppendLine("[FAIL] 场景里找不到 Tag=Player 的物体");
                Write(sb, fail + 1);
                return;
            }

            var renderer = player.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                sb.AppendLine("[FAIL] Player 上没有 SpriteRenderer");
                Write(sb, fail + 1);
                return;
            }

            var visual = player.GetComponent<WarriorVisual>();
            if (visual == null)
            {
                visual = player.AddComponent<WarriorVisual>();
                sb.AppendLine("[PASS] 新建组件 | WarriorVisual");
            }
            else
            {
                sb.AppendLine("[PASS] 组件已存在 | WarriorVisual");
            }

            var so = new SerializedObject(visual);
            SetRef(so, "sprite", renderer);
            SetRef(so, "controller", player.GetComponent<PlayerController>());
            SetRef(so, "health", player.GetComponent<Health>());
            SetRef(so, "attacker", player.GetComponent<PlayerAttacker>());
            SetRef(so, "body", player.GetComponent<Rigidbody2D>());

            SetRef(so, "idle", Get(sprites, "Warrior_Idle"));
            SetRef(so, "jump", Get(sprites, "Warrior_Jump"));
            SetRef(so, "fall", Get(sprites, "Warrior_Fall"));
            SetRef(so, "dash", Get(sprites, "Warrior_Dash"));
            SetRef(so, "hurt", Get(sprites, "Warrior_Hurt"));
            SetRef(so, "death", Get(sprites, "Warrior_Death"));
            SetRef(so, "victory", Get(sprites, "Warrior_Victory"));
            SetRef(so, "attackSlash", Get(sprites, "Warrior_AttackSlash"));
            SetRef(so, "attackLift", Get(sprites, "Warrior_AttackLift"));
            SetRef(so, "attackHeavy", Get(sprites, "Warrior_AttackHeavy"));
            SetRef(so, "attackThrust", Get(sprites, "Warrior_AttackThrust"));
            SetRef(so, "warcry", Get(sprites, "Warrior_Warcry"));

            // ---------- 3b. 短序列（2026-11-05 新增：多帧动作）----------
            if (!WireArray(so, "idleFrames", sb, sprites,
                    "Warrior_Idle", "Warrior_Idle_2", "Warrior_Idle_3")) fail++;

            if (!WireArray(so, "slashFrames", sb, sprites,
                    "Warrior_AttackSlash_Windup", "Warrior_AttackSlash", "Warrior_AttackSlash_Follow")) fail++;

            if (!WireArray(so, "liftFrames", sb, sprites,
                    "Warrior_AttackLift_Windup", "Warrior_AttackLift", "Warrior_AttackLift_Follow")) fail++;

            if (!WireArray(so, "thrustFrames", sb, sprites,
                    "Warrior_AttackThrust_Windup", "Warrior_AttackThrust", "Warrior_AttackThrust_Follow")) fail++;

            if (!WireArray(so, "spinFrames", sb, sprites,
                    "Warrior_AttackHeavy", "Warrior_AttackSlash")) fail++;

            // 跑动 6 帧：落地压缩帧（_4/_5）紧跟在落地姿势（_1/_3）后面
            string[] runNames =
            {
                "Warrior_Run_0", "Warrior_Run_1", "Warrior_Run_4",
                "Warrior_Run_2", "Warrior_Run_3", "Warrior_Run_5"
            };

            if (!WireArray(so, "run", sb, sprites, runNames)) fail++;

            so.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. 刀风 + 技能图标 ----------
            var attacker = player.GetComponent<PlayerAttacker>();
            if (attacker != null)
            {
                var aso = new SerializedObject(attacker);
                SetRef(aso, "slashArc", Get(sprites, "Warrior_SlashArc"));

                SerializedProperty colorProp = aso.FindProperty("slashColor");
                if (colorProp != null) colorProp.colorValue = new Color(1f, 0.94f, 0.75f, 1f);

                SerializedProperty scaleProp = aso.FindProperty("slashScale");
                if (scaleProp != null) scaleProp.floatValue = 1f;

                aso.ApplyModifiedPropertiesWithoutUndo();
                sb.AppendLine("[PASS] 刀风已接 | Warrior_SlashArc");
            }

            var whirlwind = AssetDatabase.LoadAssetAtPath<SkillData>(SkillWhirlwindPath);
            if (whirlwind != null)
            {
                var sso = new SerializedObject(whirlwind);
                SerializedProperty iconProp = sso.FindProperty("icon");
                if (iconProp != null)
                {
                    iconProp.objectReferenceValue = Get(sprites, "Warrior_AttackHeavy");
                    sso.ApplyModifiedPropertiesWithoutUndo();
                    sb.AppendLine("[PASS] 技能图标 | 旋风斩 → Warrior_AttackHeavy");
                }
            }

            // ---------- 5. 渲染器设置 ----------
            renderer.sprite = Get(sprites, "Warrior_Idle");
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.color = Color.white;
            sb.AppendLine("[PASS] 玩家贴图 | " + (renderer.sprite != null ? renderer.sprite.name : "null"));

            // 旧占位方块的尺寸残留（Square.png 是 32 PPU，切到 160 PPU 后 size 值会对不上）
            var rso = new SerializedObject(renderer);
            SerializedProperty sizeProp = rso.FindProperty("m_Size");
            if (sizeProp != null && sizeProp.vector2Value != Vector2.one)
            {
                sizeProp.vector2Value = Vector2.one;
                rso.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            sb.AppendLine("[PASS] 场景已存盘 | " + ScenePath);

            sb.AppendLine();
            sb.AppendLine("[SUMMARY] 失败 | " + fail);
            Write(sb, fail);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("战士素材",
                    fail == 0 ? "全部完成，按 Play 看看骑士的样子。" : "有 " + fail + " 项失败，看报告。",
                    "好");
            }
        }

        private static Sprite Get(Dictionary<string, Sprite> sprites, string name)
        {
            Sprite s;
            return sprites.TryGetValue(name, out s) ? s : null;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) return;
            prop.objectReferenceValue = value;
        }

        /// <summary>
        /// 按名字把一串 Sprite 写进数组字段。缺任何一张都算失败 —— 缺帧只会表现为
        /// 「动作少一截」，运行时不报错，所以必须在这里拦住。
        /// </summary>
        private static bool WireArray(SerializedObject so, string field, StringBuilder sb,
                                      Dictionary<string, Sprite> sprites, params string[] names)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) return false;

            prop.arraySize = names.Length;

            bool ok = true;
            var missing = "";
            for (int i = 0; i < names.Length; i++)
            {
                Sprite sprite = Get(sprites, names[i]);
                if (sprite == null)
                {
                    ok = false;
                    missing += (missing.Length > 0 ? ", " : "") + names[i];
                }

                prop.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
            }

            if (ok)
            {
                sb.AppendLine("[PASS] 序列 | " + field + " " + names.Length + " 帧");
            }
            else
            {
                sb.AppendLine("[FAIL] 序列缺帧 | " + field + "：" + missing);
            }

            return ok;
        }

        private static void Write(StringBuilder sb, int fail)
        {
            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[战士素材]\n" + sb);
        }
    }
}
#endif
