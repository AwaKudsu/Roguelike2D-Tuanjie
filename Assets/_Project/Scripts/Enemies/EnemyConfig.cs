using UnityEngine;

/// <summary>
/// 敌人的六种打法。加到第七种之前，先看看能不能用现有参数的组合表达。
/// </summary>
public enum EnemyBehavior
{
    /// <summary>走近，贴脸挥一刀（哥布林）</summary>
    Melee = 0,

    /// <summary>保持距离射投射物，被逼近就后退（骷髅弓箭手）</summary>
    Archer = 1,

    /// <summary>蓄力跳跃，落地砸出范围伤害（史莱姆）</summary>
    Jumper = 2,

    /// <summary>悬停绕圈，隔一会儿俯冲咬一口（蝙蝠）</summary>
    Flyer = 3,

    /// <summary>站远了吟唱，吟唱完放一颗火球（火焰法师）</summary>
    Caster = 4,

    /// <summary>举盾正面减伤，拉开距离后直线冲撞（持盾战士）</summary>
    Shielder = 5,
}

/// <summary>
/// 一只怪的全部可调参数。
///
/// ★ 为什么用 ScriptableObject 而不是把字段都摊在 EnemyController 上：
///   六个怪物 × 十几个参数，如果全写在组件里，预制体上一个长长的 Inspector
///   既看不出「这是哪只怪」，改一个数也不知道影响谁。
///   摊成资产之后，「哥布林的脾气」就是一个文件，复制一份改改就是新怪。
///
/// ★ 数值只放「行为上必须知道的」（距离、节奏、参数），
///   攻击力/血量/移速还是走 CharacterStats —— 那是全局唯一的数值出口。
/// </summary>
[CreateAssetMenu(menuName = "Roguelike/Enemy Config", fileName = "Enemy_New")]
public class EnemyConfig : ScriptableObject
{
    [Header("身份")]
    public string displayName = "哥布林";

    [Tooltip("六种打法之一，决定 EnemyController 走哪套逻辑")]
    public EnemyBehavior behavior = EnemyBehavior.Melee;

    [Tooltip("这一下伤害带什么元素：史莱姆=Poison、火焰法师=Fire，其余 None")]
    public DamageElement element = DamageElement.None;

    [Header("感知")]
    [Tooltip("发现玩家的距离")]
    public float detectRange = 9f;

    [Tooltip("进入这个距离就动手（近战=砍到你，远程=开始射）")]
    public float attackRange = 1.3f;

    [Tooltip("超出这个距离就放弃追击，比 detectRange 大一点避免边界抖动")]
    public float loseTargetRange = 14f;

    [Header("近战攻击（Melee / Shielder 用）")]
    public AttackData meleeAttack;

    [Tooltip("两次攻击之间至少隔多久")]
    public float meleeCooldown = 1.4f;

    [Tooltip("出手前的抬手时间：这段时间里它站着不动，是玩家的反应窗口")]
    public float meleeWindup = 0.25f;

    [Header("远程攻击（Archer / Caster 用）")]
    public Projectile projectilePrefab;

    [Tooltip("射程：玩家在 attackRange ~ rangedRange 之间才会射")]
    public float rangedRange = 7f;

    public float rangedCooldown = 2.2f;

    [Tooltip("玩家比这个距离还近，就往后退（风筝）。0 = 不后退")]
    public float retreatDistance = 2.6f;

    [Tooltip("吟唱时间：长度就是玩家的打断窗口。Archer 一般给 0.3，Caster 给 0.9")]
    public float chargeTime = 0.5f;

    [Header("跳跃（Jumper 用）")]
    [Tooltip("一次跳多远")]
    public float jumpDistance = 3.6f;

    [Tooltip("一跳飞多久")]
    public float jumpDuration = 0.55f;

    [Tooltip("跳到最高点比起点高多少")]
    public float jumpHeight = 1.5f;

    [Tooltip("落地之后要趴多久才起下一跳")]
    public float landRest = 0.9f;

    [Tooltip("落地时以自身为圆心的伤害半径")]
    public float landRadius = 1.1f;

    [Tooltip("落地砸一下多少伤害")]
    public float landDamage = 8f;

    [Header("飞行（Flyer 用）")]
    [Tooltip("悬停时离玩家的水平距离")]
    public float hoverDistance = 2.8f;

    [Tooltip("悬停时比玩家高多少")]
    public float hoverHeight = 1.4f;

    [Tooltip("俯冲速度（明显快于它平时的移动）")]
    public float diveSpeed = 10f;

    [Tooltip("一次俯冲持续多久")]
    public float diveDuration = 0.45f;

    [Tooltip("两次俯冲之间隔多久")]
    public float diveCooldown = 1.8f;

    [Tooltip("俯冲咬到人的伤害")]
    public float diveDamage = 7f;

    [Header("盾牌（Shielder 用）")]
    [Range(0f, 0.9f)]
    [Tooltip("正面挨打时减掉多少伤害（0.6 = 只吃 40%）")]
    public float shieldReduction = 0.6f;

    [Range(10f, 180f)]
    [Tooltip("盾牌护住的角度范围（以朝向为中心的半角×2）")]
    public float shieldArc = 100f;

    [Tooltip("离玩家多远开始准备冲撞")]
    public float chargeTriggerRange = 5f;

    [Tooltip("举盾蓄势时间：盾压低的动作，玩家的侧闪窗口")]
    public float chargeWindup = 0.55f;

    [Tooltip("冲撞速度")]
    public float chargeSpeed = 9f;

    [Tooltip("冲撞持续多久（撞到墙会提前停）")]
    public float chargeDuration = 0.6f;

    [Tooltip("撞到人多少伤害")]
    public float chargeDamage = 12f;

    public float chargeCooldown = 3f;

    [Header("动画帧（留空就用静态贴图，不报错）")]
    public Sprite[] idleFrames;
    public Sprite[] moveFrames;
    public Sprite[] attackFrames;
    public Sprite[] hurtFrames;
    public Sprite[] deathFrames;

    [Tooltip("每套动作每秒播几帧")]
    public float idleFps = 6f;
    public float moveFps = 10f;
    public float attackFps = 12f;
    public float hurtFps = 12f;
    public float deathFps = 8f;
}
