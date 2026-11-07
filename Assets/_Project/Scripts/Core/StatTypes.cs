using System;

/// <summary>
/// 所有可被职业 / 装备 / 技能 / 强化修改的数值。
/// 新增一种数值只需要在这里加一项，其他系统一行都不用改。
/// </summary>
public enum StatType
{
    MaxHealth,        // 最大生命
    Attack,           // 攻击力
    MoveSpeed,        // 移动速度
    AttackSpeed,      // 攻击速度（1 = 正常）
    CritChance,       // 暴击率 0~1
    CritMultiplier,   // 暴击伤害倍率，1.5 = 150%
    Defense,          // 防御（按比例减伤）
    CooldownRate,     // 冷却缩减，0.2 = 冷却减少 20%
    MaxMana,          // 最大法力
    ManaRegen,        // 每秒回蓝

    // ---- 抗性 ----
    // 都是 0~1 的比例：0.25 = 少受 25%。0 = 没有抗性，1 = 完全免疫（实际会被钳到上限）。
    // 平时没人给它们加成，数值就是 0，所以「没这抗性」不需要额外判断。
    KnockbackResist,  // 击退抗性：只影响被打飞的距离，不影响伤害
    PoisonResist,     // 毒抗
    FireResist,       // 火抗
    IceResist,        // 冰抗
}

/// <summary>
/// 修饰符的叠加方式。三种模式的计算顺序是固定的：
/// 先加算 → 再百分比加算求和 → 最后百分比乘算连乘。
/// 顺序固定，结果才可预测，玩家才能「算得清」自己的构筑。
/// </summary>
public enum ModifierMode
{
    /// <summary>加算：+10 攻击力</summary>
    Flat,

    /// <summary>百分比加算：0.2 = +20%，多个同类先求和再作用</summary>
    PercentAdd,

    /// <summary>百分比乘算：0.2 = ×1.2，多个同类连乘 —— 这是构筑深度的来源</summary>
    PercentMultiply,
}

/// <summary>一条属性修饰符</summary>
[Serializable]
public struct StatModifier
{
    public StatType Type;
    public ModifierMode Mode;
    public float Value;

    /// <summary>来源标记：卸下装备时靠它精确移除自己加过的所有修饰符</summary>
    [NonSerialized] public object Source;

    public StatModifier(StatType type, ModifierMode mode, float value)
    {
        Type = type;
        Mode = mode;
        Value = value;
        Source = null;
    }
}