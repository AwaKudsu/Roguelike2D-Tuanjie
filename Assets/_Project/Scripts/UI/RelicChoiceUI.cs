using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 三选一面板。整个界面在运行时用代码搭出来，不依赖场景里预先摆好的 UI。
///
/// 为什么不用「在场景里手搭」：
///   1. 面板只在选词条时出现，手搭的话要给 12 个 Text / Image 拖引用，错一个就静默不显示；
///   2. 中文字形必须用系统字体（Unity 内置的 Arial / LegacyRuntime 没有中文，只会显示方块），
///      而系统动态字体不是资产文件，没法序列化进场景 —— 只能在运行时赋值。
///   代价是不能在 Inspector 里可视化调，改版式要改这里的代码。对一个 400 行的面板来说划算。
///
/// 用法：什么都不用做。RelicManager 在需要时会自己 new 一个。
/// 想手动摆：场景里建一个空物体挂上本组件，把 RelicManager 的 Choice Ui 指向它即可。
/// </summary>
public class RelicChoiceUI : MonoBehaviour
{
    [Header("版式")]
    [Tooltip("卡片宽度")]
    [SerializeField] private float cardWidth = 400f;

    [Tooltip("卡片高度")]
    [SerializeField] private float cardHeight = 330f;

    [Tooltip("卡片之间的水平间距")]
    [SerializeField] private float cardGap = 50f;

    /// <summary>一张卡片上的所有控件</summary>
    private class CardView
    {
        public RectTransform root;
        public Image border;
        public Image fill;
        public Image icon;
        public Text name;
        public Text rarity;
        public Text description;
        public Text hotkey;
    }

    private static Sprite _whiteSprite;
    private static Font _uiFont;

    private RectTransform _panel;
    private Text _title;
    private Text _hint;
    private CardView[] _cards;

    private IReadOnlyList<RelicData> _choices;
    private Action<RelicData> _onPick;
    private bool _visible;

    // ---------------- 生命周期 ----------------

    private void Awake()
    {
        Build();

        // Build 万一中途失败，_panel 会是 null —— 别在这里再抛一个空引用，
        // 那只会把真正的错误埋掉。
        if (_panel != null) _panel.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_visible) return;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) { Pick(0); return; }
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) { Pick(1); return; }
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) { Pick(2); return; }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 point = mouse.position.ReadValue();
            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] == null || !_cards[i].root.gameObject.activeSelf) continue;
                // Overlay 画布的相机参数传 null
                if (RectTransformUtility.RectangleContainsScreenPoint(_cards[i].root, point, null))
                {
                    Pick(i);
                    return;
                }
            }
        }
    }

    // ---------------- 对外 ----------------

    /// <summary>弹出三选一。选完会回调 onPick，然后把世界的时间恢复</summary>
    public void Show(IReadOnlyList<RelicData> choices, Action<RelicData> onPick)
    {
        if (choices == null || choices.Count == 0) return;
        if (_panel == null) Build();

        _choices = choices;
        _onPick = onPick;
        _visible = true;

        for (int i = 0; i < _cards.Length; i++)
        {
            bool has = i < choices.Count;
            _cards[i].root.gameObject.SetActive(has);
            if (has) Apply(choices[i], i);
        }

        _hint.text = choices.Count > 1
            ? string.Format("按 1 ~ {0} 或点一下卡片", choices.Count)
            : "点一下卡片";

        _panel.gameObject.SetActive(true);
        _panel.SetAsLastSibling();

        // 选之前把世界停住：不这么做，玩家会一边选一边被敌人打死。
        // 走 TimeFreeze 而不是直接写 timeScale —— 暂停菜单也会冻结时间，
        // 各自直接写 0/1 会互相覆盖：谁后关谁说了算，另一个弹窗会被提前解冻。
        TimeFreeze.Freeze(this);
    }

    /// <summary>关掉面板并恢复时间</summary>
    public void Hide()
    {
        _visible = false;
        _onPick = null;
        _choices = null;

        if (_panel != null) _panel.gameObject.SetActive(false);

        TimeFreeze.Unfreeze(this);
    }

    private void Pick(int index)
    {
        if (!_visible || _choices == null) return;
        if (index < 0 || index >= _choices.Count) return;

        RelicData picked = _choices[index];
        Action<RelicData> callback = _onPick;

        Hide();
        if (callback != null) callback(picked);
    }

    // ---------------- 搭界面 ----------------

    private void Build()
    {
        // 1. 自己和画布
        //
        // ★ 这里绝不能写 (RectTransform)transform ——
        //   用 new GameObject(...) 建出来的物体挂的是普通 Transform，
        //   强制转换会抛 InvalidCastException，整个 Build 当场中断：
        //   面板一张卡都建不出来，而异常还会顺着 RoomChanged 事件
        //   一路炸穿 RoomManager.AdvanceRoutine，让黑幕再也淡不回去。
        //   （这个坑已经踩过一次：上一关结束切下一关时画面永久全黑。）
        RectTransform self = transform as RectTransform;
        if (self == null)
        {
            // 没有 RectTransform 也能搭：子节点自己带 RectTransform，
            // 只要父级是 Canvas 就行。正常情况下 RelicManager 会建出带
            // RectTransform 的物体，走到这里说明是手挂的，留个提示即可。
            Debug.LogWarning("[Relic UI] 本物体没有 RectTransform，按普通 Transform 继续搭界面。", this);
        }

        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;   // 必须压过 HUD 的 Canvas

        CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // 2. 全屏暗幕
        _panel = NewRect("Panel", transform);
        Stretch(_panel);
        Image backdrop = NewImage(_panel, new Color(0f, 0f, 0f, 0.82f));
        backdrop.raycastTarget = true;

        // 3. 标题
        RectTransform titleRect = NewRect("Title", _panel);
        AnchorTopCenter(titleRect, 0f, -110f, 1200f, 80f);
        _title = NewText(titleRect, "选择一项强化", 46, new Color(0.96f, 0.96f, 0.98f), TextAnchor.MiddleCenter);

        RectTransform hintRect = NewRect("Hint", _panel);
        AnchorTopCenter(hintRect, 0f, -190f, 1200f, 40f);
        _hint = NewText(hintRect, "按 1 / 2 / 3 或点一下卡片", 24, new Color(0.65f, 0.68f, 0.75f), TextAnchor.MiddleCenter);

        // 4. 三张卡片
        _cards = new CardView[3];
        float step = cardWidth + cardGap;
        float startX = -step * (_cards.Length - 1) * 0.5f;

        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i] = BuildCard(i, startX + step * i);
        }
    }

    private CardView BuildCard(int index, float offsetX)
    {
        CardView card = new CardView();

        card.root = NewRect("Card_" + (index + 1), _panel);
        card.root.anchorMin = new Vector2(0.5f, 0.5f);
        card.root.anchorMax = new Vector2(0.5f, 0.5f);
        card.root.pivot = new Vector2(0.5f, 0.5f);
        card.root.sizeDelta = new Vector2(cardWidth, cardHeight);
        card.root.anchoredPosition = new Vector2(offsetX, -40f);

        card.border = NewImage(card.root, Color.white);
        card.border.raycastTarget = true;   // 卡片要能接点击

        // 内层：比外框小 4 像素，做出「边框 + 卡面」的层次
        RectTransform inner = NewRect("Fill", card.root);
        Stretch(inner, 4f);
        card.fill = NewImage(inner, new Color(0.13f, 0.14f, 0.17f, 0.98f));

        RectTransform iconRect = NewRect("Icon", card.root);
        AnchorTopCenter(iconRect, 0f, -46f, 88f, 88f);
        card.icon = NewImage(iconRect, Color.white);

        RectTransform nameRect = NewRect("Name", card.root);
        AnchorTopCenter(nameRect, 0f, -150f, cardWidth - 40f, 46f);
        card.name = NewText(nameRect, "词条名", 30, Color.white, TextAnchor.MiddleCenter);

        RectTransform rarityRect = NewRect("Rarity", card.root);
        AnchorTopCenter(rarityRect, 0f, -196f, cardWidth - 40f, 30f);
        card.rarity = NewText(rarityRect, "普通", 20, Color.gray, TextAnchor.MiddleCenter);

        RectTransform descRect = NewRect("Desc", card.root);
        AnchorTopCenter(descRect, 0f, -232f, cardWidth - 60f, 70f);
        card.description = NewText(descRect, "效果描述", 22, new Color(0.82f, 0.84f, 0.89f), TextAnchor.UpperCenter);

        RectTransform keyRect = NewRect("Hotkey", card.root);
        keyRect.anchorMin = new Vector2(0.5f, 0f);
        keyRect.anchorMax = new Vector2(0.5f, 0f);
        keyRect.pivot = new Vector2(0.5f, 0f);
        keyRect.sizeDelta = new Vector2(cardWidth - 40f, 34f);
        keyRect.anchoredPosition = new Vector2(0f, 14f);
        card.hotkey = NewText(keyRect, "按 " + (index + 1), 22, new Color(0.55f, 0.75f, 1f), TextAnchor.MiddleCenter);

        return card;
    }

    private void Apply(RelicData relic, int index)
    {
        CardView card = _cards[index];
        Color theme = relic.RarityColor;

        card.border.color = theme;
        card.icon.sprite = relic.icon != null ? relic.icon : WhiteSprite;
        card.icon.color = relic.icon != null ? Color.white : theme;
        card.name.text = relic.displayName;
        card.rarity.text = relic.RarityLabel;
        card.rarity.color = theme;
        card.description.text = relic.description;
        card.hotkey.text = "按 " + (index + 1);
    }

    // ---------------- 小工具 ----------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = WhiteSprite;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text NewText(RectTransform rect, string content, int size, Color color, TextAnchor anchor)
    {
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = UiFont;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void AnchorTopCenter(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>一张 8×8 纯白精灵，给所有纯色块用。不用内置图，免得依赖引擎内置资源</summary>
    private static Sprite WhiteSprite
    {
        get
        {
            if (_whiteSprite != null) return _whiteSprite;

            Texture2D tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "RelicUI_White";
            return _whiteSprite;
        }
    }

    /// <summary>
    /// 中文字体。Unity 内置字体没有中文字形，必须问系统要（Windows 上微软雅黑一定有）。
    /// 这是把面板放到运行时搭的另一个原因 —— 动态字体不是资产，进不了场景序列化。
    /// </summary>
    private static Font UiFont
    {
        get
        {
            if (_uiFont != null) return _uiFont;

            _uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "PingFang SC", "Arial" },
                32);

            if (_uiFont == null)
            {
                Debug.LogWarning("[Relic UI] 找不到任何系统字体，中文会显示成方块。请装一个中文字体，或在 UiFont 里加字体名。");
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return _uiFont;
        }
    }
}
