using System;
using UnityEngine;

/// <summary>
/// 通用血量组件：玩家、敌人、可破坏物都能挂。
/// 无敌帧在这里统一处理，攻击方完全不需要关心。
///
/// 如果同一个物体上有 CharacterStats，防御属性会在这里生效 ——
/// 这是「数值层」和「结算层」的唯一交汇点。
/// </summary>
public class Health : MonoBehaviour, IDamageable
{
    [Header("血量")]
    [SerializeField] private float maxHealth = 100f;

    [Header("无敌帧")]
    [Tooltip("受击后多久内免疫后续伤害，0 表示不免疫")]
    [SerializeField] private float invincibilityDuration = 0.5f;

    public float Max => maxHealth;
    public float Current { get; private set; }
    public bool IsAlive => Current > 0f;

    /// <summary>血量百分比 0~1，血条 UI 直接用它</summary>
    public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;

    /// <summary>受到有效伤害时触发（被无敌帧挡掉的不触发）</summary>
    public event Action<DamageInfo> Damaged;

    /// <summary>血量归零时触发</summary>
    public event Action Died;

    /// <summary>回血成功时触发，参数是这一下**实际**回了多少</summary>
    public event Action<float> Healed;

    /// <summary>
    /// 伤害过滤：防御与元素抗性算完之后、写回 info.Amount 之前再插一道。
    /// 返回过滤后的伤害值；返回 0 就是完全挡下。持盾战士按「来向」用它。
    /// </summary>
    public Func<float, DamageInfo, float> DamageFilter;

    private CharacterStats _stats;
    private float _invincibilityTimer;

    private void Awake()
    {
        _stats = GetComponent<CharacterStats>();

        Current = maxHealth;
    }

    private void Update()
    {
        if (_invincibilityTimer > 0f)
            _invincibilityTimer -= Time.deltaTime;
    }

    public void TakeDamage(DamageInfo info)
    {
        if (!IsAlive) return;                  // 已经死了，不重复结算
        if (_invincibilityTimer > 0f) return;  // 处于无敌帧

        // 防御按比例减伤，上限 80% —— 不设上限的话高防御角色会完全免疫
        float defense   = _stats != null ? _stats.Get(StatType.Defense) : 0f;
        float reduction = Mathf.Clamp(defense, 0f, 0.8f);
        float amount    = info.Amount * (1f - reduction);

        // 元素抗性再过一道，上限 90%（留 10% 保底，不然火抗堆满就无敌了）
        float resist = Mathf.Clamp(DamageCalculator.ResistFor(_stats, info.Element), 0f, 0.9f);
        if (resist > 0f) amount *= 1f - resist;

        // 最后一道「外部过滤」：持盾战士用它按「来向」再减一次（正面挡、背面不挡）。
        // 放在防御/抗性之后、写回 info 之前 —— 飘字看到的仍然是实际伤害。
        if (DamageFilter != null) amount = Mathf.Max(0f, DamageFilter(amount, info));

        // 写回 info：飘字、受击特效拿到的都是**实际**伤害，不是面板值
        info.Amount = amount;

        Current = Mathf.Max(0f, Current - amount);   // 防止血量变负
        _invincibilityTimer = invincibilityDuration;

        Damaged?.Invoke(info);

        if (Current <= 0f)
            Died?.Invoke();
    }

    /// <summary>
    /// 主动给一段无敌时间，用于冲刺这类「不是挨打、但要短暂无敌」的动作。
    /// 只延长、不缩短 —— 连按冲刺不会把受击后的无敌帧削掉。
    /// </summary>
    public void GrantInvincibility(float seconds)
    {
        if (seconds <= 0f) return;
        if (seconds > _invincibilityTimer) _invincibilityTimer = seconds;
    }

    /// <summary>设置最大生命值，可选保持血量百分比 —— 职业 / 装备改变上限时调用</summary>
    public void SetMaxHealth(float newMax, bool keepRatio = true)
    {
        if (newMax <= 0f) return;

        float ratio = maxHealth > 0f ? Current / maxHealth : 1f;
        maxHealth = newMax;
        Current = keepRatio ? maxHealth * ratio : Mathf.Min(Current, maxHealth);
    }

    /// <summary>
    /// 回血，返回**实际**回了多少（满血时返回 0，不是传入值）。
    /// 回血不触发 <see cref="Damaged"/>，也不吃无敌帧 —— 那是挨打才有的规则。
    /// 返回值让技能能显示「+12」而不是「+20 结果只回了一半」。
    /// </summary>
    public float Heal(float amount)
    {
        if (!IsAlive || amount <= 0f) return 0f;

        float before = Current;
        Current = Mathf.Min(maxHealth, Current + amount);
        float healed = Current - before;

        if (healed > 0f) Healed?.Invoke(healed);

        return healed;
    }

    /// <summary>回满血 / 重置</summary>
    public void ResetToFull()
    {
        Current = maxHealth;
        _invincibilityTimer = 0f;
    }
}