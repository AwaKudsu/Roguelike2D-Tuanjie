using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 一键把左上角的 HUD 重建成「立绘 + 血条 + 蓝条 + 技能栏」一整块（DNF 那种排法）。
    ///
    /// 布局（PlayerHud 容器，锚在屏幕左上角，24,-20 起）：
    ///   ┌────────┐ ████████████████████████  122/140
    ///   │  立绘  │ ██████████████░░░░░░░░░    48/60
    ///   └────────┘ [K][L][U][I]
    ///     110×110    血 430×36（y -6）· 蓝 340×24（y -50）· 技能 4×62（y -84）
    ///
    /// 为什么非要脚本做，不能手摆：这一套里有五个坑**全都不报错**——
    ///   ① Canvas 的 Rect Transform Scale 是 (0,0,0) → 整块 UI 被"缩成一个点"，Game 视图里什么都看不见；
    ///   ② 冷却遮罩的 Image Type 是 Simple → fillAmount 完全无效，遮罩永远不动；
    ///   ③ 冷却遮罩的 Source Image 为空 → 遮罩根本不画；
    ///   ④ 技能格的图 sprite 为空 → 格子透明，看着像没生成；
    ///   ⑤ 容器锚点留在 (0.5,0.5) → 技能栏跑到屏幕中间。
    /// 这五条在 Day 6 的排错表里占了一半。
    ///
    /// 另外，旧版 HUD 是把血条、蓝条、技能栏**各自直接挂在 Canvas 下**的，
    /// 所以它们只能靠手调坐标互相对齐；这里改成统一塞进一个 PlayerHud 容器，
    /// 以后想整体挪个位置只改容器一个坐标。
    ///
    /// 用法：编辑器里打开 Assets/_Project/Scenes/Game.unity，菜单 团结 → 修复 HUD 与技能栏。
    /// 重复运行是安全的（幂等：整块删掉重建，坐标永远是这里写的值）。
    /// </summary>
    public static class HudBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string SquareSpritePath = "Assets/_Project/Art/Placeholder/Square.png";
        private const string CircleSpritePath = "Assets/_Project/Art/Placeholder/Circle.png";
        private const string RingSpritePath = "Assets/_Project/Art/Effects/Ring.png";
        private const string SwordSpritePath = "Assets/_Project/Art/Effects/GreatSword.png";
        private const string FireballAssetPath = "Assets/_Project/Data/Skills/Skill_Fireball.asset";
        private const string WhirlwindAssetPath = "Assets/_Project/Data/Skills/Skill_Whirlwind.asset";
        private const string HealAssetPath = "Assets/_Project/Data/Skills/Skill_Heal.asset";
        private const string WarCryAssetPath = "Assets/_Project/Data/Skills/Skill_WarCry.asset";
        private const string MageRingPath = "Assets/_Project/Art/Characters/Mage/Mage_Ring.png";

        // ---------- 素材大师生成的 UI 图（先跑「团结 → 导入 UI 素材」把图搬进来）----------
        // 缺图时全部自动退回纯色块，不会报错，只是没有那么好看
        private const string UiArtDir = "Assets/_Project/Art/UI";
        private const string PanelArtPath = UiArtDir + "/UI_Panel.png";
        private const string SlotArtPath = UiArtDir + "/UI_Slot.png";
        private const string IconFireballArtPath = UiArtDir + "/Icon_Fireball.png";
        private const string IconWhirlwindArtPath = UiArtDir + "/Icon_Whirlwind.png";
        private const string IconHealArtPath = UiArtDir + "/Icon_Heal.png";
        private const string IconWarcryArtPath = UiArtDir + "/Icon_Warcry.png";

        // ---------- 布局数值（改这里，别的都不用动）----------
        // 整块 HUD 是一张「连起来的底板」：外面一圈描边 + 里面的深色底，
        // 立绘、血条、蓝条、技能格都长在这张板上 —— 造梦西游 / DNF 那种感觉，
        // 而不是几块各飘各的 UI。
        private static readonly Vector2 HudPos = new Vector2(24f, -20f);
        private static readonly Vector2 HudSize = new Vector2(600f, 174f);

        private const float PanelBorder = 2f;
        private static readonly Color PanelBorderColor = new Color(0.30f, 0.33f, 0.42f, 1f);
        private static readonly Color PanelFillColor = new Color(0.07f, 0.08f, 0.11f, 0.94f);

        private const float PortraitSize = 104f;
        private static readonly Vector2 PortraitPos = new Vector2(22f, -24f);

        /// <summary>立绘放大倍数：职业图标整张 320x480，人物只占中间一圈，不放大就是个空相框</summary>
        private const float PortraitZoom = 1.6f;

        // 面板九宫格的金边在 600x174 的板上大约占 24px，内容整体往里让开这一圈，
        // 不然立绘和血条会压在花纹上
        private const float BarX = 140f;                  // 条和技能栏的左边，让开立绘
        private const float BarWidth = 420f;              // 血条、蓝条同宽，右边对齐才像一整块
        private static readonly Vector2 HealthBarPos = new Vector2(BarX, -26f);
        private static readonly Vector2 HealthBarSize = new Vector2(BarWidth, 32f);
        private static readonly Vector2 ManaBarPos = new Vector2(BarX, -64f);
        private static readonly Vector2 ManaBarSize = new Vector2(BarWidth, 24f);

        private const float SlotSize = 62f;
        private const float SlotGap = 8f;

        /// <summary>技能栏在条下面居中（4 格 272 宽，比 420 的条窄，靠居中才不显得歪）</summary>
        private static readonly Vector2 SkillBarPos =
            new Vector2(BarX + (BarWidth - (4f * SlotSize + 3f * SlotGap)) * 0.5f, -94f);

        private static readonly Color BarBackground = new Color(52f / 255f, 54f / 255f, 64f / 255f, 250f / 255f);
        private static readonly Color FrameColor = new Color(0.16f, 0.17f, 0.22f, 1f);
        private static readonly Color HealthColor = new Color(214f / 255f, 63f / 255f, 63f / 255f, 1f);
        private static readonly Color ManaColor = new Color(66f / 255f, 133f / 255f, 244f / 255f, 1f);

        /// <summary>技能 1~4 的按键，和 PlayerInputReader 里的绑定一致（K / L / U / I）</summary>
        private static readonly string[] KeyLabels = { "K", "L", "U", "I" };

        /// <summary>重建前要清掉的名字。旧版 HUD 就是这几个，改结构后它们会被漏在场景里</summary>
        private static readonly string[] LegacyNames = { "PlayerHud", "HealthBar", "ManaBar", "SkillBar" };

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

            Font font = GetFont();
            if (font == null) log.AppendLine("[WARN] 取不到内置字体，血/蓝数字会看不见");

            // 条和填充用纯白方块：UISprite 是圆角九宫格，Simple 拉伸后两端会变成大圆角，
            // 440x32 的条看着像颗药丸。方块拉出来就是干净的直角，DNF 那种。
            Sprite flatSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SquareSpritePath);
            if (flatSprite == null)
            {
                flatSprite = uiSprite;
                log.AppendLine("[WARN] 取不到 " + SquareSpritePath + "，条会退回圆角贴图");
            }

            // ---------- 1. 先清掉旧布局 ----------
            // 旧版把三块直接挂在 Canvas 下，新版是 Canvas/PlayerHud/... 的父子结构。
            // 这里不按「直接子物体」删，而是把整个场景里的 UI 画布翻一遍按名字找 ——
            // 因为旧的 SkillBar 有可能被谁挪到中间层过，Find 只认直接子物体，会漏。
            RemoveLegacyHud(active, canvas, log);

            // ---------- 2. 容器 ----------
            RectTransform hudRoot = NewRect("PlayerHud", canvas.transform);
            hudRoot.anchorMin = new Vector2(0f, 1f);
            hudRoot.anchorMax = new Vector2(0f, 1f);
            hudRoot.pivot = new Vector2(0f, 1f);
            hudRoot.anchoredPosition = HudPos;
            hudRoot.sizeDelta = HudSize;
            MakeInvisible(hudRoot.gameObject);
            log.AppendLine("[PASS] PlayerHud：锚点(0,1) Pos(" + HudPos.x + "," + HudPos.y + ") 尺寸(" +
                           HudSize.x + "x" + HudSize.y + ")");

            // ---------- 底板 ----------
            // 优先用素材大师生成的那张面板图：九宫格拉伸，四角的花纹不会被拉糊。
            // 没有图就退回「2px 描边 + 深色底」两层纯色块 —— 难看，但至少不会缺 UI。
            Sprite panelArt = AssetDatabase.LoadAssetAtPath<Sprite>(PanelArtPath);
            Sprite slotArt = AssetDatabase.LoadAssetAtPath<Sprite>(SlotArtPath);

            if (panelArt != null)
            {
                RectTransform panelSkin = NewRect("PanelSkin", hudRoot);
                Place(panelSkin, Vector2.zero, HudSize);
                var skinImage = panelSkin.gameObject.AddComponent<Image>();
                skinImage.sprite = panelArt;
                skinImage.color = Color.white;
                skinImage.type = Image.Type.Sliced;
                skinImage.raycastTarget = false;
                log.AppendLine("[PASS] 底板：" + PanelArtPath + "（九宫格拉伸）");
            }
            else
            {
                // 兜底：外面一圈描边 + 里面深色底（两层都先建，才会排在最底下不被内容盖住）
                RectTransform panelBorder = NewRect("PanelBorder", hudRoot);
                Place(panelBorder, Vector2.zero, HudSize);
                var borderImage = panelBorder.gameObject.AddComponent<Image>();
                borderImage.sprite = flatSprite;
                borderImage.color = PanelBorderColor;
                borderImage.type = Image.Type.Simple;
                borderImage.raycastTarget = false;

                RectTransform panelFill = NewRect("PanelFill", panelBorder);
                Stretch(panelFill, PanelBorder);
                var fillImage = panelFill.gameObject.AddComponent<Image>();
                fillImage.sprite = flatSprite;
                fillImage.color = PanelFillColor;
                fillImage.type = Image.Type.Simple;
                fillImage.raycastTarget = false;

                log.AppendLine("[WARN] 没找到 " + PanelArtPath + "，底板退回纯色二层（先跑「团结 → 导入 UI 素材」）");
                log.AppendLine("[PASS] 底板：描边 " + PanelBorder + "px " + ColorText(PanelBorderColor) +
                               " / 内底 " + ColorText(PanelFillColor));
            }

            // ---------- 3. 立绘 ----------
            Image portrait = BuildPortrait(hudRoot, slotArt != null ? slotArt : flatSprite, log);

            // ---------- 4. 血条 / 蓝条（各带残影和数字）----------
            // 条的底框故意不用格子图：格子的金边按 PPU 换到 32px 高的条上要吃掉 8px 上下，
            // 一半高度都是框，反而糊。条就老老实实一个深色槽 + 2px 高光。
            StatBar healthBar = BuildBar(hudRoot, "HealthBar", "HealthFill", "HealthText",
                HealthBarPos, HealthBarSize, HealthColor, 22, null, flatSprite, font, log);
            StatBar manaBar = BuildBar(hudRoot, "ManaBar", "ManaFill", "ManaText",
                ManaBarPos, ManaBarSize, ManaColor, 19, null, flatSprite, font, log);

            // ---------- 5. 技能栏（挪到条下面）----------
            BuildSkillBar(hudRoot, slotArt, flatSprite, font, log);

            // ---------- 6. HUDController 接线 ----------
            var hud = canvas.GetComponent<HUDController>();
            if (hud == null) hud = canvas.gameObject.AddComponent<HUDController>();
            var hudSo = new SerializedObject(hud);
            SetRef(hudSo, "healthBar", healthBar);
            SetRef(hudSo, "manaBar", manaBar);
            SetRef(hudSo, "portrait", portrait);
            SetRef(hudSo, "healthText", healthBar.transform.Find("HealthText").GetComponent<Text>());
            SetRef(hudSo, "manaText", manaBar.transform.Find("ManaText").GetComponent<Text>());

            // 数据源也一并写死，不依赖运行时的 FindGameObjectWithTag：
            // 那条后路留着（HUDController 里还有），但能静态接上就别让它去猜
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                SetRef(hudSo, "playerHealth", player.GetComponent<Health>());
                SetRef(hudSo, "playerMana", player.GetComponent<ManaPool>());
                SetRef(hudSo, "playerStats", player.GetComponent<CharacterStats>());
                log.AppendLine("[PASS] HUDController 数据源：Player 上的 Health / ManaPool / CharacterStats 已接");
            }
            else
            {
                log.AppendLine("[WARN] 场景里没有 Tag=Player 的对象，数据源留给运行时自动查找");
            }

            hudSo.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("[PASS] Canvas.HUDController：healthBar / manaBar / portrait / healthText / manaText 已接");

            // ---------- 7. 技能图标 ----------
            // SkillBarUI 每帧把格子的 sprite 赋成 skill.icon，空的就是「什么都不画」。
            // 优先用素材大师生成的技能图标，没有才退回旧的特效图。
            AssignSkillIcon(FireballAssetPath, PreferArt(IconFireballArtPath, RingSpritePath), log);
            AssignSkillIcon(WhirlwindAssetPath, PreferArt(IconWhirlwindArtPath, SwordSpritePath), log);
            AssignSkillIcon(HealAssetPath, PreferArt(IconHealArtPath, MageRingPath), log);
            AssignSkillIcon(WarCryAssetPath, PreferArt(IconWarcryArtPath, SwordSpritePath), log);

            // ---------- 8. 存盘 ----------
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            AssetDatabase.SaveAssets();
            log.AppendLine("[SUMMARY] 已存盘。容器 = " + GetPath(hudRoot));

            string text = log.ToString();
            Debug.Log(text);
            System.IO.File.WriteAllText("_Tuanjie/hud-report.txt", text, new UTF8Encoding(false));
            EditorUtility.DisplayDialog("修复完成", text, "好");
        }

        // ------------------------------------------------------------------ 立绘

        private static Image BuildPortrait(RectTransform parent, Sprite frameSprite, StringBuilder log)
        {
            RectTransform frame = NewRect("PortraitFrame", parent);
            Place(frame, PortraitPos, new Vector2(PortraitSize, PortraitSize));
            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.sprite = frameSprite;
            frameImage.color = frameSprite != null ? Color.white : FrameColor;
            frameImage.type = frameSprite != null && frameSprite.border != Vector4.zero
                ? Image.Type.Sliced
                : Image.Type.Simple;
            frameImage.raycastTarget = false;

            // 职业图标是 320x480 的整张待机图，人物只占中间一小块。
            // 直接铺满的话人物会被缩得很小，所以放大 1.6 倍再拿矩形遮罩裁掉多余的部分 ——
            // 效果就是「半个身位的头像」，而不是一张空相框。
            frame.gameObject.AddComponent<RectMask2D>();

            RectTransform portraitRect = NewRect("Portrait", frame);
            portraitRect.anchorMin = new Vector2(0.5f, 0.5f);
            portraitRect.anchorMax = new Vector2(0.5f, 0.5f);
            portraitRect.pivot = new Vector2(0.5f, 0.5f);
            portraitRect.anchoredPosition = new Vector2(0f, 0f);
            portraitRect.sizeDelta = new Vector2(PortraitSize * PortraitZoom, PortraitSize * PortraitZoom * 1.5f);

            var portrait = portraitRect.gameObject.AddComponent<Image>();
            portrait.sprite = frameSprite;
            portrait.color = Color.white;
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            portrait.enabled = false;            // 没接 CharacterStats 之前先不画

            log.AppendLine("[PASS] PortraitFrame：Pos(" + PortraitPos.x + "," + PortraitPos.y + ") 尺寸(" +
                           PortraitSize + "x" + PortraitSize +
                           ") + RectMask2D / Portrait：放大 " + PortraitZoom + " 倍再被矩形裁掉" +
                           "（图由 HUDController 从 ClassData.icon 取）");
            return portrait;
        }

        // ------------------------------------------------------------------ 条

        /// <summary>
        /// 一根条 = 底框 + 残影 + 填充 + 右对齐数字。
        /// 三层都是 inset 的（内缩 3），这样底框会露出一圈边，看着像有框，而不是一块色块。
        /// </summary>
        private static StatBar BuildBar(RectTransform parent, string barName, string fillName, string textName,
                                        Vector2 pos, Vector2 size, Color fillColor, int fontSize,
                                        Sprite bgSprite, Sprite fillSprite, Font font, StringBuilder log)
        {
            RectTransform bar = NewRect(barName, parent);
            Place(bar, pos, size);
            // 底框：有格子图就用（九宫格），没有就是一块深色方块
            var barImage = bar.gameObject.AddComponent<Image>();
            barImage.sprite = bgSprite != null ? bgSprite : fillSprite;
            barImage.color = bgSprite != null ? Color.white : BarBackground;
            barImage.type = bgSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            barImage.raycastTarget = false;

            // 残影：比填充亮一点的颜色，扣血之后慢慢缩回去
            Image ghost = MakeFill(bar, "Ghost", fillSprite, Color.Lerp(fillColor, Color.white, 0.62f), 3f);
            ghost.color = new Color(ghost.color.r, ghost.color.g, ghost.color.b, 0.95f);

            Image fill = MakeFill(bar, fillName, fillSprite, fillColor, 3f);

            // 顶部一条淡白高光：纯色块太平，加一条 2px 的亮线就有「玻璃条」的层次感。
            // 它铺在整根条上、不跟着 fill 动画走，所以不会出现高光比血还长的情况。
            RectTransform shineRect = NewRect("Shine", bar);
            shineRect.anchorMin = new Vector2(0f, 1f);
            shineRect.anchorMax = new Vector2(1f, 1f);
            shineRect.pivot = new Vector2(0.5f, 1f);
            shineRect.anchoredPosition = new Vector2(0f, -2f);
            shineRect.sizeDelta = new Vector2(-12f, 2f);
            var shine = shineRect.gameObject.AddComponent<Image>();
            shine.sprite = fillSprite;
            shine.color = new Color(1f, 1f, 1f, 0.12f);
            shine.raycastTarget = false;

            RectTransform textRect = NewRect(textName, bar);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);

            var text = textRect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleRight;
            text.color = new Color(0.96f, 0.97f, 1f, 1f);
            text.text = "0 / 0";
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 条的颜色会随血量变化，纯白字压在红条上会糊；描一圈黑边最省事
            var outline = textRect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var statBar = bar.gameObject.AddComponent<StatBar>();
            var so = new SerializedObject(statBar);
            SetRef(so, "fill", fill);
            SetRef(so, "ghost", ghost);
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("[PASS] " + barName + "：Pos(" + pos.x + "," + pos.y + ") 尺寸(" + size.x + "x" +
                           size.y + ") 底框=" + ColorText(BarBackground) + " / " + fillName +
                           "：Filled-Horizontal-Left " + ColorText(fillColor) +
                           " / Ghost：残影已接 / " + textName + "：右对齐 " + fontSize + " 号带描边");
            return statBar;
        }

        private static Image MakeFill(RectTransform bar, string name, Sprite barSprite, Color color, float inset)
        {
            RectTransform rect = NewRect(name, bar);
            Stretch(rect, inset);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = barSprite;
            image.color = color;
            image.type = Image.Type.Filled;                       // ★ 必须是 Filled
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            image.raycastTarget = false;
            return image;
        }

        // ------------------------------------------------------------------ 技能栏

        /// <summary>重建技能栏：透明容器 + 4 个格子 + 4 个 Filled 冷却遮罩 + 按键角标 + SkillBarUI 接线</summary>
        private static Transform BuildSkillBar(RectTransform parent, Sprite frameSprite, Sprite flatSprite,
                                               Font font, StringBuilder log)
        {
            int slotCount = SkillCaster.MaxSlots;

            RectTransform bar = NewRect("SkillBar", parent);
            Place(bar, SkillBarPos, new Vector2(slotCount * SlotSize + (slotCount - 1) * SlotGap, SlotSize));
            MakeInvisible(bar.gameObject);

            log.AppendLine("[PASS] SkillBar 容器：Pos(" + SkillBarPos.x + "," + SkillBarPos.y + ") 尺寸(" +
                           bar.sizeDelta.x + "x" + bar.sizeDelta.y + ") 全透明");

            var icons = new Image[slotCount];
            var masks = new Image[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                RectTransform slot = NewRect("SkillSlot_" + (i + 1), bar);
                Place(slot, new Vector2(i * (SlotSize + SlotGap), 0f), new Vector2(SlotSize, SlotSize));

                // 格子底框：单独一张图，这样 SkillBarUI 只改里面的 Icon，
                // 不会把底框也刷成技能图标（之前就是一张图两用，格子看着像没有框）
                var slotFrame = slot.gameObject.AddComponent<Image>();
                slotFrame.sprite = frameSprite != null ? frameSprite : flatSprite;
                slotFrame.color = frameSprite != null ? Color.white : new Color(0.13f, 0.14f, 0.18f, 0.98f);
                slotFrame.type = frameSprite != null && frameSprite.border != Vector4.zero
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                slotFrame.raycastTarget = false;

                RectTransform iconRect = NewRect("Icon", slot);
                // 金边吃掉一圈，图标往里缩 8px 才不会被框压住
                Stretch(iconRect, frameSprite != null ? 8f : 4f);

                var iconImage = iconRect.gameObject.AddComponent<Image>();
                // 故意不给默认图：SkillBarUI 每帧会把技能图标写进来，
                // 这里留空，没技能的格子就只看见底框，而不是一块白方块
                iconImage.color = Color.white;
                iconImage.type = Image.Type.Simple;
                iconImage.raycastTarget = false;
                icons[i] = iconImage;

                RectTransform maskRect = NewRect("Cooldown", slot);
                Stretch(maskRect, 0f);

                var maskImage = maskRect.gameObject.AddComponent<Image>();
                maskImage.sprite = flatSprite;
                maskImage.color = new Color(0f, 0f, 0f, 175f / 255f);
                maskImage.type = Image.Type.Filled;
                maskImage.fillMethod = Image.FillMethod.Radial360;
                maskImage.fillOrigin = (int)Image.Origin360.Top;
                maskImage.fillClockwise = true;
                maskImage.fillAmount = 0f;
                maskImage.raycastTarget = false;
                masks[i] = maskImage;

                // 按键角标：右下角一个小字母，一眼看出这格是按哪个键
                RectTransform keyRect = NewRect("Key", slot);
                keyRect.anchorMin = new Vector2(1f, 0f);
                keyRect.anchorMax = new Vector2(1f, 0f);
                keyRect.pivot = new Vector2(1f, 0f);
                keyRect.anchoredPosition = new Vector2(-2f, 1f);
                keyRect.sizeDelta = new Vector2(30f, 26f);

                var key = keyRect.gameObject.AddComponent<Text>();
                key.font = font;
                key.fontSize = 20;
                key.fontStyle = FontStyle.Bold;
                key.alignment = TextAnchor.LowerRight;
                key.color = new Color(0.86f, 0.9f, 1f, 0.95f);
                key.text = i < KeyLabels.Length ? KeyLabels[i] : (i + 1).ToString();
                key.raycastTarget = false;
                key.horizontalOverflow = HorizontalWrapMode.Overflow;

                var keyOutline = keyRect.gameObject.AddComponent<Outline>();
                keyOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                keyOutline.effectDistance = new Vector2(1f, -1f);

                log.AppendLine("[PASS] SkillSlot_" + (i + 1) + "：PosX=" + (i * (SlotSize + SlotGap)) +
                               " 尺寸=" + SlotSize + "x" + SlotSize + " 角标=" + key.text +
                               " /Cooldown：" + maskImage.type + "-" + maskImage.fillMethod + "-Top FillAmount=0");
            }

            var skillBarUI = parent.GetComponentInParent<Canvas>().GetComponent<SkillBarUI>();
            if (skillBarUI == null) skillBarUI = parent.GetComponentInParent<Canvas>().gameObject.AddComponent<SkillBarUI>();

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

            return bar;
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>有生成的图标就用生成的，没有退回旧的特效图（没跑过导入菜单也不会挂）</summary>
        private static string PreferArt(string artPath, string fallbackPath)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(artPath) != null ? artPath : fallbackPath;
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

        private static Font GetFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        /// <summary>左上角对齐的固定尺寸矩形（HUD 里绝大多数东西都是这种）</summary>
        private static void Place(RectTransform rect, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        /// <summary>四边内缩铺满父物体</summary>
        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localScale = Vector3.one;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>容器用：加一张完全透明的 Image 当背景，不吃点击</summary>
        private static void MakeInvisible(GameObject go)
        {
            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = false;
        }

        private static void RemoveLegacyHud(Scene scene, Canvas target, StringBuilder log)
        {
            var hits = new List<Transform>();

            foreach (Canvas c in Object.FindObjectsOfType<Canvas>(true))
            {
                if (c.gameObject.scene != scene) continue;
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;

                foreach (RectTransform rt in c.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rt == (RectTransform)c.transform) continue;
                    if (System.Array.IndexOf(LegacyNames, rt.name) < 0) continue;
                    hits.Add(rt);
                }
            }

            int removed = 0;
            foreach (Transform t in hits)
            {
                if (t == null) continue;                       // 已经跟着父物体一起删掉了
                if (HasLegacyAncestor(t, hits)) continue;      // 只删最上面那个，子物体跟着走

                string path = GetPath(t);
                Object.DestroyImmediate(t.gameObject);
                removed++;
                log.AppendLine("[INFO] 删掉了旧的 " + path);
            }

            log.AppendLine(removed > 0
                ? "[PASS] 清掉 " + removed + " 个旧 HUD 物体（旧的条、旧的技能栏、旧的容器）"
                : "[INFO] 没有需要清理的旧 HUD 物体");

            if (target == null) log.AppendLine("[WARN] target canvas 为空");
        }

        private static bool HasLegacyAncestor(Transform t, List<Transform> hits)
        {
            Transform p = t.parent;
            while (p != null)
            {
                if (hits.Contains(p)) return true;
                p = p.parent;
            }
            return false;
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
