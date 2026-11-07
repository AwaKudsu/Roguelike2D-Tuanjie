using UnityEngine;

/// <summary>
/// 技能的施放方式。想加一种新方式（比如「朝天上召唤一道雷」），
/// 就是在这里加一项 + 在 SkillCaster.Cast() 里加一个 case。
/// </summary>
public enum SkillCastType
{
    /// <summary>在角色身边打一个判定框，命中范围内的所有敌人。旋风斩、冲击波</summary>
    Melee,

    /// <summary>发射投射物。火球、箭、飞刀</summary>
    Projectile,

    /// <summary>
    /// 战吼：不打伤害，从自身放一圈冲击波，把范围内的敌人吓跑（丢掉目标、来回乱跑）。
    /// 见 SkillCaster.CastWarcry 和 WarCryEffect。
    /// </summary>
    Warcry,

    /// <summary>
    /// 回复：以自身为圆心撑开一个法阵，在一段时间里一点点回血。
    /// 见 SkillCaster.CastHeal 和 HealCircle。
    /// </summary>
    Heal,
}

/// <summary>
/// 一个技能的全部配置。
///
/// 和 AttackData 的分工：
///   AttackData 管「打出去长什么样」—— 伤害倍率、击退、判定框多大、能打哪些层
///   SkillData  管「什么时候能打、花多少代价」—— 法力、冷却、怎么发射、发几发
///
/// 所以一个技能 = 一个 SkillData 资产 + 一个 AttackData 资产，都是右键新建。
/// 将来战士的 3 个技能、法师的 3 个技能，全都只是 12 个资产文件，一行代码都不用写。
/// </summary>
[CreateAssetMenu(fileName = "Skill_", menuName = "Roguelike/Skill Data")]
public class SkillData : ScriptableObject
{
    [Header("标识")]
    [Tooltip("唯一 id，技能树和存档靠它认人。发布之后不要改，否则已解锁的记录会对不上")]
    public string skillId = "new_skill";

    public string displayName = "新技能";

    public Sprite icon;

    [TextArea(2, 4)]
    public string description;

    [Header("消耗与冷却")]
    [Tooltip("放一次要多少法力。0 = 不耗蓝")]
    public float manaCost = 10f;

    [Tooltip("技能自身的冷却（秒）。只吃「冷却缩减」，不吃「攻速」")]
    public float cooldown = 3f;

    [Header("施放方式")]
    public SkillCastType castType = SkillCastType.Melee;

    [Header("伤害与判定")]
    [Tooltip("伤害倍率 / 击退 / 判定框尺寸 / 目标层都在这个资产里")]
    public AttackData attack;

    [Header("持续判定（castType = Melee 且 duration > 0 时使用）")]
    [Tooltip("判定持续多久（秒）。0 = 打一下就结束（老行为）；> 0 = 在这段时间里每隔 tickInterval 打一次 —— 旋风斩就是靠这个")]
    public float duration = 0f;

    [Tooltip("持续判定每隔多久打一次（秒）。越小越密，但每密一次就多一次伤害")]
    [Min(0.05f)]
    public float tickInterval = 0.22f;

    [Header("战吼（castType = Warcry 时使用）")]
    [Tooltip("敌人被吓住多久（秒）。这段时间里它丢掉攻击目标、背对着你来回跑")]
    public float fearDuration = 3.5f;

    [Tooltip("战吼的影响半径（世界单位），以玩家为圆心。屏幕内的敌人基本都在这个圈里")]
    public float fearRadius = 12f;

    [Tooltip("冲击波圆环从「贴着身子」扩散到 fearRadius 要多久（秒）")]
    public float ringDuration = 0.45f;

    [Tooltip("冲击波用的圆环贴图（一张白色空心圆就够，颜色靠 tint）")]
    public Sprite ringSprite;

    [Tooltip("战吼时在头顶亮出来的巨剑贴图")]
    public Sprite swordSprite;

    [Tooltip("持续型近战（旋风斩）绕在身上的漩涡贴图；留空就只有刀光")]
    public Sprite auraSprite;

    [Header("投射物（castType = Projectile 时使用）")]
    public GameObject projectilePrefab;

    [Tooltip("一次发几发。强化系统做「多重射击」就是加这个数字")]
    [Range(1, 8)]
    public int projectileCount = 1;

    [Tooltip("发多发时的扇形张角（度）。3 发 + 10 度 = -10° / 0° / +10°")]
    public float spreadAngle = 10f;

    [Header("蓄力（castType = Projectile 且 chargeable 时使用）")]
    [Tooltip("勾上后：按住技能键开始蓄力，火球越蓄越大，松手才发出去")]
    public bool chargeable = false;

    [Tooltip("蓄满要按多久（秒）。到时间后不再变大")]
    public float chargeTime = 1.1f;

    [Tooltip("刚按下时投射物多大（1 = 原尺寸）")]
    public float chargeMinScale = 0.7f;

    [Tooltip("蓄满时投射物多大")]
    public float chargeMaxScale = 2.2f;

    [Tooltip("蓄满时的飞行速度倍率。蓄得越久飞得越快，不然大球只是慢吞吞飘过去")]
    public float chargeMaxSpeedBonus = 1.9f;

    [Tooltip("蓄满时的伤害倍率（乘在 AttackData 的伤害上）")]
    public float chargeMaxDamageBonus = 2.0f;

    [Header("回复（castType = Heal 时使用）")]
    [Tooltip("每次跳多少血。总回复 = healAmount × (healDuration / healTickInterval)")]
    public float healAmount = 6f;

    [Tooltip("法阵持续多久（秒）")]
    public float healDuration = 2.4f;

    [Tooltip("每隔多久回一次血（秒）")]
    [Min(0.05f)]
    public float healTickInterval = 0.3f;

    [Tooltip("法阵贴图（那张金色圆环）")]
    public Sprite circleSprite;

    [Tooltip("法阵最大直径（世界单位）")]
    public float circleDiameter = 3.2f;

    [Tooltip("治疗数字的颜色")]
    public Color healColor = new Color(0.45f, 1f, 0.55f, 1f);

    /// <summary>
    /// 实际冷却。
    ///
    /// 技能只吃「冷却缩减」，**不吃攻速** —— 如果堆攻速也能刷技能，
    /// 那攻速就会变成唯一解，装备和强化就没有别的选择可言了。
    /// 攻速只影响普通攻击的间隔（见 MeleeAttacker.GetCooldown）。
    /// </summary>
    public float GetCooldown(CharacterStats stats)
    {
        if (stats == null) return cooldown;

        float reduction = Mathf.Clamp(stats.Get(StatType.CooldownRate), 0f, 0.8f);
        return cooldown * (1f - reduction);
    }

    /// <summary>法力够不够放这一个技能</summary>
    public bool CanAfford(ManaPool mana) => mana == null || mana.Has(manaCost);
}
