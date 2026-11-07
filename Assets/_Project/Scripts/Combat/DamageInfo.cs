using UnityEngine;

/// <summary>
/// 伤害的元素类型。决定挨打的一方用哪条抗性去减免。
/// 现在只有四种：物理（无元素）、毒、火、冰 —— 刚好对上战士和法师的技能。
/// 加新元素要同时做三件事：这里加一项、<see cref="StatType"/> 加一条抗性、<see cref="StatText"/> 加中文名。
/// </summary>
public enum DamageElement
{
    None,     // 物理，吃防御
    Poison,
    Fire,
    Ice,
}

/// <summary>
/// 一次伤害的完整描述。
///
/// 用结构体而不是一堆方法参数：将来要加「元素类型」「伤害类型」「是否破防」时，
/// 只需要在这里加字段，所有攻击方和受击方的函数签名都不用动。
/// </summary>
public struct DamageInfo
{
    /// <summary>伤害数值（**已经扣过防御与元素抗性**，受击方拿到的就是这个数）</summary>
    public float Amount;

    /// <summary>元素类型，决定用哪条抗性减免</summary>
    public DamageElement Element;

    /// <summary>伤害来源的世界坐标，用于计算击退方向</summary>
    public Vector2 SourcePosition;

    /// <summary>击退力度，0 表示不击退</summary>
    public float KnockbackForce;

    /// <summary>攻击者，用于避免自己打自己，以及将来的伤害统计</summary>
    public GameObject Attacker;

    /// <summary>是否暴击（用于飘字颜色等表现）</summary>
    public bool IsCritical;
}