using UnityEngine;

/// <summary>
/// 词条的稀有度。只影响卡片颜色和将来可能的掉落权重，不改数值。
/// </summary>
public enum RelicRarity
{
    /// <summary>普通：纯数值，幅度小</summary>
    Common,

    /// <summary>稀有：数值更大，或带一点代价</summary>
    Rare,

    /// <summary>传说：改变玩法的那一档</summary>
    Legendary,
}

/// <summary>
/// 一个「词条」= 打完一间房之后可以选的一条强化。
///
/// 它的全部内容只是「一串属性修饰符」，所以加新词条不需要写一行代码：
/// 右键 Create → Roguelike → Relic Data，填几个数就行。
///
/// 为什么用数据资产而不是给每条词条写一个类：
///   词条是要被玩家刷几百次的东西，纯数据才能被配置表、平衡表、随机池统一管理。
///   将来如果要做「击杀回血」这种需要代码钩子的效果，给这里加一个可选的
///   「特殊效果」字段就够了，不需要推翻现在这套。
///
/// 修饰符最终交给 CharacterStats.AddModifier()，
/// 而战斗代码只从 CharacterStats.Get() 取最终值 ——
/// 所以一条词条会自动作用于攻击力、暴击、冷却、移速、法力、生命，不用改任何战斗代码。
/// </summary>
[CreateAssetMenu(fileName = "Relic_", menuName = "Roguelike/Relic Data")]
public class RelicData : ScriptableObject
{
    [Header("展示")]
    [Tooltip("卡片上显示的名字，比如「锋利的爪」")]
    public string displayName = "新词条";

    [TextArea(2, 4)]
    [Tooltip("一句话说清楚它做什么。玩家只会读这一行，别写「提升战斗能力」这种空话")]
    public string description = "攻击力 +10";

    [Tooltip("卡片上的图标。留空就画一个纯色方块，不影响功能")]
    public Sprite icon;

    [Tooltip("只影响卡片配色。普通=灰白，稀有=蓝，传说=金")]
    public RelicRarity rarity = RelicRarity.Common;

    [Header("效果")]
    [Tooltip("可以填多条。比如「狂怒」= 攻击力 +25% 且 最大生命 -15")]
    public StatModifier[] modifiers;

    /// <summary>稀有度对应的主题色，卡片边框用这个</summary>
    public Color RarityColor
    {
        get
        {
            switch (rarity)
            {
                case RelicRarity.Rare:      return new Color(0.36f, 0.62f, 1f);
                case RelicRarity.Legendary: return new Color(1f, 0.78f, 0.25f);
                default:                    return new Color(0.75f, 0.78f, 0.85f);
            }
        }
    }

    /// <summary>稀有度的中文名，卡片上直接显示</summary>
    public string RarityLabel
    {
        get
        {
            switch (rarity)
            {
                case RelicRarity.Rare:      return "稀有";
                case RelicRarity.Legendary: return "传说";
                default:                    return "普通";
            }
        }
    }
}
