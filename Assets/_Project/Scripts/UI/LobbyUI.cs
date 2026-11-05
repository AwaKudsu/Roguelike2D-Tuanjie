using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 等待大厅：进游戏先停在这里选职业，点了「开始游戏」才真正开局。
///
/// 为什么要单独一个界面而不是塞进暂停菜单（`GameMenuUI` 里已经有个「大厅」页）：
///   · 大厅是**开局前**的东西，暂停菜单是**开局后**的东西，两者叠在一起会互相挡；
///   · 大厅要能按左右键换角色、要能看见职业的全套数值，和「继续 / 设置 / 改键」不是一个信息密度。
/// 所以这里独立一套，暂停菜单保持原样。
///
/// 界面在代码里搭：中文字形必须用运行时动态字体（不是资产，序列化不进场景），
/// 和 `RelicChoiceUI` / `InventoryUI` / `GameMenuUI` 是同一套做法。
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private const float CardWidth = 460f;
    private const float CardHeight = 640f;
    private const float CardGap = 96f;
    private const int SortingOrder = 350;

    private sealed class Card
    {
        public GameObject Root;
        public Image Border;
        public Image Portrait;
        public Text Name;
        public Text Description;
        public Text Stats;
        public Text Skills;
        public ClassData Data;
    }

    private Canvas _canvas;
    private RectTransform _panel;
    private RectTransform _cardsRoot;
    private Text _startLabel;
    private Text _hint;

    private readonly List<Card> _cards = new();
    private static readonly Color Accent = new Color(1f, 0.78f, 0.32f, 1f);
    private static readonly Color CardIdle = new Color(0.16f, 0.17f, 0.22f, 1f);
    private static readonly Color CardFocus = new Color(0.24f, 0.21f, 0.16f, 1f);

    private List<ClassData> _classes = new();
    private int _index;
    private bool _open;

    /// <summary>是不是正停在大厅里</summary>
    public bool IsOpen => _open;

    /// <summary>当前高亮的职业（自检用）</summary>
    public ClassData Current => _index >= 0 && _index < _cards.Count ? _cards[_index].Data : null;

    public int CardCount => _cards.Count;

    private void Awake()
    {
        Build();
        SetVisible(false);
    }

    private void Update()
    {
        if (!_open) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) Move(-1);
        if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Move(1);
        if (kb.digit1Key.wasPressedThisFrame) Focus(0);
        if (kb.digit2Key.wasPressedThisFrame) Focus(1);
        if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) StartGame();
    }

    // ---------------- 对外 ----------------

    public void Show()
    {
        Refresh();
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
        TimeFreeze.Unfreeze(this);
        SetPlayerInput(true);
    }

    /// <summary>大厅里点「开始游戏」：把选中的职业装上，然后交给 GameBootstrap 开局</summary>
    public void StartGame()
    {
        ClassData picked = Current;

        if (picked != null)
        {
            var switcher = FindObjectOfType<PlayerVisualSwitcher>();
            if (switcher != null)
            {
                switcher.SetClass(picked);
            }
            else
            {
                // 没有切换器（比如以后做了独立的大厅场景）：至少把数值换上
                var stats = GameObject.FindGameObjectWithTag("Player")?.GetComponent<CharacterStats>();
                if (stats != null) stats.ApplyClass(picked);
            }
        }

        Hide();
        GameBootstrap.StartRun();
    }

    // ---------------- 内部 ----------------

    private void Move(int delta)
    {
        if (_cards.Count == 0) return;
        Focus((_index + delta + _cards.Count) % _cards.Count);
    }

    private void Focus(int index)
    {
        if (_cards.Count == 0) return;
        _index = Mathf.Clamp(index, 0, _cards.Count - 1);
        ApplyHighlight();
    }

    private void Refresh()
    {
        _classes = CollectClasses();
        RebuildCards();

        // 进大厅时默认高亮玩家当前职业，免得「我明明玩的是法师，大厅却停在战士」
        _index = 0;
        var switcher = FindObjectOfType<PlayerVisualSwitcher>();
        if (switcher != null && switcher.CurrentClassIndex >= 0)
        {
            _index = Mathf.Clamp(switcher.CurrentClassIndex, 0, Mathf.Max(0, _cards.Count - 1));
        }

        ApplyHighlight();
    }

    private List<ClassData> CollectClasses()
    {
        var list = new List<ClassData>();

        var switcher = FindObjectOfType<PlayerVisualSwitcher>();
        if (switcher != null) list.AddRange(switcher.Classes);

        if (list.Count == 0)
        {
            // 没配职业表时的兜底：直接按工程里的资产顺序摆出来
            ClassData[] all = Resources.FindObjectsOfTypeAll<ClassData>();
            foreach (ClassData data in all)
            {
                if (data != null && !list.Contains(data)) list.Add(data);
            }
        }

        return list;
    }

    private void RebuildCards()
    {
        foreach (Card card in _cards)
        {
            if (card.Root != null) Destroy(card.Root);
        }

        _cards.Clear();

        if (_cardsRoot == null) return;

        int count = Mathf.Clamp(_classes.Count, 0, 4);
        float totalWidth = count * CardWidth + Mathf.Max(0, count - 1) * CardGap;
        float left = -totalWidth * 0.5f + CardWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            ClassData data = _classes[i];
            float x = left + i * (CardWidth + CardGap);

            RectTransform root = NewRect(_cardsRoot, $"Card_{i}", new Vector2(CardWidth, CardHeight), new Vector2(x, -10f));
            var background = root.gameObject.AddComponent<Image>();
            background.sprite = WhiteSprite;
            background.color = CardIdle;
            background.raycastTarget = true;

            RectTransform borderRect = NewRect(root, "Border", new Vector2(CardWidth + 8f, CardHeight + 8f), Vector2.zero);
            var border = borderRect.gameObject.AddComponent<Image>();
            border.sprite = WhiteSprite;
            border.raycastTarget = false;
            border.color = new Color(0f, 0f, 0f, 0f);
            borderRect.SetAsFirstSibling();

            var card = new Card
            {
                Root = root.gameObject,
                Border = border,
                Portrait = MakePortrait(root, data),
                Name = NewText(root, "Name", data != null ? data.className : "未知", 46, new Vector2(0f, 132f), Accent, 380f),
                Description = NewText(root, "Description", data != null ? data.description : "", 24,
                                      new Vector2(0f, 34f), new Color(0.82f, 0.85f, 0.9f, 1f), 390f, 84f),
                Stats = NewText(root, "Stats", StatsLine(data), 23, new Vector2(0f, -80f),
                                new Color(0.75f, 0.79f, 0.86f, 1f), 400f, 56f),
                Skills = NewText(root, "Skills", SkillsLine(data), 23, new Vector2(0f, -178f),
                                 new Color(0.68f, 0.74f, 0.82f, 1f), 400f, 80f),
                Data = data,
            };

            NewText(root, "Index", $"{(i + 1)}", 30, new Vector2(-CardWidth * 0.5f + 34f, CardHeight * 0.5f - 34f),
                    new Color(1f, 1f, 1f, 0.35f), 60f);

            int captured = i;
            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                Focus(captured);
                StartGame();
            });

            _cards.Add(card);
        }
    }

    private Image MakePortrait(RectTransform parent, ClassData data)
    {
        RectTransform rect = NewRect(parent, "Portrait", new Vector2(300f, 300f), new Vector2(0f, 300f));
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = PortraitSprite(data);
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private Sprite PortraitSprite(ClassData data)
    {
        if (data == null) return null;
        if (data.icon != null) return data.icon;

        // 没配头像就用该职业的待机第一帧 —— 两套动画的字段里都有
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return null;

        bool mage = data.className != null && data.className.Contains("法");
        if (mage)
        {
            var visual = player.GetComponent<MageVisual>();
            Sprite[] frames = visual != null ? visual.DebugIdleFrames() : null;
            if (frames != null && frames.Length > 0) return frames[0];
        }
        else
        {
            var visual = player.GetComponent<WarriorVisual>();
            Sprite[] frames = visual != null ? visual.DebugIdleFrames() : null;
            if (frames != null && frames.Length > 0) return frames[0];
        }

        return null;
    }

    private static string StatsLine(ClassData data)
    {
        if (data == null) return "";
        return $"生命 {data.maxHealth:0}　攻击 {data.attack:0}　移速 {data.moveSpeed:0.0}\n法力 {data.maxMana:0}　暴击 {data.critChance * 100f:0}%　防御 {data.defense * 100f:0}%";
    }

    private static string SkillsLine(ClassData data)
    {
        if (data == null || data.startingSkills == null || data.startingSkills.Count == 0) return "没有初始技能";

        string[] keys = { "K", "L", "U", "I" };
        var parts = new List<string>();
        for (int i = 0; i < data.startingSkills.Count && i < keys.Length; i++)
        {
            SkillData skill = data.startingSkills[i];
            if (skill != null) parts.Add($"{keys[i]}　{skill.displayName}");
        }

        return parts.Count > 0 ? "技能\n" + string.Join("　·　", parts) : "没有初始技能";
    }

    private void ApplyHighlight()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            Card card = _cards[i];
            bool focused = i == _index;

            if (card.Border != null) card.Border.color = focused ? Accent : new Color(0f, 0f, 0f, 0f);
            if (card.Root != null)
            {
                var image = card.Root.GetComponent<Image>();
                if (image != null) image.color = focused ? CardFocus : CardIdle;
                card.Root.transform.localScale = Vector3.one * (focused ? 1.03f : 0.98f);
            }
        }

        if (_startLabel != null && Current != null) _startLabel.text = $"开始游戏 · {Current.className}";
        if (_hint != null) _hint.text = "A / D 或 ← → 换职业　·　Enter 开始　·　1 / 2 直选";
    }

    private void SetVisible(bool visible)
    {
        _open = visible;
        if (_canvas != null) _canvas.enabled = visible;
        if (_panel != null) _panel.gameObject.SetActive(visible);

        if (visible)
        {
            TimeFreeze.Freeze(this);
            SetPlayerInput(false);
            if (Keyboard.current != null) { /* 保持焦点提示：无操作 */ }
        }
    }

    private void SetPlayerInput(bool enabled)
    {
        var reader = FindObjectOfType<PlayerInputReader>();
        if (reader != null) reader.enabled = enabled;
    }

    // ---------------- 搭界面 ----------------

    private void Build()
    {
        var canvasGo = new GameObject("LobbyCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        _panel = NewRect(canvasGo.transform, "Panel", new Vector2(1920f, 1080f), Vector2.zero);
        var panelImage = _panel.gameObject.AddComponent<Image>();
        panelImage.sprite = WhiteSprite;
        panelImage.color = new Color(0.05f, 0.06f, 0.09f, 1f);
        panelImage.raycastTarget = true;
        Stretch(_panel);

        NewText(_panel, "Title", "肉鸽 2D", 104, new Vector2(0f, 424f), Accent, 900f);
        NewText(_panel, "Subtitle", "选一个职业，然后下去", 34, new Vector2(0f, 344f),
                new Color(0.72f, 0.76f, 0.84f, 1f), 900f);

        _cardsRoot = NewRect(_panel, "Cards", new Vector2(1920f, CardHeight), new Vector2(0f, 0f));

        // 开始按钮：底图一个物体、文字一个子物体。
        // 不能把 Image 和 Text 挂在同一个物体上 —— 两个 Graphic 互斥，AddComponent 会直接抛异常。
        RectTransform startRect = NewRect(_panel, "Start", new Vector2(620f, 96f), new Vector2(0f, -430f));
        var startBg = startRect.gameObject.AddComponent<Image>();
        startBg.sprite = WhiteSprite;
        startBg.color = Accent;

        _startLabel = NewText(startRect, "Label", "开始游戏", 40, Vector2.zero, new Color(0.06f, 0.06f, 0.08f, 1f), 600f);
        _startLabel.alignment = TextAnchor.MiddleCenter;

        var startButton = startRect.gameObject.AddComponent<Button>();
        startButton.transition = Selectable.Transition.None;
        startButton.onClick.AddListener(StartGame);

        RectTransform quitRect = NewRect(_panel, "Quit", new Vector2(220f, 64f), new Vector2(820f, -500f));
        var quitBg = quitRect.gameObject.AddComponent<Image>();
        quitBg.sprite = WhiteSprite;
        quitBg.color = new Color(0.22f, 0.23f, 0.28f, 1f);
        var quitLabel = NewText(quitRect, "Label", "退出游戏", 26, Vector2.zero, new Color(0.85f, 0.87f, 0.92f, 1f), 200f);
        quitLabel.alignment = TextAnchor.MiddleCenter;
        var quitButton = quitRect.gameObject.AddComponent<Button>();
        quitButton.transition = Selectable.Transition.None;
        quitButton.onClick.AddListener(Quit);

        _hint = NewText(_panel, "Hint", "A / D 或 ← → 换职业　·　Enter 开始", 24, new Vector2(-620f, -498f),
                        new Color(0.6f, 0.65f, 0.72f, 1f), 900f);
        _hint.alignment = TextAnchor.MiddleLeft;

        NewText(_panel, "Version", "团结引擎 2022.3.62t16 · Tuanjie Editor 1.10.4", 20, new Vector2(0f, -532f),
                new Color(0.42f, 0.46f, 0.53f, 1f), 900f);
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------- 小工具 ----------------

    private static RectTransform NewRect(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }

    private static Text NewText(RectTransform parent, string name, string content, int size, Vector2 position,
                                Color color, float width, float height = 60f)
    {
        RectTransform rect = NewRect(parent, name, new Vector2(width, height), position);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = UiFont;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Sprite _whiteSprite;
    private static Sprite WhiteSprite
    {
        get
        {
            if (_whiteSprite != null) return _whiteSprite;

            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "LobbyWhite" };
            var pixels = new Color32[8 * 8];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            _whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 8f);
            _whiteSprite.name = "LobbyWhite";
            return _whiteSprite;
        }
    }

    private static Font _font;
    private static Font UiFont
    {
        get
        {
            if (_font != null) return _font;

            // 中文字形只在系统字体里有：Unity 内置的 Arial/LegacyRuntime 不含汉字
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "PingFang SC", "Arial" }, 32);

            if (_font == null)
            {
                Debug.LogWarning("[LobbyUI] 没找到系统字体，中文会变方块（回退到 LegacyRuntime.ttf）");
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return _font;
        }
    }
}
