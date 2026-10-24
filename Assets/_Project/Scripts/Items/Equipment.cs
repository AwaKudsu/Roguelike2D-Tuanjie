using UnityEngine;

/// <summary>
/// 玩家身上的三个装备槽：武器 / 护甲 / 饰品。
///
/// 它做两件事：
///   1. 把装备的 StatModifier 加进 CharacterStats（脱下来时用 RemoveAllFrom 一次全撤）
///   2. 武器还要换掉 PlayerAttacker 的攻击数据（法杖 → 发射投射物）
///
/// 它不改任何战斗代码 —— 战斗那边一直只调 CharacterStats.Get()，
/// 所以「穿一件 +6 攻击的剑」立刻反映到伤害里，伤害公式一行都不用动。
/// </summary>
public class Equipment : MonoBehaviour
{
    [Header("引用（留空会自动在本物体上找）")]
    [SerializeField] private CharacterStats stats;
    [SerializeField] private PlayerAttacker attacker;
    [SerializeField] private Inventory inventory;

    [Header("调试")]
    [Tooltip("穿脱时打日志。做数值平衡时很有用")]
    [SerializeField] private bool logChanges = true;

    private readonly ItemData[] _equipped = new ItemData[3];

    /// <summary>穿脱装备后触发，UI 订阅它刷新</summary>
    public event System.Action Changed;

    /// <summary>没穿武器时本该用的攻击（换下武器要还原成它）</summary>
    private AttackData _defaultAttack;

    public ItemData this[EquipSlot slot] => _equipped[(int)slot];

    private void Awake()
    {
        if (stats == null)     stats     = GetComponent<CharacterStats>();
        if (attacker == null)  attacker  = GetComponent<PlayerAttacker>();
        if (inventory == null) inventory = GetComponent<Inventory>();

        // 记下「出厂攻击」。穿武器时覆盖它，脱下时还原它
        if (attacker != null) _defaultAttack = attacker.CurrentAttack;
    }

    public ItemData Get(EquipSlot slot) => _equipped[(int)slot];

    public bool IsEquipped(ItemData item)
    {
        if (item == null) return false;
        for (int i = 0; i < _equipped.Length; i++)
            if (ReferenceEquals(_equipped[i], item)) return true;
        return false;
    }

    /// <summary>从背包里穿上一件。同槽位已有装备时，旧的那件自动回背包</summary>
    public bool Equip(ItemData item)
    {
        if (item == null) return false;

        int index = (int)item.slot;
        if (index < 0 || index >= _equipped.Length)
        {
            Debug.LogWarning($"[Equipment] 「{item.displayName}」的槽位不合法，无法穿戴。", this);
            return false;
        }

        if (ReferenceEquals(_equipped[index], item)) return false;   // 已经穿着了

        ItemData old = _equipped[index];

        // 先把新装备从背包拿走，腾出一格；再把旧的放回去。
        // 顺序不能反 —— 反了的话背包满时会「旧的回不来、新的也没穿上」
        if (inventory != null) inventory.Remove(item);

        // ★ 必须先撤掉旧装备的修饰符，否则两件的加全会叠加，
        //   而且旧的那件已经不在槽位里了，以后再也没有机会被 Clear —— 加成会永久赖在玩家身上。
        if (old != null) Clear(old);

        _equipped[index] = item;
        Apply(item);

        if (old != null && inventory != null && !inventory.Add(old))
            Debug.LogWarning($"[Equipment] 背包满了，「{old.displayName}」没地方放，被丢掉了。", this);

        if (logChanges)
            Debug.Log($"[Equipment] 穿上{equipSlotLabel(item)}「{item.displayName}」" +
                      (old != null ? $"，换下「{old.displayName}」" : ""), this);

        Changed?.Invoke();
        return true;
    }

    /// <summary>脱下某槽位的装备，放回背包。背包满了就脱不下来（不会凭空吞掉物品）</summary>
    public bool Unequip(EquipSlot slot)
    {
        int index = (int)slot;
        if (index < 0 || index >= _equipped.Length) return false;

        ItemData item = _equipped[index];
        if (item == null) return false;

        if (inventory != null && inventory.IsFull)
        {
            Debug.LogWarning($"[Equipment] 背包满了，「{item.displayName}」脱不下来。", this);
            return false;
        }

        _equipped[index] = null;
        Clear(item);

        if (inventory != null) inventory.Add(item);

        if (logChanges)
            Debug.Log($"[Equipment] 脱下{equipSlotLabel(item)}「{item.displayName}」", this);

        Changed?.Invoke();
        return true;
    }

    // ---------------- 生效 / 失效 ----------------

    private void Apply(ItemData item)
    {
        if (stats != null && item.modifiers != null)
        {
            for (int i = 0; i < item.modifiers.Length; i++)
                stats.AddModifier(item.modifiers[i], item);   // source = 这件装备，脱下时按 source 全撤
        }

        // 武器额外接管普通攻击形态
        if (item.slot == EquipSlot.Weapon && attacker != null)
        {
            // 兜底：Awake 没跑过（比如编辑器里直接调）时，用当前攻击当「出厂攻击」，
            // 免得脱下武器后攻击变成 null
            if (_defaultAttack == null) _defaultAttack = attacker.CurrentAttack;

            attacker.SetAttack(item.weaponAttack != null ? item.weaponAttack : _defaultAttack);
        }
    }

    private void Clear(ItemData item)
    {
        if (stats != null) stats.RemoveAllFrom(item);

        if (item.slot == EquipSlot.Weapon && attacker != null)
            attacker.SetAttack(_defaultAttack);
    }

    private static string equipSlotLabel(ItemData item) => EquipSlotNames.Of(item.slot) + "槽";
}
