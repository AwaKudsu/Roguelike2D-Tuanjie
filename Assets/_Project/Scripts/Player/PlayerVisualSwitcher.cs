using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 按职业切换玩家的表现层（战士 / 法师 / 射手）与装备的初始武器。
///
/// 为什么需要它：工程里 `WarriorVisual` / `MageVisual` / `ArcherVisual` 是三套互斥的动画组件，
/// 而玩家物体只有一个 —— 不有个东西统一开关它们，就会出现「几套都在每帧写 sprite」的抢帧。
///
/// 同时它也负责「换职业」这个操作本身：
///   · `CharacterStats.ApplyClass` 换数值；
///   · `PlayerAttacker` 会通过 ClassChanged 自动换成新职业的普通攻击；
///   · `SkillCaster` 会通过 ClassChanged 自动换成新职业的技能栏；
///   · 这里只做「关掉旧动画、打开新动画、换初始贴图」这三件表现层的事。
///
/// 加第三个职业时最容易漏的就是这里：多一个表现层组件，就必须多一条「什么时候开它」的规则，
/// 否则新职业会变成一个站着不动的空壳（或者两套动画一起抢同一张 sprite）。
/// </summary>
[DisallowMultipleComponent]
public class PlayerVisualSwitcher : MonoBehaviour
{
    /// <summary>当前职业用哪一套表现层。比一串 bool 更容易读，也不会出现「两个都为 true」的非法状态</summary>
    private enum Look
    {
        Warrior,
        Mage,
        Archer,
    }

    [SerializeField] private CharacterStats stats;
    [SerializeField] private WarriorVisual warrior;
    [SerializeField] private MageVisual mage;

    [Tooltip("射手表现层（ArcherVisual）。留空 = 这个工程里没有射手，选中射手也不会崩")]
    [SerializeField] private ArcherVisual archer;

    [SerializeField] private SpriteRenderer sprite;

    [Header("可切换的职业（按顺序轮换）")]
    [SerializeField] private List<ClassData> classes = new();

    [Header("职业 → 表现层的显式映射（可选）")]
    [Tooltip("留空则按职业名猜：名字里带「射」= 射手、带「法」= 法师，其余都是战士。\n" +
             "将来职业改名成不带这些字的时候，把它拖进来即可")]
    [SerializeField] private ClassData archerClass;

    [SerializeField] private int currentIndex;

    /// <summary>
    /// 大厅模式：所有关卡动画都停掉。
    /// 它们每帧都会 <c>Apply(ChooseFrame())</c> 往同一个 SpriteRenderer 上写图，
    /// 大厅里则由 <see cref="LobbyAvatar"/> 按朝向决定画哪张 —— 几套同时写就会抢帧。
    /// </summary>
    private bool _lobbyMode;

    private Look _look = Look.Warrior;

    /// <summary>当前在用的表现层是不是法师</summary>
    public bool IsMage => _look == Look.Mage;

    /// <summary>当前在用的表现层是不是射手</summary>
    public bool IsArcher => _look == Look.Archer;

    /// <summary>当前在用的表现层是不是战士</summary>
    public bool IsWarrior => _look == Look.Warrior;

    /// <summary>职业表（大厅要拿它摆出「选角色」的卡片）</summary>
    public IReadOnlyList<ClassData> Classes => classes;

    /// <summary>当前职业在表里的下标（大厅用来默认高亮玩家在用的那个）</summary>
    public int CurrentClassIndex => currentIndex;

    private void Awake()
    {
        if (stats  == null) stats  = GetComponent<CharacterStats>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (warrior == null) warrior = GetComponent<WarriorVisual>();
        if (mage    == null) mage    = GetComponent<MageVisual>();
        if (archer  == null) archer  = GetComponent<ArcherVisual>();

        // 先把所有表现层都关掉：被禁用的组件不会执行自己的 Start，
        // 所以不会出现「另外几套也往同一张 sprite 上写一帧」的抢帧。
        // 具体开哪一套，等 Start 里知道职业了再决定。
        if (warrior != null) warrior.enabled = false;
        if (mage    != null) mage.enabled    = false;
        if (archer  != null) archer.enabled  = false;
    }

    private void Start()
    {
        if (classes == null || classes.Count == 0)
        {
            // 没在 Inspector 里配职业表时，按场景里当前职业决定表现层
            ApplyForClass(stats != null ? stats.Class : null);
            return;
        }

        // 有职业表就从表里取一个：默认沿用场景里配好的那个，找不到就用第一项
        currentIndex = Mathf.Clamp(currentIndex, 0, classes.Count - 1);

        if (stats != null && stats.Class != null)
        {
            int found = classes.IndexOf(stats.Class);
            if (found >= 0) currentIndex = found;
        }

        SetClass(classes[currentIndex]);
    }

    /// <summary>战士 → 法师 → 射手 → 战士…（Esc 菜单的「切换职业」按钮调它）</summary>
    public void CycleClass()
    {
        if (classes == null || classes.Count == 0)
        {
            Debug.LogWarning("[PlayerVisualSwitcher] 职业表是空的，切不了职业", this);
            return;
        }

        currentIndex = (currentIndex + 1) % classes.Count;
        SetClass(classes[currentIndex]);
    }

    /// <summary>当前职业名，UI 显示用</summary>
    public string CurrentClassName
        => stats != null && stats.Class != null ? stats.Class.className : "未知";

    private void OnEnable()
    {
        if (stats != null) stats.ClassChanged += ApplyForClass;
    }

    private void OnDisable()
    {
        if (stats != null) stats.ClassChanged -= ApplyForClass;
    }

    /// <summary>换职业：数值、攻击、技能都由各自的 ClassChanged 订阅者跟上，这里只管表现</summary>
    public void SetClass(ClassData data)
    {
        if (data == null || stats == null) return;
        stats.ApplyClass(data);
    }

    /// <summary>
    /// 进出大厅时调用。进大厅把所有关卡动画都关掉（否则它们每帧写 sprite，会把大厅按朝向挑的图覆盖掉），
    /// 出发时再按当前职业打开对应的那一套。
    /// </summary>
    public void SetLobbyMode(bool lobbyMode)
    {
        _lobbyMode = lobbyMode;
        ApplyVisualEnable();
    }

    private void ApplyVisualEnable()
    {
        if (warrior != null) warrior.enabled = !_lobbyMode && _look == Look.Warrior;
        if (mage    != null) mage.enabled    = !_lobbyMode && _look == Look.Mage;
        if (archer  != null) archer.enabled  = !_lobbyMode && _look == Look.Archer;
    }

    /// <summary>
    /// 掐断所有表现层正在播的动作（换房间时由 RoomManager 调用）。
    /// 只管姿势，不管数值：血、蓝、冷却都是这一局要带着走的东西。
    /// </summary>
    public void CancelAction()
    {
        if (warrior != null) warrior.CancelAction();
        if (mage    != null) mage.CancelAction();
        if (archer  != null) archer.CancelAction();
    }

    /// <summary>
    /// 当前职业的「跑」帧序列，交给大厅的俯视走路动画用。
    /// 表现层被 SetLobbyMode 关掉了，但它们的帧数组还在，直接读出来即可。
    /// </summary>
    public Sprite[] LobbyRunFrames()
    {
        switch (_look)
        {
            case Look.Mage:   return mage   != null ? mage.DebugRunFrames()   : null;
            case Look.Archer: return archer != null ? archer.DebugRunFrames() : null;
            default:          return warrior != null ? warrior.DebugRunFrames() : null;
        }
    }

    /// <summary>当前职业的待机帧（大厅站着时用第 0 张）</summary>
    public Sprite[] LobbyIdleFrames()
    {
        switch (_look)
        {
            case Look.Mage:   return mage   != null ? mage.DebugIdleFrames()   : null;
            case Look.Archer: return archer != null ? archer.DebugIdleFrames() : null;
            default:          return warrior != null ? warrior.DebugIdleFrames() : null;
        }
    }

    /// <summary>
    /// 职业 → 表现层。能显式映射就按映射，否则按名字里的关键字猜。
    ///
    /// 为什么允许「猜」：ClassData 是纯数据资产，本身不记录自己要哪套动画；
    /// 而新建职业时最容易漏的就是「记得把它接到表现层」。猜一手至少不会让人变成空壳，
    /// 真猜错了把 archerClass / 名字改一下就行。
    /// </summary>
    private Look LookFor(ClassData data)
    {
        if (data == null) return Look.Warrior;

        if (archerClass != null && ReferenceEquals(data, archerClass)) return Look.Archer;

        string name = data.className ?? string.Empty;
        if (name.Contains("射")) return Look.Archer;
        if (name.Contains("法")) return Look.Mage;

        return Look.Warrior;
    }

    private void ApplyForClass(ClassData data)
    {
        if (data == null) return;

        _look = LookFor(data);

        // 只在这里写一次 enabled：要连着「是不是在大厅」一起判断（见 ApplyVisualEnable）
        ApplyVisualEnable();

        // 换职业立刻换一张待机图，否则会停在上一套动画的最后一帧直到下一次状态变化
        Sprite idleFrame = IdleFrameOf(_look);
        if (sprite != null && idleFrame != null) sprite.sprite = idleFrame;

        // 装备不在这里换 —— 由背包 / 初始物品决定，避免和装备管线打架
        Debug.Log($"[PlayerVisualSwitcher] 当前职业：{data.className}（{_look}表现）", this);
    }

    private Sprite IdleFrameOf(Look look)
    {
        Sprite[] frames = null;

        switch (look)
        {
            case Look.Mage:   frames = mage   != null ? mage.DebugIdleFrames()   : null; break;
            case Look.Archer: frames = archer != null ? archer.DebugIdleFrames() : null; break;
            default:          frames = warrior != null ? warrior.DebugIdleFrames() : null; break;
        }

        if (frames != null)
        {
            for (int i = 0; i < frames.Length; i++)
                if (frames[i] != null) return frames[i];
        }

        return null;
    }
}
