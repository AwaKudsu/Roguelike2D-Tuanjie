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

    /// <summary>回满血 / 重置</summary>
    public void ResetToFull()
    {
        Current = maxHealth;
        _invincibilityTimer = 0f;
    }
}