using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 背包界面。按 Tab 打开/关闭。
///
/// 布局（参考 1920×1080，窗口 1180×720）：
///   · 左上：人物卡 —— 职业立绘 + 三个装备槽
///   · 左下：属性卡 —— 生命、攻击、防御……一列数字，穿脱装备立刻变
///   · 右侧：分类标签（全部 / 武器 / 护甲 / 饰品）+ 背包格子
///
/// 为什么还是「运行时用代码搭界面」：
///   1. 中文字形必须问系统要动态字体，而动态字体不是资产，进不了场景序列化；
///   2. 格子数量由 Inventory.capacity 决定，手搭就得每次改容量都重新摆一遍。
/// 这两条和词条面板（RelicChoiceUI）完全一样，所以沿用同一套搭法。
///
/// 点击规则只有三条：
///   · 点背包里的物品  → 穿上（同槽位的旧装备自动回背包）
///   · 点装备槽        → 卸下（放回背包）
///   · 点分类标签      → 只显示那一类东西
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("引用（留空自动找玩家身上的组件）")]
    [SerializeField] private Inventory      inventory;
    [SerializeField] private Equipment      equipment;
    [SerializeField] private PlayerInputReader inputReader;
    [SerializeField] private CharacterStats stats;
    [SerializeField] private Health         health;

    [Header("打开方式")]
    [Tooltip("打开背包的按键。默认 Tab")]
    [SerializeField] private Key toggleKey = Key.Tab;

    [Header("美术（留空就退回纯色块，不会白屏）")]
    [Tooltip("九宫格窗口图：主窗口、提示框用")]
    [SerializeField] private Sprite panelSprite;

    [Tooltip("九宫格格子图：物品格、装备槽、标签页用")]
    [SerializeField] private Sprite slotSprite;

    [Tooltip("打开时暂停游戏。关掉它背包就是「实时」的，怪会继续打你")]
    [SerializeField] private bool pauseWhileOpen = true;

    // ---------------- 布局常量（1920×1080 参考分辨率下的像素） ----------------
    // 整块放大了一档（1180×720 → 1320×800）：1920 宽下左右还各留 300，正好「占满但不顶边」
    private const float WindowWidth  = 1320f;
    private const float WindowHeight = 800f;

    private const float LeftColumnX  = -393f;   // 左侧两块的中心 x（离窗口左边留 62）
    private const float DividerX     = -165f;   // 左右两栏之间的分割线
    private const float CardWidth    = 410f;
    private const float CharCardY    = 160f;    // 上沿 310、下沿 10（标题下沿 318，留 8）
    private const float CharCardH    = 300f;
    private const float StatCardY    = -180f;   // 上沿 -15、下沿 -345：和人物卡之间留 30，底部离窗口边 55
    private const float StatCardH    = 330f;

    private const float EquipCell    = 88f;
    private const float EquipGap     = 5f;
    private const float EquipX       = 118f;    // 装备槽在人物卡里的横向位置

    private const int   Columns      = 6;
    private const int   MaxRows      = 4;
    private const float CellSize     = 104f;
    private const float CellStep     = 120f;
    private const float GridCenterX  = 240f;    // 右侧区域中心
    private const float GridTopY     = 210f;    // 首行中心：上沿 262，刚好在标签页下沿 276 之下

    private const float TabWidth     = 176f;
    private const float TabHeight    = 48f;
    private const float TabY         = 300f;
    private const float TabStep      = 186f;
    private const float TabFirstX    = -60f;

    private const float TooltipWidth  = 400f;
    private const float TooltipHeight = 260f;

    private const float MenuWidth  = 290f;
    private const float MenuHeight = 262f;
    private const float MenuButtonH = 50f;
    private const float MenuButtonStep = 58f;

    private static readonly Color WindowBg     = new Color(0.10f, 0.11f, 0.14f, 0.99f);
    private static readonly Color WindowBorder = new Color(0.30f, 0.33f, 0.40f, 1f);
    private static readonly Color CardBg       = new Color(0.13f, 0.15f, 0.19f, 1f);
    private static readonly Color CellBg       = new Color(0.16f, 0.17f, 0.21f, 1f);
    private static readonly Color CellHover    = new Color(0.26f, 0.29f, 0.36f, 1f);
    private static readonly Color EquipCellBg  = new Color(0.17f, 0.18f, 0.23f, 1f);   // 要亮过角色卡(0.13)才看得出来
    private static readonly Color TabBg        = new Color(0.15f, 0.17f, 0.22f, 1f);
    private static readonly Color TabSelected  = new Color(0.62f, 0.47f, 0.20f, 1f);
    private static readonly Color Accent       = new Color(0.98f, 0.80f, 0.40f, 1f);
    private static readonly Color TextMain     = new Color(0.92f, 0.93f, 0.96f);
    private static readonly Color MutedText    = new Color(0.74f, 0.77f, 0.84f);

    // 左键菜单：三种按钮各一个底色，靠颜色区分「主要动作 / 换钱 / 退出」，
    // 比三个一模一样的灰块好认得多
    private static readonly Color MenuButtonBg  = new Color(0.19f, 0.21f, 0.26f, 1f);
    private static readonly Color MenuPrimaryBg = new Color(0.17f, 0.27f, 0.21f, 1f);   // 装备 / 卸下 / 服用
    private static readonly Color MenuSellBg    = new Color(0.29f, 0.23f, 0.12f, 1f);   // 出售
    private static readonly Color MenuCancelBg  = new Color(0.15f, 0.16f, 0.20f, 1f);   // 取消
    private static readonly Color GoldText      = new Color(1f, 0.82f, 0.40f, 1f);

    private static readonly string[] CategoryNames = { "全部", "武器", "护甲", "饰品" };

    private RectTransform _panel;
    private RectTransform _window;
    private RectTransform _tooltip;
    private Text _tooltipText;
    private Text _hint;
    private Text _emptyLabel;

    private Image _portrait;
    private Text  _className;
    private Text[] _statValues;

    private ItemSlotWidget[] _inventoryCells;
    private ItemSlotWidget[] _equipCells;
    private TabButtonWidget[] _tabs;

    private readonly List<ItemData> _shown = new();

    private int  _category;          // 0 = 全部，1..3 = EquipSlot
    private bool _visible;
    private bool _dirty;
    private ItemSlotWidget _hovered;
    private TabButtonWidget _hoveredTab;

    private RectTransform _menu;
    private Text _menuTitle;
    private Text _menuSub;
    private SmallButton[] _menuButtons;
    private ItemSlotWidget _menuTarget;

    public bool IsOpen => _visible;

    // ---------------- 生命周期 ----------------

    private void Awake()
    {
        ResolveReferences();
        Build();
        SetVisible(false);
    }

    private void ResolveReferences()
    {
        if (inventory == null)   inventory   = FindObjectOfType<Inventory>();
        if (equipment == null)   equipment   = FindObjectOfType<Equipment>();
        if (inputReader == null) inputReader = FindObjectOfType<PlayerInputReader>();

        // 属性和血量都从背包所在的玩家身上拿 —— 场景里还有敌人也有 Health，
        // 用 FindObjectOfType<Health>() 会随加载顺序拿到怪的
        if (stats == null && inventory != null) stats = inventory.GetComponent<CharacterStats>();
        if (health == null && inventory != null) health = inventory.GetComponent<Health>();
    }

    private void Start()
    {
        if (inventory != null)
        {
            inventory.Changed += MarkDirty;
            inventory.GoldChanged += MarkDirty;
        }

        if (equipment != null) equipment.Changed += MarkDirty;
        if (stats != null)     stats.Changed     += MarkDirty;

        Refresh();
    }

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.Changed -= MarkDirty;
            inventory.GoldChanged -= MarkDirty;
        }

        if (equipment != null) equipment.Changed -= MarkDirty;
        if (stats != null)     stats.Changed     -= MarkDirty;
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

        ResolveReferences();
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

        // 菜单是窗口的子物体，关窗口时一起关；但 HideItemMenu 还要把 _menuTarget 清掉
        if (!visible) HideItemMenu();
    }

    // ---------------- 交互 ----------------

    public void HandleClick(ItemSlotWidget widget)
    {
        if (widget == null || equipment == null) return;

        // 左键不再「点了就穿」—— 先弹一张小菜单，让玩家自己选做什么（装备/服用/出售）
        ShowItemMenu(widget);
    }

    /// <summary>
    /// 物品菜单：一格能做的事不止一件，所以不直接执行，先问一句。
    /// 菜单只有两项可点（主操作 + 出售），加上「取消」共三行。
    /// </summary>
    private void ShowItemMenu(ItemSlotWidget widget)
    {
        _menuTarget = widget;
        if (widget.item == null && !widget.isEquipSlot)
        {
            HideItemMenu();
            return;
        }

        ItemData item = widget.item;

        if (item == null && widget.isEquipSlot)
        {
            // 空装备槽：没什么可做的
            HideItemMenu();
            return;
        }

        _menuTitle.text = item.displayName;
        _menuTitle.color = item.RarityColor;
        _menuSub.text = $"{item.RarityLabel} · {item.CategoryLabel}";

        // 提示框会正好盖在菜单上，先收起来
        if (_tooltip != null) _tooltip.gameObject.SetActive(false);

        if (widget.isEquipSlot)
        {
            SetMenuButton(0, "卸下", true);
            SetMenuButton(1, $"出售  +{item.SellPrice} 金币", true);
        }
        else if (item.IsConsumable)
        {
            SetMenuButton(0, item.healAmount > 0f ? $"服用  +{Mathf.RoundToInt(item.healAmount)} 生命" : "服用", true);
            SetMenuButton(1, $"出售  +{item.SellPrice} 金币", true);
        }
        else
        {
            SetMenuButton(0, "装备", true);
            SetMenuButton(1, $"出售  +{item.SellPrice} 金币", true);
        }

        // 摆在被点的那一格旁边：格子的屏幕位置换成窗口内坐标
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, widget.transform.position);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_window, screen, null, out Vector2 local))
        {
            // 菜单往右下展开；靠边时翻到另一侧，别跑出窗口
            float halfW = WindowWidth * 0.5f;
            float halfH = WindowHeight * 0.5f;
            float px = local.x + MenuWidth * 0.5f + 16f;
            float py = local.y - MenuHeight * 0.5f - 8f;

            if (px + MenuWidth * 0.5f > halfW - 8f) px = local.x - MenuWidth * 0.5f - 16f;
            if (py - MenuHeight * 0.5f < -halfH + 8f) py = local.y + MenuHeight * 0.5f + 8f;

            _menu.anchoredPosition = new Vector2(px, py);
        }

        _menu.gameObject.SetActive(true);
        _menu.SetAsLastSibling();
    }

    private void HideItemMenu()
    {
        _menuTarget = null;
        if (_menu != null) _menu.gameObject.SetActive(false);
    }

    private void SetMenuButton(int index, string label, bool enabled)
    {
        if (_menuButtons == null || index < 0 || index >= _menuButtons.Length) return;

        SmallButton button = _menuButtons[index];
        button.gameObject.SetActive(enabled);
        button.label.text = label;
    }

    public void HandleMenuHover(SmallButton button, bool inside)
    {
        if (button == null || button.background == null) return;

        bool primary = button.id == 0;
        Color baseColor = button.baseColor;
        button.background.color = inside ? Color.Lerp(baseColor, Color.white, primary ? 0.22f : 0.16f) : baseColor;
    }

    public void HandleMenuClick(SmallButton button)
    {
        if (button == null || _menuTarget == null) return;

        ItemSlotWidget widget = _menuTarget;

        switch (button.id)
        {
            case 0: PrimaryAction(widget); break;
            case 1: SellItem(widget); break;
            default: HideItemMenu(); return;
        }

        HideItemMenu();
        Refresh();
    }

    /// <summary>主操作：装备槽卸下、消耗品服用、装备穿身上</summary>
    private void PrimaryAction(ItemSlotWidget widget)
    {
        if (widget.isEquipSlot)
        {
            equipment.Unequip(widget.slot);
            return;
        }

        ItemData item = widget.item;
        if (item == null) return;

        if (item.IsConsumable)
        {
            UseConsumable(item);
            return;
        }

        // 点到的如果是武器，还会顺带换掉普通攻击形态（法杖 → 投射物）
        equipment.Equip(item);

        // 穿上后这件东西从背包里走了，当前分类下可能一件都不剩 —— 回到「全部」看一眼
        if (_category != 0 && CountInCategory(_category) == 0) SelectCategory(0);
    }

    private void UseConsumable(ItemData item)
    {
        if (inventory == null) return;

        // 飘字走的是 sortingOrder 260 的画布，会被背包窗口（320）盖住，所以这里不弹字：
        // 服下去的效果直接体现在左边「生命」那一行上，看数字就行
        if (health != null && item.healAmount > 0f) health.Heal(item.healAmount);

        inventory.Remove(item);
    }

    /// <summary>卖出：装备槽里的先卸下再卖，背包里的直接卖</summary>
    private void SellItem(ItemSlotWidget widget)
    {
        ItemData item = widget.item;
        if (item == null || inventory == null) return;

        if (widget.isEquipSlot)
        {
            equipment.Unequip(widget.slot);
            // Unequip 会把东西塞回背包，这里要再摘掉
            inventory.Remove(item);
        }
        else
        {
            if (!inventory.Remove(item)) return;
        }

        inventory.AddGold(item.SellPrice);

        if (_category != 0 && CountInCategory(_category) == 0) SelectCategory(0);
    }

    public void HandleHover(ItemSlotWidget widget, Vector2 screenPosition)
    {
        if (widget == null) return;

        if (_hovered != null && _hovered != widget) ApplyCellLook(_hovered);
        _hovered = widget;
        if (widget.item != null)
        {
            // 有美术图时不能盖一层纯色，否则边框被吃掉；改成「提亮」表达悬停
            widget.background.color = slotSprite != null
                ? new Color(1f, 0.92f, 0.66f, 1f)
                : CellHover;
        }

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

    // ---------------- 分类标签 ----------------

    public void SelectCategory(int index)
    {
        _category = Mathf.Clamp(index, 0, CategoryNames.Length - 1);
        RefreshTabs();
        RefreshGrid();
        RefreshHint();
    }

    public void HandleTabHover(TabButtonWidget tab, bool inside)
    {
        if (tab == null) return;

        if (inside)
        {
            if (_hoveredTab != null && _hoveredTab != tab) ApplyTabLook(_hoveredTab);
            _hoveredTab = tab;
            if (tab.background != null)
                tab.background.color = slotSprite != null
                    ? new Color(0.86f, 0.82f, 0.70f, 1f)
                    : Color.Lerp(TabBg, TabSelected, 0.45f);
        }
        else if (_hoveredTab == tab)
        {
            ApplyTabLook(tab);
            _hoveredTab = null;
        }
    }

    private void ApplyTabLook(TabButtonWidget tab)
    {
        if (tab == null || tab.background == null) return;

        bool selected = tab.index == _category;

        if (slotSprite != null)
        {
            // 有美术图时不换底色，只换色调：选中偏金、未选中压暗，边框的金边不会丢。
            // 字色必须保持浅色 —— 图中间是深蓝黑，深色字在金底上等于看不见
            tab.background.sprite = slotSprite;
            tab.background.type = slotSprite.border.sqrMagnitude > 0.01f ? Image.Type.Sliced : Image.Type.Simple;
            tab.background.color = selected ? new Color(0.92f, 0.78f, 0.46f, 1f) : new Color(0.58f, 0.62f, 0.70f, 1f);
            if (tab.label != null)
                tab.label.color = selected ? new Color(1f, 0.98f, 0.92f) : MutedText;
            return;
        }

        tab.background.color = selected ? TabSelected : TabBg;
        if (tab.label != null) tab.label.color = selected ? new Color(0.10f, 0.09f, 0.07f) : TextMain;
    }

    // ---------------- 刷新 ----------------

    private void Refresh()
    {
        if (_inventoryCells == null) return;

        RefreshCharacter();
        RefreshStats();
        RefreshTabs();
        RefreshGrid();
        RefreshHint();
    }

    private void RefreshCharacter()
    {
        ClassData data = stats != null ? stats.Class : null;

        if (_className != null)
            _className.text = data != null ? data.className : "未知职业";

        if (_portrait != null)
        {
            Sprite portrait = data != null ? data.icon : null;
            _portrait.enabled = portrait != null;
            _portrait.sprite  = portrait;
            _portrait.color   = Color.white;
        }
    }

    private void RefreshStats()
    {
        if (_statValues == null || _statValues.Length == 0) return;

        for (int i = 0; i < StatRows.Length; i++)
        {
            Text label = _statValues[i];
            if (label == null) continue;
            label.text = FormatStat(StatRows[i].Type);
        }
    }

    private string FormatStat(StatType type)
    {
        float v = stats != null ? stats.Get(type) : 0f;

        switch (type)
        {
            case StatType.MaxHealth:
            {
                float cur = health != null ? health.Current : v;
                return string.Format("<color=#e0574f>{0:0}</color><color=#7f8697> / {1:0}</color>", cur, v);
            }
            case StatType.Attack:         return v.ToString("0.#");
            case StatType.Defense:        return (v * 100f).ToString("0") + "%";
            case StatType.AttackSpeed:    return "×" + v.ToString("0.##");
            case StatType.CritChance:     return (v * 100f).ToString("0.#") + "%";
            case StatType.CritMultiplier: return "×" + v.ToString("0.##");
            case StatType.MoveSpeed:      return v.ToString("0.##");
            case StatType.MaxMana:        return v.ToString("0");
            case StatType.ManaRegen:      return v.ToString("0.#");
            case StatType.CooldownRate:   return (v * 100f).ToString("0") + "%";

            // 抗性：0 就显示「—」，别让四行都是「0%」看着像占位
            case StatType.KnockbackResist:
            case StatType.PoisonResist:
            case StatType.FireResist:
            case StatType.IceResist:
                return v <= 0.001f ? "—" : (v * 100f).ToString("0") + "%";

            default:                      return v.ToString("0.#");
        }
    }

    private void RefreshTabs()
    {
        if (_tabs == null) return;
        for (int i = 0; i < _tabs.Length; i++) ApplyTabLook(_tabs[i]);
    }

    private void RefreshGrid()
    {
        CollectShown();

        for (int i = 0; i < _inventoryCells.Length; i++)
            SetCell(_inventoryCells[i], i < _shown.Count ? _shown[i] : null, false);

        if (equipment != null && _equipCells != null)
        {
            for (int i = 0; i < _equipCells.Length; i++)
            {
                EquipSlot slot = EquipSlotNames.All[i];
                SetCell(_equipCells[i], equipment.Get(slot), true);
            }
        }

        if (_emptyLabel != null)
            _emptyLabel.gameObject.SetActive(_shown.Count == 0);
    }

    private void CollectShown()
    {
        _shown.Clear();
        if (inventory == null) return;

        IReadOnlyList<ItemData> items = inventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item == null) continue;
            if (_category != 0 && (int)item.slot != _category - 1) continue;
            _shown.Add(item);
        }
    }

    private int CountInCategory(int category)
    {
        if (inventory == null) return 0;
        if (category == 0) return inventory.Count;

        int n = 0;
        IReadOnlyList<ItemData> items = inventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item != null && (int)item.slot == category - 1) n++;
        }
        return n;
    }

    private void RefreshHint()
    {
        if (_hint == null) return;

        int used = inventory != null ? inventory.Count : 0;
        int cap  = inventory != null ? inventory.Capacity : 0;
        int gold = inventory != null ? inventory.Gold : 0;

        string filter = _category == 0 ? "" : $"　·　{CategoryNames[_category]} {_shown.Count} 件";
        _hint.text = $"Tab 关闭　·　左键物品选操作{filter}　·　<color=#ffd166>金币 {gold}</color>　·　背包 {used}/{cap}";
    }

    private void SetCell(ItemSlotWidget widget, ItemData item, bool isEquip)
    {
        widget.item = item;
        widget.isEquipSlot = isEquip;

        if (item == null)
        {
            widget.icon.enabled = false;
            if (widget.gem != null) widget.gem.enabled = false;
            widget.nameLabel.text = isEquip ? EquipSlotNames.Of(widget.slot) : "";
            widget.nameLabel.color = MutedText;
        }
        else if (item.icon != null)
        {
            widget.icon.enabled = true;
            widget.icon.sprite = item.icon;
            widget.icon.color  = Color.white;

            if (widget.gem != null) widget.gem.enabled = false;

            widget.nameLabel.text = item.displayName;
            widget.nameLabel.color = item.RarityColor;
        }
        else
        {
            // 没有图标：藏起大图标，用一颗稀有度颜色的小宝石 + 名字
            widget.icon.enabled = false;

            if (widget.gem != null)
            {
                widget.gem.enabled = true;
                widget.gem.color = Color.Lerp(item.RarityColor, Color.white, 0.25f);
            }

            widget.nameLabel.text = item.displayName;
            widget.nameLabel.color = item.RarityColor;
        }

        ApplyCellLook(widget, isEquip);
    }

    private void ApplyCellLook(ItemSlotWidget widget, bool? isEquipOverride = null)
    {
        bool isEquip = isEquipOverride ?? widget.isEquipSlot;

        if (slotSprite != null)
        {
            // 有美术图：底色交给图，只用「亮/暗」表达空不空；
            // 空槽压暗才不会让一排空框看着像已经装满了
            widget.background.sprite = slotSprite;
            widget.background.type = slotSprite.border.sqrMagnitude > 0.01f ? Image.Type.Sliced : Image.Type.Simple;

            Color art = isEquip ? new Color(1f, 0.96f, 0.86f, 1f) : Color.white;
            if (widget.item == null) art = new Color(art.r * 0.55f, art.g * 0.55f, art.b * 0.6f, 1f);
            widget.background.color = art;
        }
        else
        {
            Color bg = isEquip ? EquipCellBg : CellBg;
            if (widget.item == null) bg.a = 0.75f;
            widget.background.color = bg;
        }

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
            // 空槽不弹提示框：格子底下本来就写着「武器 / 护甲 / 饰品」，
            // 再弹一张 400×260 的大框，里面只有「空着」两个字，只是挡住旁边的物品。
            _tooltip.gameObject.SetActive(false);
            return;
        }

        _tooltipText.text = BuildTooltip(widget.item, widget.isEquipSlot);

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
          .Append(item.RarityLabel).Append(" · ").Append(item.CategoryLabel)
          .Append("</color>\n\n");

        if (!string.IsNullOrEmpty(item.description))
            sb.Append(item.description).Append("\n\n");

        if (item.IsConsumable)
        {
            if (item.healAmount > 0f)
                sb.Append("<color=#7fe08a>服用  +").Append(Mathf.RoundToInt(item.healAmount)).Append(" 生命</color>\n");
        }
        else
        {
            sb.Append(item.DescribeModifiers());
            if (!string.IsNullOrEmpty(item.DescribeModifiers())) sb.Append('\n');
        }

        sb.Append("<color=#ffd166>售价 ").Append(item.SellPrice).Append(" 金币</color>");
        sb.Append("\n<color=#7f8697>").Append(isEquipSlot ? "左键：卸下 / 出售" : "左键：选择操作").Append("</color>");

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
        // 按高度对齐而不是「宽高各半」：
        // match=0.5 时窗口宽高比一偏离 16:9，缩放比就落在一个歪的小数上，
        // 界面被等比缩到 0.7x、0.76x 这种数值，动态字体的点阵再一重采样，笔画就发虚。
        // 按高度对齐至少保证「1080 高时 1:1」，常见窗口下的缩放比也更整。
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // 全屏暗幕：既压暗背景，也接住点击不让它穿到游戏里
        _panel = NewRect("Panel", transform);
        Stretch(_panel);
        Image backdrop = NewImage(_panel, new Color(0f, 0f, 0f, 0.80f));
        backdrop.raycastTarget = true;

        // 主窗口：有九宫格美术就用美术，没有就退回「描边 + 深色底」
        _window = NewRect("Window", _panel);
        Center(_window, 0f, 0f, WindowWidth, WindowHeight);

        // 界面是「打开时按常量搭一次」的，改了常量必须重进 Play 才会重建。
        // 这行日志就是为了省掉「改了没反应」的来回猜测：看数字就知道跑的是哪一版。
        Debug.Log($"[Inventory UI] 搭好了：窗口 {WindowWidth:0}×{WindowHeight:0}，格子 {CellSize:0}，左栏 {CardWidth:0}", this);

        Image border = NewFramed(_window, panelSprite, Color.white, WindowBorder);
        border.raycastTarget = true;

        RectTransform inner = NewRect("Bg", _window);
        Stretch(inner, 3f);

        // 美术图自带深蓝黑的底，但角色卡/文字压在上面时要再暗一档才够对比。
        // 没有美术图时这就是唯一的一层底色，所以 alpha 不能太低。
        NewImage(inner, panelSprite != null ? new Color(0.04f, 0.05f, 0.07f, 0.35f) : WindowBg);

        // 标题
        RectTransform titleRect = NewRect("Title", _window);
        Center(titleRect, 0f, WindowHeight * 0.5f - 52f, 900f, 60f);
        Text titleText = NewText(titleRect, "角色 · 背包", 40, TextMain, TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyle.Bold;

        // 左右两栏的分割线：一条深色沟 + 一条浅色高光，看起来像块面被压进去
        RectTransform dividerDark = NewRect("DividerDark", _window);
        Center(dividerDark, DividerX, -26f, 3f, WindowHeight - 132f);
        NewImage(dividerDark, new Color(0f, 0f, 0f, 0.55f));

        RectTransform dividerLight = NewRect("DividerLight", _window);
        Center(dividerLight, DividerX + 2f, -26f, 1f, WindowHeight - 132f);
        NewImage(dividerLight, new Color(0.55f, 0.58f, 0.66f, 0.35f));

        BuildCharacterCard();
        BuildStatCard();
        BuildTabs();
        BuildGrid();
        BuildHint();
        BuildTooltip();
        BuildItemMenu();
    }

    private void BuildItemMenu()
    {
        float half = MenuHeight * 0.5f;

        _menu = NewRect("ItemMenu", _window);
        Center(_menu, 0f, 0f, MenuWidth, MenuHeight);
        Image bg = NewFramed(_menu, panelSprite, Color.white, new Color(0.10f, 0.11f, 0.14f, 0.98f));
        bg.raycastTarget = true;

        // 物品名（按稀有度上色）+ 一行「稀有度 · 槽位」
        RectTransform titleRect = NewRect("Title", _menu);
        Center(titleRect, 0f, half - 30f, MenuWidth - 28f, 34f);
        _menuTitle = NewText(titleRect, "", 24, TextMain, TextAnchor.MiddleCenter);
        _menuTitle.fontStyle = FontStyle.Bold;
        _menuTitle.horizontalOverflow = HorizontalWrapMode.Overflow;

        RectTransform subRect = NewRect("Subtitle", _menu);
        Center(subRect, 0f, half - 56f, MenuWidth - 28f, 22f);
        _menuSub = NewText(subRect, "", 17, MutedText, TextAnchor.MiddleCenter);
        _menuSub.horizontalOverflow = HorizontalWrapMode.Overflow;

        // 一条细分割线，把标题和按钮分开
        RectTransform line = NewRect("Divider", _menu);
        Center(line, 0f, half - 74f, MenuWidth - 40f, 1f);
        NewImage(line, new Color(0.55f, 0.58f, 0.66f, 0.35f));

        _menuButtons = new SmallButton[3];
        string[] labels = { "装备", "出售", "取消" };

        for (int i = 0; i < 3; i++)
        {
            RectTransform row = NewRect("Button" + i, _menu);
            Center(row, 0f, 14f - i * MenuButtonStep, MenuWidth - 28f, MenuButtonH);

            Color tint = i == 0 ? MenuPrimaryBg : i == 1 ? MenuSellBg : MenuCancelBg;
            Image buttonBg = NewFramed(row, slotSprite, tint, tint);
            buttonBg.raycastTarget = true;

            RectTransform labelRect = NewRect("Label", row);
            Stretch(labelRect, 6f);
            Text label = NewText(labelRect, labels[i], 21, i == 1 ? GoldText : TextMain, TextAnchor.MiddleCenter);

            SmallButton button = row.gameObject.AddComponent<SmallButton>();
            button.owner = this;
            button.id = i;
            button.background = buttonBg;
            button.label = label;
            button.baseColor = tint;
            _menuButtons[i] = button;
        }

        _menu.gameObject.SetActive(false);
    }

    private void BuildCharacterCard()
    {
        RectTransform card = NewRect("CharacterCard", _window);
        Center(card, LeftColumnX, CharCardY, CardWidth, CharCardH);
        NewFramed(card, slotSprite, Color.white, CardBg);

        RectTransform cardTitle = NewRect("Title", card);
        Center(cardTitle, -CardWidth * 0.5f + 80f, CharCardH * 0.5f - 28f, 140f, 32f);
        Text t = NewText(cardTitle, "角色", 26, Accent, TextAnchor.MiddleLeft);
        t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;

        // 立绘框（先画框，再画图，图盖住框的内边）
        RectTransform frame = NewRect("PortraitFrame", card);
        Center(frame, -100f, 8f, 168f, 196f);
        NewFramed(frame, slotSprite, new Color(1f, 0.96f, 0.86f, 1f), new Color(0.24f, 0.27f, 0.33f, 1f));

        RectTransform portraitRect = NewRect("Portrait", card);
        Center(portraitRect, -100f, 8f, 156f, 184f);
        _portrait = NewImage(portraitRect, Color.white);
        _portrait.preserveAspect = true;

        // 三个装备槽，竖着贴在立绘右边
        int slots = EquipSlotNames.All.Length;
        float slotStep = EquipCell + EquipGap;
        _equipCells = new ItemSlotWidget[slots];
        for (int i = 0; i < slots; i++)
        {
            float y = (slots - 1) * 0.5f * slotStep - i * slotStep;

            RectTransform rect = NewRect("EquipSlot_" + i, card);
            Center(rect, EquipX, y, EquipCell, EquipCell);

            _equipCells[i] = MakeCell(rect, true);
            _equipCells[i].slot = EquipSlotNames.All[i];
        }

        // 职业名（贴在卡片下沿，不能压到立绘）
        RectTransform nameRect = NewRect("ClassName", card);
        Center(nameRect, -100f, -CharCardH * 0.5f + 30f, 190f, 30f);
        _className = NewText(nameRect, "未知职业", 22, TextMain, TextAnchor.MiddleCenter);
        _className.horizontalOverflow = HorizontalWrapMode.Overflow;
    }

    private void BuildStatCard()
    {
        RectTransform card = NewRect("StatCard", _window);
        Center(card, LeftColumnX, StatCardY, CardWidth, StatCardH);
        NewFramed(card, slotSprite, Color.white, CardBg);

        RectTransform cardTitle = NewRect("Title", card);
        Center(cardTitle, 0f, StatCardH * 0.5f - 26f, CardWidth - 40f, 30f);
        Text t = NewText(cardTitle, "属性", 26, Accent, TextAnchor.MiddleLeft);
        t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;

        _statValues = new Text[StatRows.Length];

        // 十四条属性，一列排下来放不下（会顶到卡片下边框），所以拆成左右两列：
        // 左列 0~6、右列 7~13，每列 7 行
        const int perColumn = 7;
        const float rowStep = 30f;
        float firstY = StatCardH * 0.5f - 58f;
        float columnX = CardWidth * 0.25f;
        // 每列比「半张卡」再窄一点，两列中间就空出 2×内缩 的空档：
        // 左列数值右对齐到 -内缩，右列标签左对齐到 +内缩，中间不会只有几像素
        float rowWidth = CardWidth * 0.5f - 40f;

        for (int i = 0; i < StatRows.Length; i++)
        {
            int column = i / perColumn;
            int line = i % perColumn;

            float y = firstY - line * rowStep;
            float x = column == 0 ? -columnX : columnX;

            RectTransform row = NewRect("Stat_" + i, card);
            Center(row, x, y, rowWidth, rowStep);

            RectTransform labelRect = NewRect("Label", row);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(108f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            NewText(labelRect, StatRows[i].Name, 22, MutedText, TextAnchor.MiddleLeft);

            RectTransform valueRect = NewRect("Value", row);
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(88f, 0f);
            valueRect.anchoredPosition = Vector2.zero;

            _statValues[i] = NewText(valueRect, "", 22, TextMain, TextAnchor.MiddleRight);
            _statValues[i].supportRichText = true;
            _statValues[i].horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }

    private void BuildTabs()
    {
        _tabs = new TabButtonWidget[CategoryNames.Length];

        for (int i = 0; i < CategoryNames.Length; i++)
        {
            float x = TabFirstX + i * TabStep;

            RectTransform rect = NewRect("Tab_" + i, _window);
            Center(rect, x, TabY, TabWidth, TabHeight);

            TabButtonWidget tab = rect.gameObject.AddComponent<TabButtonWidget>();
            tab.owner = this;
            tab.index = i;

            tab.background = rect.gameObject.AddComponent<Image>();
            tab.background.sprite = WhiteSprite;
            tab.background.color = TabBg;
            tab.background.raycastTarget = true;

            RectTransform labelRect = NewRect("Label", rect);
            Stretch(labelRect);
            tab.label = NewText(labelRect, CategoryNames[i], 24, TextMain, TextAnchor.MiddleCenter);

            _tabs[i] = tab;
        }
    }

    private void BuildGrid()
    {
        int capacity = inventory != null ? inventory.Capacity : 12;
        int maxCells = Columns * MaxRows;

        if (capacity > maxCells)
        {
            Debug.LogWarning($"[Inventory UI] 背包容量 {capacity} 超过界面能放的 {maxCells} 格，多出来的看不到。", this);
            capacity = maxCells;
        }

        int rows = Mathf.CeilToInt(capacity / (float)Columns);
        float gridWidth = Columns * CellStep - (CellStep - CellSize);
        float firstX = GridCenterX - gridWidth * 0.5f + CellSize * 0.5f;
        float firstY = GridTopY;

        _inventoryCells = new ItemSlotWidget[capacity];

        for (int i = 0; i < capacity; i++)
        {
            int col = i % Columns;
            int row = i / Columns;

            RectTransform rect = NewRect("BagSlot_" + i, _window);
            Center(rect, firstX + col * CellStep, firstY - row * CellStep, CellSize, CellSize);

            _inventoryCells[i] = MakeCell(rect, false);
        }

        if (rows == 0)
            Debug.LogWarning("[Inventory UI] 背包容量是 0，一个格子都没建。", this);

        RectTransform emptyRect = NewRect("Empty", _window);
        Center(emptyRect, GridCenterX, GridTopY - 120f, 520f, 40f);
        _emptyLabel = NewText(emptyRect, "这个分类下没有东西", 24, MutedText, TextAnchor.MiddleCenter);
        _emptyLabel.gameObject.SetActive(false);
    }

    private void BuildHint()
    {
        // 只占右侧区域：整条铺满会从属性卡底下穿过去（和最后一行数字叠字）
        RectTransform hintRect = NewRect("Hint", _window);
        // 只占右侧一列（900 宽，1920 下 x −210…690），并且在网格下方、窗口下边框之上 ——
        // 贴到 +28 时字正好落在九宫格底边（边框约 35px 厚）上，被边框压在下面看不清，
        // 抬到 +64 才真正落在底色区域里
        Center(hintRect, GridCenterX, -WindowHeight * 0.5f + 64f, 900f, 32f);
        _hint = NewText(hintRect, "", 24, MutedText, TextAnchor.MiddleCenter);
    }

    private void BuildTooltip()
    {
        // 提示框最后建 → 画在最上层
        _tooltip = NewRect("Tooltip", _panel);
        _tooltip.anchorMin = new Vector2(0.5f, 0.5f);
        _tooltip.anchorMax = new Vector2(0.5f, 0.5f);
        _tooltip.pivot = new Vector2(0f, 1f);
        _tooltip.sizeDelta = new Vector2(TooltipWidth, TooltipHeight);

        Image tipBg = NewImage(_tooltip, new Color(0.07f, 0.08f, 0.11f, 0.98f));
        tipBg.raycastTarget = false;

        RectTransform tipBorder = NewRect("Border", _tooltip);
        Stretch(tipBorder, 0f);
        NewFramed(tipBorder, panelSprite, Color.white, new Color(0.42f, 0.46f, 0.56f, 1f));

        RectTransform tipInner = NewRect("Inner", _tooltip);
        Stretch(tipInner, 2f);
        // 有美术图时内层只压一点暗，让文字读得清；没图时它就是底
        NewImage(tipInner, panelSprite != null
            ? new Color(0.04f, 0.05f, 0.07f, 0.55f)
            : new Color(0.07f, 0.08f, 0.11f, 1f));

        RectTransform tipTextRect = NewRect("Text", _tooltip);
        Stretch(tipTextRect, 16f);
        _tooltipText = NewText(tipTextRect, "", 21, new Color(0.88f, 0.90f, 0.94f), TextAnchor.UpperLeft);
        _tooltipText.supportRichText = true;
        _tooltipText.lineSpacing = 1.15f;

        _tooltip.gameObject.SetActive(false);
    }

    /// <summary>
    /// 格子/标签的底：有九宫格图就用图（装备槽偏暖金一点，和背包格区分开），没有就用纯色块。
    /// </summary>
    private void ApplySlotLook(Image image, bool warm)
    {
        if (slotSprite != null)
        {
            image.sprite = slotSprite;
            image.type = slotSprite.border.sqrMagnitude > 0.01f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = warm ? new Color(1f, 0.96f, 0.86f, 1f) : Color.white;
        }
        else
        {
            image.sprite = WhiteSprite;
            image.type = Image.Type.Simple;
            image.color = warm ? EquipCellBg : CellBg;
        }
    }

    private ItemSlotWidget MakeCell(RectTransform rect, bool isEquip)
    {
        ItemSlotWidget widget = rect.gameObject.AddComponent<ItemSlotWidget>();
        widget.owner = this;
        widget.isEquipSlot = isEquip;

        widget.background = rect.gameObject.AddComponent<Image>();
        ApplySlotLook(widget.background, isEquip);
        widget.background.raycastTarget = true;   // 格子必须能接鼠标

        // 装备只有六个、名字靠槽位本身就能认，图标给大一点；
        // 背包格子里还要塞名字（大部分装备没有图标，只有色块），先给名字留位置：
        //   104 高 = 名字 46（贴底）+ 空隙 + 图标 40（贴顶）
        float iconSize = isEquip ? 44f : 40f;
        float iconY    = isEquip ? 20f : 24f;
        float nameH    = isEquip ? 22f : 46f;

        RectTransform iconRect = NewRect("Icon", rect);
        Center(iconRect, 0f, iconY, iconSize, iconSize);
        widget.icon = NewImage(iconRect, Color.white);
        widget.icon.preserveAspect = true;

        // 没有图标时退回来的「宝石」：稀有度颜色的小方块，压在图标上面同位置。
        // 比整块 36×36 的浅色方块好看：留出格子的深底，颜色只是点缀
        RectTransform gemRect = NewRect("Gem", rect);
        Center(gemRect, 0f, iconY, 26f, 26f);
        widget.gem = NewImage(gemRect, Color.white);
        widget.gem.raycastTarget = false;

        RectTransform nameRect = NewRect("Name", rect);
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 0f);
        nameRect.pivot = new Vector2(0.5f, 0f);
        nameRect.sizeDelta = new Vector2(-6f, nameH);
        nameRect.anchoredPosition = new Vector2(0f, isEquip ? 10f : 2f);

        widget.nameLabel = NewText(nameRect, "", isEquip ? 18 : 19, MutedText, TextAnchor.MiddleCenter);
        widget.nameLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        widget.nameLabel.verticalOverflow = VerticalWrapMode.Truncate;

        return widget;
    }

    // ---------------- 属性表 ----------------

    private struct StatRow
    {
        public string Name;
        public StatType Type;

        public StatRow(string name, StatType type)
        {
            Name = name;
            Type = type;
        }
    }

    private static readonly StatRow[] StatRows =
    {
        new StatRow("生命",      StatType.MaxHealth),
        new StatRow("攻击力",    StatType.Attack),
        new StatRow("防御",      StatType.Defense),
        new StatRow("攻击速度",  StatType.AttackSpeed),
        new StatRow("暴击率",    StatType.CritChance),
        new StatRow("暴击伤害",  StatType.CritMultiplier),
        new StatRow("移动速度",  StatType.MoveSpeed),
        new StatRow("最大法力",  StatType.MaxMana),
        new StatRow("每秒回蓝",  StatType.ManaRegen),
        new StatRow("冷却缩减",  StatType.CooldownRate),
        new StatRow("击退抗性",  StatType.KnockbackResist),
        new StatRow("毒抗",      StatType.PoisonResist),
        new StatRow("火抗",      StatType.FireResist),
        new StatRow("冰抗",      StatType.IceResist),
    };

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

    /// <summary>
    /// 有九宫格美术就按九宫格铺（边框不会被拉粗），没有就退回「纯色块当边框」。
    /// fallbackColor 是美术缺失时的颜色 —— 所以每个调用点都要给自己那一层想好一个颜色。
    /// </summary>
    private static Image NewFramed(RectTransform rect, Sprite sprite, Color tint, Color fallbackColor)
    {
        Image image = rect.gameObject.AddComponent<Image>();

        if (sprite != null)
        {
            image.sprite = sprite;
            // 有边框信息才用 Sliced；没有的话硬用 Sliced 会退化成 Simple，等于白写
            image.type = sprite.border.sqrMagnitude > 0.01f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = tint;
        }
        else
        {
            image.sprite = WhiteSprite;
            image.type = Image.Type.Simple;
            image.color = fallbackColor;
        }

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

        // 深色底上，细笔画的中文字不加描边就是糊成一片灰。
        // 描边太粗（1.5 以上）反而会把笔画糊在一起，所以只给 1 像素、透明度也收着点。
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

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

            // 顺序有讲究：雅黑/等线是「无衬线黑体」，小字号最清晰；
            // SimSun 是宋体，横细竖粗，20 号以下基本看不清，所以排在最后只当兜底
            _uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "PingFang SC", "SimSun", "Arial" }, 36);

            if (_uiFont == null)
            {
                Debug.LogWarning("[Inventory UI] 系统里没找到中文字体，退回 Unity 内置字体（中文会显示成方块）。");
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return _uiFont;
        }
    }
}
