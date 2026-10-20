using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 词条系统的中枢：持有「词条池」，抽三选一，把选中的词条变成 CharacterStats 上的修饰符。
///
/// 它自己不算任何数值 —— 数值全部走 CharacterStats.Get() 这个唯一出口，
/// 所以一条词条会自动作用于攻击力、暴击、冷却、移速、法力、生命，战斗代码一行都不用改。
///
/// 触发时机订阅 RoomManager.RoomChanged：每进一间新房弹一次三选一。
/// 第一间房默认跳过 —— 开局就弹面板，玩家还没看懂操作就被迫做选择，体验很差。
///
/// 用法（最省事）：把本组件挂在场景里任意一个物体上，把词条资产拖进 Pool 就完事。
/// 面板会自动在运行时创建，不需要在场景里摆，也不需要在 Hierarchy 里拖引用。
/// </summary>
public class RelicManager : MonoBehaviour
{
    [Header("词条池")]
    [Tooltip("所有可能掉落的词条。留空会报警告，抽不出任何选项")]
    [SerializeField] private List<RelicData> pool = new();

    [Tooltip("一次弹几个选项。三选一是肉鸽的行业惯例")]
    [SerializeField] private int choiceCount = 3;

    [Tooltip("允许重复抽到已有的词条？数值型词条叠起来更爽，但默认关掉，避免构筑同质化")]
    [SerializeField] private bool allowDuplicates = false;

    [Header("联动")]
    [Tooltip("留空则自动找同物体上的 CharacterStats（挂在 Player 上就不用填）")]
    [SerializeField] private CharacterStats stats;

    [Tooltip("留空则自动找场景里的 RoomManager")]
    [SerializeField] private RoomManager roomManager;

    [Tooltip("留空则运行时自动建一个面板，不需要在场景里摆")]
    [SerializeField] private RelicChoiceUI choiceUI;

    [Header("触发规则")]
    [Tooltip("每进一间新房弹一次三选一")]
    [SerializeField] private bool offerOnRoomChanged = true;

    [Tooltip("第一间房跳过。开局就弹会让玩家还没看懂规则就做选择")]
    [SerializeField] private bool skipFirstRoom = true;

    [Tooltip("按 F5 手动弹一次，方便单独测面板，不用真的打完一间房")]
    [SerializeField] private bool debugHotkey = true;

    private readonly List<RelicData> _owned = new();
    private bool _offering;
    private bool _subscribed;

    /// <summary>已经拿到的词条，按获得顺序。HUD / 存档 / 成就读它</summary>
    public IReadOnlyList<RelicData> Owned => _owned;

    /// <summary>获得一条词条时触发</summary>
    public event Action<RelicData> RelicAdded;

    /// <summary>现在是不是正开着三选一面板</summary>
    public bool IsOffering => _offering;

    private void Awake()
    {
        if (stats == null) stats = GetComponent<CharacterStats>();
        if (stats == null) stats = GetComponentInChildren<CharacterStats>();
    }

    private void Start()
    {
        if (roomManager == null) roomManager = FindObjectOfType<RoomManager>();
        Subscribe();
    }

    private void OnEnable()
    {
        // 走 Start 再订阅是有意的：Awake 阶段场景里的 RoomManager 可能还没就绪
        if (roomManager != null) Subscribe();
    }

    private void OnDisable()
    {
        if (roomManager != null && _subscribed)
        {
            roomManager.RoomChanged -= OnRoomChanged;
            _subscribed = false;
        }
    }

    private void Subscribe()
    {
        if (roomManager == null || _subscribed) return;
        roomManager.RoomChanged += OnRoomChanged;
        _subscribed = true;
    }

    private void Update()
    {
        if (!debugHotkey || _offering) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.f5Key.wasPressedThisFrame) OfferChoices();
    }

    // ---------------- 触发 ----------------

    private void OnRoomChanged(int roomsCleared)
    {
        if (!offerOnRoomChanged || _offering) return;

        // 第一间房（roomsCleared == 1）是开局，跳过
        if (skipFirstRoom && roomsCleared <= 1) return;

        OfferChoices();
    }

    /// <summary>立刻弹一次三选一。F5 和 RoomChanged 都走这里</summary>
    public void OfferChoices()
    {
        if (_offering) return;

        List<RelicData> choices = DrawChoices(choiceCount);
        if (choices.Count == 0)
        {
            Debug.LogWarning("[Relic] 词条池是空的，抽不出任何选项。请把词条资产拖进 RelicManager 的 Pool。", this);
            return;
        }

        _offering = true;
        EnsureUI().Show(choices, OnPicked);
    }

    private void OnPicked(RelicData relic)
    {
        _offering = false;
        Add(relic);
    }

    // ---------------- 词条 ----------------

    /// <summary>直接获得一条词条（不走面板）。调试和「宝箱直接给」都用它</summary>
    public void Add(RelicData relic)
    {
        if (relic == null) return;

        _owned.Add(relic);

        if (stats != null && relic.modifiers != null)
        {
            for (int i = 0; i < relic.modifiers.Length; i++)
                stats.AddModifier(relic.modifiers[i], relic);
        }
        else if (stats == null)
        {
            Debug.LogWarning("[Relic] 找不到 CharacterStats，这条词条的数值不会生效。请把 RelicManager 挂在 Player 上，或手动填 Stats。", this);
        }

        RelicAdded?.Invoke(relic);

        string now = stats != null
            ? string.Format("攻击 {0:F1} / 生命上限 {1:F0} / 暴击 {2:P0}",
                stats.Get(StatType.Attack),
                stats.Get(StatType.MaxHealth),
                stats.Get(StatType.CritChance))
            : "（无数值面板）";

        Debug.Log(string.Format("[Relic] 获得词条「{0}」（{1}）：{2}　当前 {3}",
            relic.displayName, relic.RarityLabel, relic.description, now), relic);
    }

    /// <summary>
    /// 抽 count 个不重复的候选。
    /// 池子被抽干时退化成「允许重复」——否则玩家后期会拿到一个空面板，游戏直接卡死。
    /// </summary>
    private List<RelicData> DrawChoices(int count)
    {
        List<RelicData> candidates = new List<RelicData>();

        for (int i = 0; i < pool.Count; i++)
        {
            RelicData relic = pool[i];
            if (relic == null) continue;
            if (!allowDuplicates && _owned.Contains(relic)) continue;
            candidates.Add(relic);
        }

        if (candidates.Count == 0)
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null) candidates.Add(pool[i]);
        }

        // Fisher-Yates 洗牌：每个子集等概率，比「抽到重复就重抽」干净
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            RelicData tmp = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = tmp;
        }

        int take = Mathf.Min(count, candidates.Count);
        List<RelicData> result = new List<RelicData>(take);
        for (int i = 0; i < take; i++) result.Add(candidates[i]);
        return result;
    }

    private RelicChoiceUI EnsureUI()
    {
        if (choiceUI != null) return choiceUI;

        GameObject go = new GameObject("RelicChoiceUI");
        choiceUI = go.AddComponent<RelicChoiceUI>();
        return choiceUI;
    }
}
