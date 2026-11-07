using UnityEngine;

/// <summary>
/// 一次攻击 / 一个技能的数据定义。
///
/// 这是整个「技能系统」的接入点：
/// 普通攻击是一个 AttackData 资产，战士的冲锋、法师的火球也各是一个 AttackData，
/// 区别只在数值和判定形状。所以第 2 周加技能时，攻击代码一个字都不用改。
/// </summary>
[CreateAssetMenu(fileName = "Attack_", menuName = "Roguelike/Attack Data")]
public class AttackData : ScriptableObject
{
    [Header("标识")]
    public string displayName = "普通攻击";
    public Sprite icon;

    [Header("伤害")]
    [Tooltip("伤害 = 攻击力 × 这个倍率。用倍率而不是固定值，装备变强时技能自动变强")]
    public float damageMultiplier = 1f;

    [Tooltip("击退力度")]
    public float knockbackForce = 7f;

    [Tooltip("技能自带的额外暴击率")]
    [Range(0f, 1f)]
    public float bonusCritChance = 0f;

    [Header("元素")]
    [Tooltip("伤害元素。吃不吃抗性看这里：物理走防御，毒/火/冰还要再过一道对应抗性。\n" +
             "火球填 Fire、法师法杖弹填 None（奥术）、毒池类填 Poison")]
    public DamageElement element = DamageElement.None;

    [Header("判定范围")]
    [Tooltip("判定框相对角色的偏移，x 会被朝向翻转。\n" +
             "(0,0) = 以角色为中心，身前身后都打得到（旋风斩这类原地 AoE）。\n" +
             "填正数 = 判定框挪到身前（普通挥砍）。")]
    // 默认值刻意用「居中」而不是「身前」：
    // 忘了改偏移时，居中会让攻击**打到太多**（一眼能看出不对），
    // 而身前会让 AoE **打不到身后**——攻击照样能命中，于是错误永远不暴露。
    // 宁可错得明显，也不要错得安静。
    public Vector2 hitboxOffset = Vector2.zero;

    [Tooltip("判定框的宽和高（世界单位）。改完按 Play 打一下，看 Scene 视图里亮青色的框")]
    public Vector2 hitboxSize = new Vector2(1.2f, 0.9f);

    [Tooltip("能打到的层。⚠️ 必须设成 Enemy，留空会永远打不到人")]
    public LayerMask targetLayers;

    [Header("远程")]
    [Tooltip("勾上就从「身前的判定框」变成「发射投射物」。法师的法杖弹靠它")]
    public bool isRanged = false;

    [Tooltip("Is Ranged 勾上时必填：要发射的投射物预制体")]
    public GameObject projectilePrefab;

    [Header("节奏")]
    [Tooltip("两次攻击之间的最短间隔")]
    public float cooldown = 0.35f;
}