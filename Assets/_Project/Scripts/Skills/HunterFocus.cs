using UnityEngine;

/// <summary>
/// 猎人专注：一段时间的自身增益。
///
/// 它不改任何战斗代码 —— 只是往 <see cref="CharacterStats"/> 上挂几条修饰符：
///   · AttackSpeed 走百分比加算（普通攻击冷却由 PlayerAttacker 每次现算，立刻生效）
///   · CritChance 走加算（DamageCalculator 每次攻击现读）
///   · MoveSpeed 可选（PlayerController 的 MaxSpeed 也是现读）
/// 时间到了把 Source = this 的修饰符一次全撤，数值干净回到原样。
///
/// 挂上时脚下会亮一圈淡金色光环，作为「增益正在生效」的持续视觉反馈；
/// 光环用的是技能资产里的 ringSprite，不额外占一张贴图。
/// </summary>
public class HunterFocus : MonoBehaviour
{
    [Header("光环")]
    [Tooltip("光环相对角色的高度")]
    [SerializeField] private float ringHeight = 0.05f;

    [Tooltip("光环缩放（1 = 约 1 单位直径）")]
    [SerializeField] private float ringScale = 1.7f;

    private CharacterStats _stats;
    private SkillData _skill;
    private float _timer;
    private float _total;
    private SpriteRenderer _ring;
    private float _age;

    /// <summary>增益还在生效吗（自检用）</summary>
    public bool IsActive => _timer > 0f;

    /// <summary>剩余时间（自检 / HUD 可能用）</summary>
    public float Remaining => Mathf.Max(0f, _timer);

    public void Setup(SkillData skill, CharacterStats stats)
    {
        if (skill == null) return;

        _skill = skill;
        _stats = stats;
        _total = Mathf.Max(0.1f, skill.buffDuration);
        _timer = _total;
        _age = 0f;

        ApplyModifiers();
        BuildRing();
    }

    private void ApplyModifiers()
    {
        if (_stats == null)
        {
            Debug.LogWarning("[HunterFocus] 同物体上没有 CharacterStats，增益不会生效", this);
            return;
        }

        // 重复施放时先撤掉旧的，避免「叠两层专注」把攻速堆到离谱
        _stats.RemoveAllFrom(this);

        if (_skill.buffAttackSpeed != 0f)
        {
            _stats.AddModifier(new StatModifier(
                StatType.AttackSpeed, ModifierMode.PercentAdd, _skill.buffAttackSpeed), this);
        }

        if (_skill.buffCritChance != 0f)
        {
            _stats.AddModifier(new StatModifier(
                StatType.CritChance, ModifierMode.Flat, _skill.buffCritChance), this);
        }

        if (_skill.buffMoveSpeed != 0f)
        {
            _stats.AddModifier(new StatModifier(
                StatType.MoveSpeed, ModifierMode.PercentAdd, _skill.buffMoveSpeed), this);
        }
    }

    private void BuildRing()
    {
        if (_ring != null || _skill == null || _skill.ringSprite == null) return;

        var go = new GameObject("FocusRing");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, -0.45f + ringHeight, 0f);
        go.transform.localScale = new Vector3(ringScale, ringScale * 0.55f, 1f);

        _ring = go.AddComponent<SpriteRenderer>();
        _ring.sprite = _skill.ringSprite;
        _ring.color = new Color(1f, 0.85f, 0.35f, 0.45f);
        // 脚边的持续光环画在角色下面（地板之上），不然整个身体会被染一层黄
        _ring.sortingOrder = 1;
    }

    private void Update()
    {
        if (_timer <= 0f) return;

        _timer -= Time.deltaTime;
        _age += Time.deltaTime;

        if (_ring != null)
        {
            // 呼吸 + 尾段淡出，让「还剩多久」有个粗略的读感
            float fade = Mathf.Clamp01(_timer / Mathf.Max(0.1f, _total));
            float pulse = 0.9f + Mathf.Sin(_age * 6f) * 0.08f;

            Color c = _ring.color;
            c.a = 0.45f * Mathf.Min(1f, fade * 2f);
            _ring.color = c;

            _ring.transform.localScale = new Vector3(
                ringScale * pulse, ringScale * 0.55f * pulse, 1f);
        }

        if (_timer <= 0f)
        {
            RemoveModifiers();
            DestroyRing();
            Destroy(this);
        }
    }

    /// <summary>
    /// 立刻结束（换房间时由 SkillCaster.ResetRoomState 调）。
    /// 它和 Update 到点的区别只有一点：这里不等自然到期，修饰符当场撤掉。
    /// </summary>
    public void StopNow()
    {
        _timer = 0f;
        RemoveModifiers();
        DestroyRing();
        Destroy(this);
    }

    private void RemoveModifiers()
    {
        if (_stats != null) _stats.RemoveAllFrom(this);
    }

    private void DestroyRing()
    {
        if (_ring == null) return;

#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(_ring.gameObject);
        else Destroy(_ring.gameObject);
#else
        Destroy(_ring.gameObject);
#endif

        _ring = null;
    }

    private void OnDisable()
    {
        // 玩家死亡 / 换场景 / 组件被移除时，修饰符必须跟着走，否则会永久赖在属性表里
        RemoveModifiers();
    }

    private void OnDestroy()
    {
        RemoveModifiers();
    }
}
