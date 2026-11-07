using UnityEngine;

/// <summary>
/// 按职业切换玩家的表现层（战士 / 法师）与装备的初始武器。
///
/// 为什么需要它：工程里 `WarriorVisual` 和 `MageVisual` 是两套互斥的动画组件，
/// 而玩家物体只有一个 —— 不有个东西统一开关它们，就会出现「两套都在每帧写 sprite」的抢帧。
///
/// 同时它也负责「换职业」这个操作本身：
///   · `CharacterStats.ApplyClass` 换数值；
///   · `PlayerAttacker` 会通过 ClassChanged 自动换成新职业的普通攻击；
///   · `SkillCaster` 会通过 ClassChanged 自动换成新职业的技能栏；
///   · 这里只做「关掉旧动画、打开新动画、换初始贴图」这三件表现层的事。
/// </summary>
[DisallowMultipleComponent]
public class PlayerVisualSwitcher : MonoBehaviour
{
    [SerializeField] private CharacterStats stats;
    [SerializeField] private WarriorVisual warrior;
    [SerializeField] private MageVisual mage;
    [SerializeField] private SpriteRenderer sprite;

    [Header("可切换的职业（按顺序轮换）")]
    [SerializeField] private System.Collections.Generic.List<ClassData> classes = new();

    [SerializeField] private int currentIndex;

    /// <summary>
    /// 大厅模式：两套关卡动画都停掉。
    /// 它们每帧都会 <c>Apply(ChooseFrame())</c> 往同一个 SpriteRenderer 上写图，
    /// 大厅里则由 <see cref="LobbyAvatar"/> 按朝向决定画哪张 —— 两套同时写就会抢帧。
    /// </summary>
    private bool _lobbyMode;

    /// <summary>当前在用的表现层是不是法师</summary>
    public bool IsMage { get; private set; }

    /// <summary>职业表（大厅要拿它摆出「选角色」的卡片）</summary>
    public System.Collections.Generic.IReadOnlyList<ClassData> Classes => classes;

    /// <summary>当前职业在表里的下标（大厅用来默认高亮玩家在用的那个）</summary>
    public int CurrentClassIndex => currentIndex;

    private void Awake()
    {
        if (stats  == null) stats  = GetComponent<CharacterStats>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (warrior == null) warrior = GetComponent<WarriorVisual>();
        if (mage == null) mage = GetComponent<MageVisual>();

        // 先把两套动画都关掉：被禁用的组件不会执行自己的 Start，
        // 所以不会出现「法师那套也往同一张 sprite 上写一帧」的抢帧。
        // 具体开哪一套，等 Start 里知道职业了再决定。
        if (warrior != null) warrior.enabled = false;
        if (mage    != null) mage.enabled    = false;
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

    /// <summary>战士 → 法师 → 战士…（Esc 菜单的「切换职业」按钮调它）</summary>
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
    /// 进出大厅时调用。进大厅把两套关卡动画都关掉（否则它们每帧写 sprite，会把大厅按朝向挑的图覆盖掉），
    /// 出发时再按当前职业打开对应的那一套。
    /// </summary>
    public void SetLobbyMode(bool lobbyMode)
    {
        _lobbyMode = lobbyMode;
        ApplyVisualEnable();
    }

    private void ApplyVisualEnable()
    {
        if (warrior != null) warrior.enabled = !_lobbyMode && !IsMage;
        if (mage != null) mage.enabled = !_lobbyMode && IsMage;
    }

    /// <summary>
    /// 掐断两套表现层正在播的动作（换房间时由 RoomManager 调用）。
    /// 只管姿势，不管数值：血、蓝、冷却都是这一局要带着走的东西。
    /// </summary>
    public void CancelAction()
    {
        if (warrior != null) warrior.CancelAction();
        if (mage != null) mage.CancelAction();
    }

    /// <summary>
    /// 当前职业的「跑」帧序列，交给大厅的俯视走路动画用。
    /// 两套表现层被 SetLobbyMode 关掉了，但它们的帧数组还在，直接读出来即可。
    /// </summary>
    public Sprite[] LobbyRunFrames()
    {
        if (IsMage) return mage != null ? mage.DebugRunFrames() : null;
        return warrior != null ? warrior.DebugRunFrames() : null;
    }

    /// <summary>当前职业的待机帧（大厅站着时用第 0 张）</summary>
    public Sprite[] LobbyIdleFrames()
    {
        if (IsMage) return mage != null ? mage.DebugIdleFrames() : null;
        return warrior != null ? warrior.DebugIdleFrames() : null;
    }

    private void ApplyForClass(ClassData data)
    {
        if (data == null) return;

        // 用职业名字判断：现在只有两个职业，加第三个时这里要改成「按 ClassData 查表」
        bool wantMage = data.className != null && data.className.Contains("法");

        IsMage = wantMage;

        // 只在这里写一次 enabled：要连着「是不是在大厅」一起判断（见 ApplyVisualEnable）
        ApplyVisualEnable();

        // 换职业立刻换一张待机图，否则会停在上一套动画的最后一帧直到下一次状态变化
        if (sprite != null)
        {
            if (wantMage && mage != null && mage.DebugIdleFrames().Length > 0)
                sprite.sprite = mage.DebugIdleFrames()[0];
            else if (!wantMage && warrior != null && warrior.DebugIdleFrames().Length > 0)
                sprite.sprite = warrior.DebugIdleFrames()[0];
        }

        // 法杖职业给个法杖武器？暂不自动换装备 —— 装备由背包/初始物品决定，避免和 Day 9 的管线打架
        Debug.Log($"[PlayerVisualSwitcher] 当前职业：{data.className}（{(wantMage ? "法师" : "战士")}表现）", this);
    }
}
