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
    // 24 = 界面能放的格子数（6 列 × 4 行）。界面那边超过 24 会打警告并截断，
    // 所以这两个数要一起改。
    [SerializeField] private int capacity = 24;

    [Header("初始物品")]
    [Tooltip("开局的几件东西。填在这里就不用每次手动捡")]
    [SerializeField] private List<ItemData> startingItems = new();

    [Header("金币")]
    [Tooltip("开局带多少钱。卖东西加、买东西减（商店还没做，先只有卖出）")]
    [SerializeField] private int startingGold = 60;

    [Header("开场药水")]
    [Tooltip("开局塞几瓶生命药水进背包。做成一个数而不是资源，是为了不用新建并维护 .asset")]
    [SerializeField] private int startingPotions = 2;
    [SerializeField] private float potionHealAmount = 35f;
    [SerializeField] private int potionPrice = 12;

    private readonly List<ItemData> _items = new();

    /// <summary>背包内容变化时触发（捡到、丢掉、穿上、脱下都会响）</summary>
    public event System.Action Changed;

    /// <summary>金币变化时触发</summary>
    public event System.Action GoldChanged;

    public int Gold { get; private set; }

    public void AddGold(int amount)
    {
        if (amount == 0) return;
        Gold = Mathf.Max(0, Gold + amount);
        GoldChanged?.Invoke();
    }

    public bool TrySpendGold(int amount)
    {
        if (amount <= 0) return true;
        if (Gold < amount) return false;

        Gold -= amount;
        GoldChanged?.Invoke();
        return true;
    }

    public int Capacity => Mathf.Max(1, capacity);
    public IReadOnlyList<ItemData> Items => _items;
    public int Count => _items.Count;
    public bool IsFull => _items.Count >= Capacity;

    private void Start()
    {
        Gold = Mathf.Max(0, startingGold);

        // 放 Start 而不是 Awake：要等 Equipment 先把身上预置的装备脱下来还回背包
        if (startingItems != null && startingItems.Count > 0)
        {
            for (int i = 0; i < startingItems.Count; i++)
            {
                ItemData item = startingItems[i];
                if (item == null) continue;
                if (IsFull) break;
                _items.Add(item);
            }

            // 初始物品只发一次，之后重进场景由场景重置负责
            startingItems.Clear();
        }

        AddStartingPotions();
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

    /// <summary>
    /// 塞几瓶生命药水。用运行时造出来的 ItemData，而不是新建 .asset：
    /// 团结引擎的 .meta guid 是 base64 形式，手写 asset 很容易和场景里的引用对不上。
    /// </summary>
    private void AddStartingPotions()
    {
        if (startingPotions <= 0) return;

        for (int i = 0; i < startingPotions; i++)
        {
            if (IsFull) break;

            ItemData potion = ScriptableObject.CreateInstance<ItemData>();
            potion.name        = "Item_Potion";
            potion.displayName = "生命药水";
            potion.description = "灌下去立刻回一口血。喝不完就卖给商人。";
            potion.rarity      = ItemRarity.Common;
            potion.category    = ItemCategory.Consumable;
            potion.healAmount  = potionHealAmount;
            potion.price       = potionPrice;

            _items.Add(potion);
        }

        Changed?.Invoke();
    }

    /// <summary>调试用：把背包内容打成一行日志</summary>    [ContextMenu("打印背包内容")]
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
