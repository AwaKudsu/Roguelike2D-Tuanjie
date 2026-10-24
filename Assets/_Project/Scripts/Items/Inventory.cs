using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家的背包。只管「存哪些物品、最多存几件」，不认识装备也不认识属性。
///
/// 拆成 Inventory / Equipment 两个组件是有意的：
///   · 背包是「东西放在哪」
///   · 装备是「哪些东西正生效」
/// 将来加商店、掉落、仓库，都只跟背包打交道，不必碰装备逻辑。
/// </summary>
public class Inventory : MonoBehaviour
{
    [Header("容量")]
    [Tooltip("最多能放几件。UI 的格子数由它决定（4 列，行数自动算）")]
    [SerializeField] private int capacity = 12;

    [Header("初始物品")]
    [Tooltip("开局的几件东西。填在这里就不用每次手动捡")]
    [SerializeField] private List<ItemData> startingItems = new();

    private readonly List<ItemData> _items = new();

    /// <summary>背包内容变化时触发（捡到、丢掉、穿上、脱下都会响）</summary>
    public event System.Action Changed;

    public int Capacity => Mathf.Max(1, capacity);
    public IReadOnlyList<ItemData> Items => _items;
    public int Count => _items.Count;
    public bool IsFull => _items.Count >= Capacity;

    private void Start()
    {
        // 放 Start 而不是 Awake：要等 Equipment 先把身上预置的装备脱下来还回背包
        if (startingItems == null || startingItems.Count == 0) return;

        for (int i = 0; i < startingItems.Count; i++)
        {
            ItemData item = startingItems[i];
            if (item == null) continue;
            if (IsFull) break;
            _items.Add(item);
        }

        // 初始物品只发一次，之后重进场景由场景重置负责
        startingItems.Clear();
        Changed?.Invoke();
    }

    public bool Add(ItemData item)
    {
        if (item == null)
        {
            Debug.LogWarning("[Inventory] 想加入一个空物品，已忽略。", this);
            return false;
        }

        if (IsFull)
        {
            Debug.Log($"[Inventory] 背包满了（{Capacity} 格），「{item.displayName}」没进来。", this);
            return false;
        }

        _items.Add(item);
        Changed?.Invoke();
        return true;
    }

    public bool Remove(ItemData item)
    {
        if (item == null) return false;
        if (!_items.Remove(item)) return false;

        Changed?.Invoke();
        return true;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return false;

        _items.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    public bool Contains(ItemData item) => item != null && _items.Contains(item);

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }

    /// <summary>调试用：把背包内容打成一行日志</summary>
    [ContextMenu("打印背包内容")]
    public void LogContents()
    {
        if (_items.Count == 0)
        {
            Debug.Log("[Inventory] 背包是空的。", this);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"[Inventory] {_items.Count}/{Capacity}：");
        for (int i = 0; i < _items.Count; i++)
        {
            if (i > 0) sb.Append("、");
            sb.Append(_items[i].displayName);
        }
        Debug.Log(sb.ToString(), this);
    }
}
