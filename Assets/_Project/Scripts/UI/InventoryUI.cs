using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 背包与装备界面。按 Tab 打开/关闭。
///
/// 为什么又是「运行时用代码搭界面」：
///   1. 中文字形必须问系统要动态字体，而动态字体不是资产，进不了场景序列化；
///   2. 格子数量由 Inventory.capacity 决定，手搭就得每次改容量都重新摆一遍。
/// 这两条和词条面板（RelicChoiceUI）完全一样，所以沿用同一套搭法。
///
/// 点击规则只有两条：
///   · 点背包里的物品  → 穿上（同槽位的旧装备自动回背包）
///   · 点左边的装备槽  → 卸下（放回背包）
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("引用（留空自动找玩家身上的组件）")]
    [SerializeField] private Inventory  inventory;
    [SerializeField] private Equipment  equipment;
    [SerializeField] private PlayerInputReader inputReader;

    [Header("打开方式")]
    [Tooltip("打开背包的按键。默认 Tab")]
    [SerializeField] private Key toggleKey = Key.Tab;

    [Tooltip("打开时暂停游戏。关掉它背包就是「实时」的，怪会继续打你")]
    [SerializeField] private bool pauseWhileOpen = true;

    // ---------------- 布局常量（1920×1080 参考分辨率下的像素） ----------------
    private const float WindowWidth  = 1100f;
    private const float WindowHeight = 640f;
    private const float CellSize     = 104f;
    private const float CellStep     = 118f;
    private const int   Columns      = 4;
    private const float GridLeft     = -60f;   // 第一列格子的中心 x（相对窗口中心）
    private const float GridTop      = 150f;   // 第一行格子的中心 y
    private const float EquipColumnX = -380f;
    private const float EquipCell    = 100f;
    private const float TooltipWidth  = 380f;
    private const float TooltipHeight = 250f;

    private static readonly Color WindowBg     = new Color(0.10f, 0.11f, 0.14f, 0.99f);
    private static readonly Color WindowBorder = new Color(0.30f, 0.33f, 0.40f, 1f);
    private static readonly Color CellBg       = new Color(0.16f, 0.17f, 0.21f, 1f);
    private static readonly Color CellHover    = new Color(0.26f, 0.29f, 0.36f, 1f);
    private static readonly Color EquipCellBg  = new Color(0.13f, 0.15f, 0.19f, 1f);
    private static readonly Color MutedText    = new Color(0.62f, 0.65f, 0.72f);

    private RectTransform _panel;
    private RectTransform _window;
    private RectTransform _tooltip;
    private Text _tooltipText;
    private Text _hint;

    private ItemSlotWidget[] _inventoryCells;
    private ItemSlotWidget[] _equipCells;

    private bool _visible;
    private bool _dirty;
    private ItemSlotWidget _hovered;

    public bool IsOpen => _visible;

    // ---------------- 生命周期 ----------------

    private void Awake()
    {
        if (inventory == null)   inventory   = FindObjectOfType<Inventory>();
        if (equipment == null)   equipment   = FindObjectOfType<Equipment>();
        if (inputReader == null) inputReader = FindObjectOfType<PlayerInputReader>();

        Build();
        SetVisible(false);
    }

    private void Start()
    {
        if (inventory != null) inventory.Changed += MarkDirty;
        if (equipment != null) equipment.Changed += MarkDirty;

        Refresh();
    }

    private void OnDestroy()
    {
        if (inventory != null) inventory.Changed -= MarkDirty;
        if (equipment != null) equipment.Changed -= MarkDirty;
    }

    private void MarkDirty() => _dirty = true;

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb[toggleKey].wasPressedThisFrame)
        {
            if (_visible) Hide();
            // 别的面板（词条三选一 / 暂停菜单）已经冻结了时间时，不允许再叠一层
            else if (!TimeFreeze.IsFrozen) Show();
            return;
        }

        if (!_visible) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            Hide();
            return;
        }

        if (_dirty)
        {
            Refresh();
            _dirty = false;
        }
    }

    // ---------------- 开关 ----------------

    public void Show()
    {
        if (_visible) return;
        if (inventory == null || equipment == null)
        {
            Debug.LogWarning("[Inventory UI] 没找到背包或装备组件，界面打不开。", this);
            return;
        }

        SetVisible(true);
        Refresh();

        if (pauseWhileOpen) TimeFreeze.Freeze(this);
        if (inputReader != null) inputReader.enabled = false;   // 开着背包时人物不该还能动
    }

    public void Hide()
    {
        if (!_visible) return;

        SetVisible(false);
        HandleHoverEnd();

        if (pauseWhileOpen) TimeFreeze.Unfreeze(this);
        if (inputReader != null) inputReader.enabled = true;
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        if (_panel != null) _panel.gameObject.SetActive(visible);
    }

    // ---------------- 交互 ----------------

    public void HandleClick(ItemSlotWidget widget)
    {
        if (widget == null || equipment == null) return;

        if (widget.isEquipSlot)
        {
            equipment.Unequip(widget.slot);
        }
        else
        {
            if (widget.item == null) return;

            // 点到的如果是武器，还会顺带换掉普通攻击形态（法杖 → 投射物）
            equipment.Equip(widget.item);
        }

        Refresh();
    }

    public void HandleHover(ItemSlotWidget widget, Vector2 screenPosition)
    {
        if (widget == null) return;

        if (_hovered != null && _hovered != widget) ApplyCellLook(_hovered);
        _hovered = widget;
        if (widget.item != null) widget.background.color = CellHover;

        ShowTooltip(widget, screenPosition);
    }

    public void HandleHoverEnd()
    {
        if (_hovered != null)
        {
            ApplyCellLook(_hovered);
            _hovered = null;
        }

        if (_tooltip != null) _tooltip.gameObject.SetActive(false);
    }

    // ---------------- 刷新 ----------------

    private void Refresh()
    {
        if (_inventoryCells == null || _equipCells == null) return;

        // 背包格子：前 Count 个有东西，后面都是空的
        int count = inventory != null ? inventory.Count : 0;
        for (int i = 0; i < _inventoryCells.Length; i++)
        {
            ItemData item = (i < count) ? inventory.Items[i] : null;
            SetCell(_inventoryCells[i], item, false);
        }

        // 装备槽：固定三个
        if (equipment != null)
        {
            for (int i = 0; i < _equipCells.Length; i++)
            {
                EquipSlot slot = EquipSlotNames.All[i];
                SetCell(_equipCells[i], equipment.Get(slot), true);
            }
        }

        if (_hint != null)
        {
            int used = inventory != null ? inventory.Count : 0;
            int cap  = inventory != null ? inventory.Capacity : 0;
            _hint.text = $"Tab 关闭　·　点物品穿上　·　点已装备的格子卸下　·　背包 {used}/{cap}";
        }
    }

    private void SetCell(ItemSlotWidget widget, ItemData item, bool isEquip)
    {
        widget.item = item;
        widget.isEquipSlot = isEquip;

        if (item == null)
        {
            widget.icon.enabled = false;
            widget.nameLabel.text = isEquip ? EquipSlotNames.Of(widget.slot) : "";
            widget.nameLabel.color = MutedText;
        }
        else
        {
            widget.icon.enabled = true;
            widget.icon.sprite = item.icon != null ? item.icon : WhiteSprite;
            widget.icon.color  = item.icon != null ? Color.white : item.RarityColor;

            widget.nameLabel.text = item.displayName;
            widget.nameLabel.color = item.RarityColor;
        }

        ApplyCellLook(widget, isEquip);
    }

    private void ApplyCellLook(ItemSlotWidget widget, bool? isEquipOverride = null)
    {
        bool isEquip = isEquipOverride ?? widget.isEquipSlot;

        Color bg = isEquip ? EquipCellBg : CellBg;
        if (widget.item == null) bg.a = 0.75f;

        widget.background.color = bg;
        if (widget.icon != null)
        {
            Color c = widget.icon.color;
            c.a = widget.item != null ? 1f : 0f;
            widget.icon.color = c;
        }
    }

    // ---------------- 提示框 ----------------

    private void ShowTooltip(ItemSlotWidget widget, Vector2 screenPosition)
    {
        if (_tooltip == null || _tooltipText == null) return;

        if (widget.item == null)
        {
            if (!widget.isEquipSlot)
            {
                _tooltip.gameObject.SetActive(false);
                return;
            }

            _tooltipText.text = $"<b>{EquipSlotNames.Of(widget.slot)}</b>\n<color=#8b90a0>空着</color>";
        }
        else
        {
            _tooltipText.text = BuildTooltip(widget.item, widget.isEquipSlot);
        }

        _tooltip.gameObject.SetActive(true);
        PlaceTooltip(screenPosition);
    }

    private static string BuildTooltip(ItemData item, bool isEquipSlot)
    {
        var sb = new StringBuilder();

        sb.Append("<b><color=#")
          .Append(ColorUtility.ToHtmlStringRGB(item.RarityColor))
          .Append('>')
          .Append(item.displayName)
          .Append("</color></b>\n");

        sb.Append("<color=#8b90a0>")
          .Append(item.RarityLabel).Append(" · ").Append(item.SlotLabel)
          .Append("</color>\n\n");

        if (!string.IsNullOrEmpty(item.description))
            sb.Append(item.description).Append("\n\n");

        sb.Append(item.DescribeModifiers());
        sb.Append("\n\n<color=#7f8697>").Append(isEquipSlot ? "点击卸下" : "点击穿上").Append("</color>");

        return sb.ToString();
    }

    private void PlaceTooltip(Vector2 screenPosition)
    {
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_panel, screenPosition, null, out local))
            return;

        Rect area = _panel.rect;

        float x = local.x + 26f;
        if (x + TooltipWidth > area.xMax) x = local.x - TooltipWidth - 26f;
        x = Mathf.Clamp(x, area.xMin, area.xMax - TooltipWidth);

        float y = Mathf.Clamp(local.y - 10f, area.yMin + TooltipHeight, area.yMax);

        _tooltip.anchoredPosition = new Vector2(x, y);
    }

    // ---------------- 搭界面 ----------------

    private void Build()
    {
        // 和 RelicChoiceUI 同一个坑：new GameObject 出来的是普通 Transform，绝不能强转
        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 320;   // 压过 HUD(0)，但让词条面板(300/400)能盖在上面

        CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // 全屏暗幕：既压暗背景，也接住点击不让它穿到游戏里
        _panel = NewRect("Panel", transform);
        Stretch(_panel);
        Image backdrop = NewImage(_panel, new Color(0f, 0f, 0f, 0.80f));
        backdrop.raycastTarget = true;

        // 主窗口
        _window = NewRect("Window", _panel);
        Center(_window, 0f, 0f, WindowWidth, WindowHeight);
        Image border = NewImage(_window, WindowBorder);
        border.raycastTarget = true;

        RectTransform inner = NewRect("Bg", _window);
        Stretch(inner, 3f);
        NewImage(inner, WindowBg);

        // 标题
        RectTransform titleRect = NewRect("Title", _window);
        Center(titleRect, 0f, WindowHeight * 0.5f - 52f, 900f, 60f);
        NewText(titleRect, "背包与装备", 38, new Color(0.96f, 0.96f, 0.98f), TextAnchor.MiddleCenter);

        // 左：三个装备槽
        RectTransform equipTitle = NewRect("EquipTitle", _window);
        Center(equipTitle, EquipColumnX, 230f, 240f, 40f);
        NewText(equipTitle, "装备", 26, new Color(0.82f, 0.84f, 0.90f), TextAnchor.MiddleCenter);

        _equipCells = new ItemSlotWidget[EquipSlotNames.All.Length];
        for (int i = 0; i < _equipCells.Length; i++)
        {
            float y = 150f - i * 130f;
            RectTransform rect = NewRect("EquipSlot_" + i, _window);
            Center(rect, EquipColumnX, y, EquipCell, EquipCell);

            _equipCells[i] = MakeCell(rect, true);
            _equipCells[i].slot = EquipSlotNames.All[i];
        }

        // 右：背包格子
        RectTransform bagTitle = NewRect("BagTitle", _window);
        Center(bagTitle, 168f, 230f, 400f, 40f);
        NewText(bagTitle, "背包", 26, new Color(0.82f, 0.84f, 0.90f), TextAnchor.MiddleCenter);

        int capacity = inventory != null ? inventory.Capacity : 12;
        int rows     = Mathf.CeilToInt(capacity / (float)Columns);
        _inventoryCells = new ItemSlotWidget[capacity];

        for (int i = 0; i < capacity; i++)
        {
            int col = i % Columns;
            int row = i / Columns;

            float x = GridLeft + col * CellStep;
            float y = GridTop - row * CellStep;

            RectTransform rect = NewRect("BagSlot_" + i, _window);
            Center(rect, x, y, CellSize, CellSize);

            _inventoryCells[i] = MakeCell(rect, false);
        }

        if (rows == 0)
            Debug.LogWarning("[Inventory UI] 背包容量是 0，一个格子都没建。", this);

        // 底部提示
        RectTransform hintRect = NewRect("Hint", _window);
        Center(hintRect, 0f, -WindowHeight * 0.5f + 34f, WindowWidth - 80f, 34f);
        _hint = NewText(hintRect, "", 21, MutedText, TextAnchor.MiddleCenter);

        // 提示框（最后建 → 画在最上层）
        _tooltip = NewRect("Tooltip", _panel);
        _tooltip.anchorMin = new Vector2(0.5f, 0.5f);
        _tooltip.anchorMax = new Vector2(0.5f, 0.5f);
        _tooltip.pivot = new Vector2(0f, 1f);
        _tooltip.sizeDelta = new Vector2(TooltipWidth, TooltipHeight);

        Image tipBg = NewImage(_tooltip, new Color(0.07f, 0.08f, 0.11f, 0.98f));
        tipBg.raycastTarget = false;

        RectTransform tipBorder = NewRect("Border", _tooltip);
        Stretch(tipBorder, 0f);
        NewImage(tipBorder, new Color(0.42f, 0.46f, 0.56f, 1f));

        RectTransform tipInner = NewRect("Inner", _tooltip);
        Stretch(tipInner, 2f);
        NewImage(tipInner, new Color(0.07f, 0.08f, 0.11f, 1f));

        RectTransform tipTextRect = NewRect("Text", _tooltip);
        Stretch(tipTextRect, 16f);
        _tooltipText = NewText(tipTextRect, "", 21, new Color(0.88f, 0.90f, 0.94f), TextAnchor.UpperLeft);
        _tooltipText.supportRichText = true;
        _tooltipText.lineSpacing = 1.15f;

        _tooltip.gameObject.SetActive(false);
    }

    private ItemSlotWidget MakeCell(RectTransform rect, bool isEquip)
    {
        ItemSlotWidget widget = rect.gameObject.AddComponent<ItemSlotWidget>();
        widget.owner = this;
        widget.isEquipSlot = isEquip;

        widget.background = rect.gameObject.AddComponent<Image>();
        widget.background.sprite = WhiteSprite;
        widget.background.type = Image.Type.Simple;
        widget.background.color = isEquip ? EquipCellBg : CellBg;
        widget.background.raycastTarget = true;   // 格子必须能接鼠标

        RectTransform iconRect = NewRect("Icon", rect);
        Center(iconRect, 0f, 8f, 70f, 70f);
        widget.icon = NewImage(iconRect, Color.white);
        widget.icon.preserveAspect = true;

        RectTransform nameRect = NewRect("Name", rect);
        nameRect.anchorMin = new Vector2(0.5f, 0f);
        nameRect.anchorMax = new Vector2(0.5f, 0f);
        nameRect.pivot = new Vector2(0.5f, 0f);
        nameRect.sizeDelta = new Vector2(isEquip ? 240f : 96f, 26f);
        nameRect.anchoredPosition = new Vector2(0f, isEquip ? -30f : 4f);

        widget.nameLabel = NewText(nameRect, "", 17, MutedText, TextAnchor.MiddleCenter);
        widget.nameLabel.horizontalOverflow = isEquip ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;

        return widget;
    }

    // ---------------- 小工具（与 RelicChoiceUI 保持一致的写法） ----------------

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

    private static void Center(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    private static Sprite _whiteSprite;

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
            _whiteSprite.name = "InventoryUI_White";
            return _whiteSprite;
        }
    }

    private static Font _uiFont;

    private static Font UiFont
    {
        get
        {
            if (_uiFont != null) return _uiFont;

            _uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "PingFang SC", "Arial" }, 32);

            if (_uiFont == null)
            {
                Debug.LogWarning("[Inventory UI] 系统里没找到中文字体，退回 Unity 内置字体（中文会显示成方块）。");
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return _uiFont;
        }
    }
}
