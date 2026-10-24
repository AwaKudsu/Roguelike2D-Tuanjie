using UnityEngine;

/// <summary>装备能占的格子。三个槽位固定，不做「两个戒指」这种扩展</summary>
public enum EquipSlot
{
    Weapon  = 0,   // 武器
    Armor   = 1,   // 护甲
    Trinket = 2,   // 饰品
}

public enum ItemRarity
{
    Common    = 0,   // 普通
    Rare      = 1,   // 稀有
    Legendary = 2,   // 传说
}

public static class EquipSlotNames
{
    public static string Of(EquipSlot slot)
    {
        switch (slot)
        {
            case EquipSlot.Weapon:  return "武器";
            case EquipSlot.Armor:   return "护甲";
            case EquipSlot.Trinket: return "饰品";
            default:                return "未知";
        }
    }

    /// <summary>三个槽位的固定顺序，UI 按这个顺序排</summary>
    public static readonly EquipSlot[] All =
    {
        EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Trinket,
    };
}

/// <summary>
/// 一件装备的数据定义。
///
/// 它和 RelicData 是同一套思路：装备本身没有任何逻辑，只是「一组 StatModifier」。
/// 穿上 = 把这些修饰符加到 CharacterStats 上；脱掉 = RemoveAllFrom(this)。
/// 所以加新装备不需要写一行代码。
/// </summary>
[CreateAssetMenu(fileName = "Item_", menuName = "Roguelike/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("标识")]
    public string displayName = "新装备";

    [TextArea(2, 4)]
    public string description;

    [Tooltip("留空则 UI 用稀有度颜色画一个色块，不依赖美术资源")]
    public Sprite icon;

    public ItemRarity rarity = ItemRarity.Common;

    [Header("装备")]
    public EquipSlot slot = EquipSlot.Weapon;

    [Tooltip("只有武器用得上：穿上后换成另一套攻击数据（比如法杖改成发射投射物）。\n" +
             "留空 = 沿用角色当前的普通攻击")]
    public AttackData weaponAttack;

    [Header("属性加成")]
    [Tooltip("这些修饰符在穿戴期间生效。百分比类填 0.1 表示 +10%")]
    public StatModifier[] modifiers = new StatModifier[0];

    // ---------------- 展示用 ----------------

    public Color RarityColor
    {
        get
        {
            switch (rarity)
            {
                case ItemRarity.Rare:      return new Color(0.36f, 0.62f, 1f);
                case ItemRarity.Legendary: return new Color(1f, 0.78f, 0.25f);
                default:                   return new Color(0.82f, 0.83f, 0.86f);
            }
        }
    }

    public string RarityLabel
    {
        get
        {
            switch (rarity)
            {
                case ItemRarity.Rare:      return "稀有";
                case ItemRarity.Legendary: return "传说";
                default:                   return "普通";
            }
        }
    }

    public string SlotLabel => EquipSlotNames.Of(slot);

    /// <summary>把 modifiers 拼成多行中文，给提示框用</summary>
    public string DescribeModifiers()
    {
        if (modifiers == null || modifiers.Length == 0) return "无属性加成";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(StatText.Describe(modifiers[i]));
        }
        return sb.ToString();
    }
}

/// <summary>把 StatModifier 翻译成人话。所有 UI 共用，避免三处各写一份格式</summary>
public static class StatText
{
    public static string Name(StatType type)
    {
        switch (type)
        {
            case StatType.MaxHealth:      return "最大生命";
            case StatType.Attack:         return "攻击力";
            case StatType.MoveSpeed:      return "移动速度";
            case StatType.AttackSpeed:    return "攻击速度";
            case StatType.CritChance:     return "暴击率";
            case StatType.CritMultiplier: return "暴击伤害";
            case StatType.Defense:        return "防御";
            case StatType.CooldownRate:   return "冷却缩减";
            case StatType.MaxMana:        return "最大法力";
            case StatType.ManaRegen:      return "每秒回蓝";
            default:                      return type.ToString();
        }
    }

    /// <summary>这些属性本身就是 0~1 的比例，Flat 加成也要显示成百分比</summary>
    private static bool IsRatio(StatType type)
    {
        return type == StatType.CritChance
            || type == StatType.Defense
            || type == StatType.CooldownRate
            || type == StatType.CritMultiplier;
    }

    public static string Describe(StatModifier m)
    {
        string name = Name(m.Type);

        switch (m.Mode)
        {
            case ModifierMode.Flat:
                if (IsRatio(m.Type))
                    return string.Format("{0} {1:+0.#;-0.#}%", name, m.Value * 100f);
                return string.Format("{0} {1:+0.#;-0.#}", name, m.Value);

            case ModifierMode.PercentAdd:
                return string.Format("{0} {1:+0.#;-0.#}%", name, m.Value * 100f);

            case ModifierMode.PercentMultiply:
                return string.Format("{0} ×{1:0.##}", name, 1f + m.Value);

            default:
                return name;
        }
    }
}
