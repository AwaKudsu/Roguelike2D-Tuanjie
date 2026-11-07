using UnityEngine;

/// <summary>
/// 伤害公式的唯一出处。
///
/// 普通攻击、近战技能、投射物技能全都走这里。
/// 好处是将来要调平衡（「暴击应该只算武器的那部分」「加个破防属性」），
/// 只需要改这一个文件，不会出现「改了三处漏了第四处」。
///
/// 这是个静态类，不需要挂在任何物体上。
/// </summary>
public static class DamageCalculator
{
    /// <summary>
    /// 伤害 = 攻击力 × 技能倍率 ×（暴击时再乘暴击倍率）
    ///
    /// 用「攻击力 × 倍率」而不是固定伤害值，装备变强时技能会跟着变强，
    /// 不需要回头去改几十个技能资产 —— 这是数据驱动的关键一步。
    /// </summary>
    /// <param name="stats">攻击者的属性。传 null 也能跑，会退化成默认值，方便做纯场景测试</param>
    /// <param name="data">这次攻击用的数据（倍率、击退、额外暴击率）</param>
    /// <param name="attacker">谁打的，记在 DamageInfo 里给仇恨 / 击杀统计用</param>
    /// <param name="sourcePosition">伤害来源坐标，受击方向靠它算</param>
    public static DamageInfo Build(CharacterStats stats, AttackData data, GameObject attacker, Vector2 sourcePosition)
    {
        float attack  = stats != null ? stats.Get(StatType.Attack)         : 10f;
        float critMul = stats != null ? stats.Get(StatType.CritMultiplier) : 1.5f;

        float critChance = (stats != null ? stats.Get(StatType.CritChance) : 0f)
                         + data.bonusCritChance;

        bool isCrit = Random.value < critChance;

        return new DamageInfo
        {
            Amount         = attack * data.damageMultiplier * (isCrit ? critMul : 1f),
            Element        = data.element,
            SourcePosition = sourcePosition,
            KnockbackForce = data.knockbackForce,
            Attacker       = attacker,
            IsCritical     = isCrit,
        };
    }

    /// <summary>
    /// 元素抗性对应的属性值（0~1）。物理没有抗性通道，固定返回 0 —— 物理只吃防御。
    /// </summary>
    public static float ResistFor(CharacterStats stats, DamageElement element)
    {
        if (stats == null) return 0f;

        switch (element)
        {
            case DamageElement.Poison: return Mathf.Clamp01(stats.Get(StatType.PoisonResist));
            case DamageElement.Fire:   return Mathf.Clamp01(stats.Get(StatType.FireResist));
            case DamageElement.Ice:    return Mathf.Clamp01(stats.Get(StatType.IceResist));
            default:                   return 0f;
        }
    }

    /// <summary>元素的中文名，飘字和提示框用</summary>
    public static string ElementLabel(DamageElement element)
    {
        switch (element)
        {
            case DamageElement.Poison: return "毒";
            case DamageElement.Fire:   return "火";
            case DamageElement.Ice:    return "冰";
            default:                   return "物理";
        }
    }
}
