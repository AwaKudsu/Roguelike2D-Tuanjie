using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 一键把 HUD（血条 + 蓝条）和技能栏（4 格 + 冷却遮罩）按 Day 6 第 4、5 节的数值重建。
    ///
    /// 为什么非要脚本做，不能手摆：这一套里有五个坑**全都不报错**——
    ///   ① Canvas 的 Rect Transform Scale 是 (0,0,0) → 整块 UI 被"缩成一个点"，Game 视图里什么都看不见；
    ///   ② 冷却遮罩的 Image Type 是 Simple → fillAmount 完全无效，遮罩永远不动；
    ///   ③ 冷却遮罩的 Source Image 为空 → 遮罩根本不画；
    ///   ④ 技能格的图 sprite 为空 → 格子透明，看着像没生成；
    ///   ⑤ 容器锚点留在 (0.5,0.5) → 技能栏跑到屏幕中间。
    /// 这五条在 Day 6 的排错表里占了一半。
    ///
    /// 用法：编辑器里打开 Assets/_Project/Scenes/Game.unity，菜单 团结 → 修复 HUD 与技能栏。
    /// 重复运行是安全的（幂等：已有的物体就地改参数，没有的才新建）。
    /// </summary>
    public static class HudBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string SquareSpritePath = "Assets/_Project/Art/Placeholder/Square.png";
        private const string CircleSpritePath = "Assets/_Project/Art/Placeholder/Circle.png";
        private const string FireballAssetPath = "Assets/_Project/Data/Skills/Skill_Fireball.asset";
        private const string WhirlwindAssetPath = "Assets/_Project/Data/Skills/Skill_Whirlwind.asset";

        // Day 6 第 4 节给的血条 / 蓝条数值
        private static readonly Vector2 HealthBarPos = new Vector2(40f, -40f);
        private static readonly Vector2 HealthBarSize = new Vector2(420f, 26f);
        private static readonly Vector2 ManaBarPos = new Vector2(40f, -74f);
        private static readonly Vector2 ManaBarSize = new Vector2(320f, 18f);

        private static readonly Color BarBackground = new Color(38f / 255f, 38f / 255f, 46f / 255f, 230f / 255f);
        private static readonly Color HealthColor = new Color(214f / 255f, 63f / 255f, 63f / 255f, 1f);
        private static readonly Color ManaColor = new Color(66f / 255f, 133f / 255f, 244f / 255f, 1f);

        [MenuItem("团结/修复 HUD 与技能栏")]
        public static void FixAll()
        {
            Scene active = SceneManager.GetActiveScene();
            if (!string.Equals(active.path, ScenePath, System.StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "需要先打开 Game 场景",
                    "请先在编辑器里打开 " + ScenePath + "（并保存当前改动），再运行这个菜单项。\n\n" +
                    "当前打开的是：" + (string.IsNullOrEmpty(active.path) ? "（未保存的新场景）" : active.path),
                    "好");
                return;
            }

            Canvas canvas = FindOverlayCanvas(active);
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("找不到 Canvas", "场景里没有 Screen Space - Overlay 的 Canvas。", "好");
                return;
            }

            var log = new StringBuilder();
            log.AppendLine("===== 修复 HUD 与技能栏 =====");

            // ---------- 0. Canvas 自身的 Scale ----------
            // ★ 这是「血条蓝条看不见」的根因：Scale 是 0 的时候，
            //   整棵 UI 子树都被缩成一个点，渲染出来是零面积。
            var canvasRect = (RectTransform)canvas.transform;
            Vector3 before = canvasRect.localScale;
            canvasRect.localScale = Vector3.one;
            log.AppendLine("[PASS] Canvas.localScale：" + Format(before) + " → (1.00,1.00,1.00)");

            Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (uiSprite == null)
            {
                uiSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SquareSpritePath);
                log.AppendLine("[WARN] 取不到内置 UISprite，改用 " + SquareSpritePath);
            }
            else
            {
                log.AppendLine("[INFO] 内置 UISprite = " + uiSprite.name);
            }

            // ---------- 1. 血条 / 蓝条 ----------
            StatBar healthBar = EnsureBar(canvas.transform, "HealthBar", "HealthFill",
                HealthBarPos, HealthBarSize, HealthColor, uiSprite, log);
            StatBar manaBar = EnsureBar(canvas.transform, "ManaBar", "ManaFill",
                ManaBarPos, ManaBarSize, ManaColor, uiSprite, log);

            // ---------- 2. HUDController 接线 ----------
            var hud = canvas.GetComponent<HUDController>();
            if (hud == null) hud = canvas.gameObject.AddComponent<HUDController>();
            var hudSo = new SerializedObject(hud);
            SetRef(hudSo, "healthBar", healthBar);
            SetRef(hudSo, "manaBar", manaBar);
            hudSo.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("[PASS] Canvas.HUDController：healthBar=HealthBar，manaBar=ManaBar");

            // ---------- 3. 技能栏 ----------
            Transform bar = BuildSkillBar(canvas.transform, uiSprite, log);

            // ---------- 4. 技能图标 ----------
            // SkillData.icon 目前是空的，而 SkillBarUI 每帧会把格子的 sprite 赋成 skill.icon
            // —— 空的就等于把格子刷成「什么都不画」，看着像技能栏坏了。
            AssignSkillIcon(FireballAssetPath, CircleSpritePath, log);
            AssignSkillIcon(WhirlwindAssetPath, SquareSpritePath, log);

            // ---------- 5. 存盘 ----------
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            AssetDatabase.SaveAssets();
            log.AppendLine("[SUMMARY] 已存盘。技能栏容器 = " + GetPath(bar));

            string text = log.ToString();
            Debug.Log(text);
            System.IO.File.WriteAllText("_Tuanjie/hud-report.txt", text, new UTF8Encoding(false));
            EditorUtility.DisplayDialog("修复完成", text, "好");
        }

        /// <summary>把一根条（背景 + 填充）按 Day 6 的数值摆好，并返回挂在背景上的 StatBar</summary>
        private static StatBar EnsureBar(Transform canvas, string barName, string fillName,
                                         Vector2 pos, Vector2 size, Color fillColor,
                                         Sprite uiSprite, StringBuilder log)
        {
            GameObject barGo = GetDirectChild(canvas, barName);
            if (barGo == null)
            {
                barGo = new GameObject(barName, typeof(RectTransform), typeof(Image));
                barGo.transform.SetParent(canvas, false);
                log.AppendLine("[INFO] 新建 " + barName);
            }

            barGo.SetActive(true);

            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = new Vector2(0f, 1f);   // 左上角
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 1f);
            barRect.localScale = Vector3.one;
            barRect.anchoredPosition = pos;
            barRect.sizeDelta = size;

            Image barImage = GetOrAddImage(barGo);
            barImage.sprite = uiSprite;
            barImage.color = BarBackground;
            barImage.type = Image.Type.Simple;
            barImage.raycastTarget = false;

            GameObject fillGo = GetDirectChild(barGo.transform, fillName);
            if (fillGo == null)
            {
                fillGo = new GameObject(fillName, typeof(RectTransform), typeof(Image));
                fillGo.transform.SetParent(barGo.transform, false);
                log.AppendLine("[INFO] 新建 " + barName + "/" + fillName);
            }

            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = Vector2.zero;         // 铺满父物体
            fillRect.anchorMax = Vector2.one;
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.localScale = Vector3.one;

            Image fillImage = GetOrAddImage(fillGo);
            fillImage.sprite = uiSprite;
            fillImage.color = fillColor;
            fillImage.type = Image.Type.Filled;                       // ★ 必须是 Filled
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;

            StatBar statBar = barGo.GetComponent<StatBar>();
            if (statBar == null) statBar = barGo.AddComponent<StatBar>();

            var so = new SerializedObject(statBar);
            SetRef(so, "fill", fillImage);
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("[PASS] " + barName + "：锚点(0,1) Pos(" + pos.x + "," + pos.y + ") 尺寸(" +
                           size.x + "x" + size.y + ") 背景=" + ColorText(BarBackground) +
                           " / " + fillName + "：Filled-Horizontal-Left 颜色=" + ColorText(fillColor) +
                           " fillAmount=1 / StatBar.fill 已接");
            return statBar;
        }

        /// <summary>重建技能栏：透明容器 + 4 个格子 + 4 个 Filled 冷却遮罩 + SkillBarUI 接线</summary>
        private static Transform BuildSkillBar(Transform canvas, Sprite uiSprite, StringBuilder log)
        {
            const float slotSize = 72f;
            const float spacing = 8f;
            const float posX = 40f;
            const float posY = 40f;

            GameObject old = GetDirectChild(canvas, "SkillBar");
            if (old != null)
            {
                Object.DestroyImmediate(old);
                log.AppendLine("[INFO] 删掉了旧的 Canvas/SkillBar（含里面的格子）");
            }

            var barGo = new GameObject("SkillBar", typeof(RectTransform), typeof(Image));
            barGo.transform.SetParent(canvas, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = Vector2.zero;          // 左下角
            barRect.anchorMax = Vector2.zero;
            barRect.pivot = Vector2.zero;
            barRect.localScale = Vector3.one;
            barRect.anchoredPosition = new Vector2(posX, posY);
            barRect.sizeDelta = new Vector2(slotSize * 4 + spacing * 3, slotSize);

            Image barImage = barGo.GetComponent<Image>();
            barImage.color = new Color(0f, 0f, 0f, 0f);   // 完全透明的容器
            barImage.raycastTarget = false;

            log.AppendLine("[PASS] SkillBar 容器：锚点(0,0) Pos(" + posX + "," + posY + ") 尺寸(" +
                           barRect.sizeDelta.x + "x" + barRect.sizeDelta.y + ") 颜色A=0 RaycastTarget=关");

            int slotCount = SkillCaster.MaxSlots;
            var icons = new Image[slotCount];
            var masks = new Image[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                var slotGo = new GameObject("SkillSlot_" + (i + 1), typeof(RectTransform), typeof(Image));
                slotGo.transform.SetParent(barGo.transform, false);

                var slotRect = (RectTransform)slotGo.transform;
                slotRect.anchorMin = Vector2.zero;
                slotRect.anchorMax = Vector2.zero;
                slotRect.pivot = Vector2.zero;
                slotRect.localScale = Vector3.one;
                slotRect.anchoredPosition = new Vector2(i * (slotSize + spacing), 0f);
                slotRect.sizeDelta = new Vector2(slotSize, slotSize);

                Image iconImage = slotGo.GetComponent<Image>();
                iconImage.sprite = uiSprite;
                iconImage.color = Color.white;
                iconImage.type = Image.Type.Simple;
                iconImage.raycastTarget = false;
                icons[i] = iconImage;

                var maskGo = new GameObject("Cooldown", typeof(RectTransform), typeof(Image));
                maskGo.transform.SetParent(slotGo.transform, false);

                var maskRect = (RectTransform)maskGo.transform;
                maskRect.anchorMin = Vector2.zero;
                maskRect.anchorMax = Vector2.one;
                maskRect.pivot = new Vector2(0.5f, 0.5f);
                maskRect.offsetMin = Vector2.zero;
                maskRect.offsetMax = Vector2.zero;
                maskRect.localScale = Vector3.one;

                Image maskImage = maskGo.GetComponent<Image>();
                maskImage.sprite = uiSprite;
                maskImage.color = new Color(0f, 0f, 0f, 170f / 255f);
                maskImage.type = Image.Type.Filled;
                maskImage.fillMethod = Image.FillMethod.Radial360;
                maskImage.fillOrigin = (int)Image.Origin360.Top;
                maskImage.fillClockwise = true;
                maskImage.fillAmount = 0f;
                maskImage.raycastTarget = false;
                masks[i] = maskImage;

                log.AppendLine("[PASS] SkillSlot_" + (i + 1) + "：PosX=" + (i * (slotSize + spacing)) +
                               " 尺寸=" + slotSize + "x" + slotSize + "/Cooldown：" +
                               maskImage.type + "-" + maskImage.fillMethod + "-Top FillAmount=0");
            }

            var skillBarUI = canvas.GetComponent<SkillBarUI>();
            if (skillBarUI == null) skillBarUI = canvas.gameObject.AddComponent<SkillBarUI>();

            var so = new SerializedObject(skillBarUI);
            SerializedProperty slotsProp = so.FindProperty("slots");
            if (slotsProp == null)
            {
                log.AppendLine("[FAIL] SkillBarUI 里找不到 slots 字段");
            }
            else
            {
                slotsProp.arraySize = slotCount;
                for (int i = 0; i < slotCount; i++)
                {
                    SerializedProperty el = slotsProp.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative("icon").objectReferenceValue = icons[i];
                    el.FindPropertyRelative("cooldownMask").objectReferenceValue = masks[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("[PASS] Canvas.SkillBarUI：4 行 Slots 已接（Icon / CooldownMask）");
            }

            return barGo.transform;
        }

        private static void AssignSkillIcon(string skillPath, string spritePath, StringBuilder log)
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(skillPath);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

            if (skill == null) { log.AppendLine("[WARN] 加载不到 " + skillPath); return; }
            if (sprite == null) { log.AppendLine("[WARN] 加载不到 " + spritePath); return; }

            var so = new SerializedObject(skill);
            SerializedProperty iconProp = so.FindProperty("icon");
            if (iconProp == null) { log.AppendLine("[WARN] " + skill.name + " 里没有 icon 字段"); return; }

            iconProp.objectReferenceValue = sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("[PASS] " + skill.name + ".icon = " + sprite.name);
        }

        private static void SetRef(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty p = so.FindProperty(fieldName);
            if (p != null) p.objectReferenceValue = value;
        }

        private static Canvas FindOverlayCanvas(Scene scene)
        {
            foreach (Canvas c in Object.FindObjectsOfType<Canvas>(true))
            {
                if (c.gameObject.scene == scene && c.renderMode == RenderMode.ScreenSpaceOverlay) return c;
            }
            return null;
        }

        private static GameObject GetDirectChild(Transform parent, string childName)
        {
            Transform t = parent.Find(childName);
            return t != null ? t.gameObject : null;
        }

        private static Image GetOrAddImage(GameObject go)
        {
            Image img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            return img;
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            Transform cur = t.parent;
            while (cur != null) { path = cur.name + "/" + path; cur = cur.parent; }
            return path;
        }

        private static string Format(Vector3 v)
        {
            return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
        }

        private static string ColorText(Color c)
        {
            return "RGBA(" + Mathf.RoundToInt(c.r * 255f) + "," + Mathf.RoundToInt(c.g * 255f) + "," +
                   Mathf.RoundToInt(c.b * 255f) + "," + Mathf.RoundToInt(c.a * 255f) + ")";
        }
    }
}
