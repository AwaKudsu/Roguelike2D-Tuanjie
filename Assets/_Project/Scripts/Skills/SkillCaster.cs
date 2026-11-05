using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能施放器：管「什么时候能放」和「放出来是什么」。
///
/// 它不碰任何伤害公式 ——
///   近战技能直接交给 MeleeAttacker.Execute()，和普通攻击走完全同一套判定；
///   投射物技能把 DamageCalculator 算好的 DamageInfo 塞进 Projectile。
///
/// 所以「每个职业 3 个技能」将来只是多几个资产文件，这个脚本一行都不用改。
/// </summary>
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(CharacterStats))]
public class SkillCaster : MonoBehaviour
{
    /// <summary>技能槽数量，对应 K / L / U / I 四个键</summary>
    public const int MaxSlots = 4;

    [Header("初始技能（职业配了技能就会覆盖它）")]
    [Tooltip("按顺序对应 K / L / U / I。留空的位置按了不会有反应")]
    [SerializeField] private List<SkillData> startingSkills = new();

    [Header("依赖（留空会自动找同物体上的组件）")]
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private ManaPool mana;
    [SerializeField] private PlayerAttacker attacker;
    [SerializeField] private HitReaction hitReaction;

    [Header("调试")]
    [Tooltip("勾上后，技能放不出来时会在 Console 说明原因（蓝不够 / 冷却中 / 没配技能）")]
    [SerializeField] private bool logFailures = false;

    private CharacterStats _stats;

    private readonly SkillData[] _slots     = new SkillData[MaxSlots];
    private readonly float[]     _cooldowns = new float[MaxSlots];

    private void Awake()
    {
        _stats = GetComponent<CharacterStats>();

        if (input       == null) input       = GetComponent<PlayerInputReader>();
        if (mana        == null) mana        = GetComponent<ManaPool>();
        if (attacker    == null) attacker    = GetComponent<PlayerAttacker>();
        if (hitReaction == null) hitReaction = GetComponent<HitReaction>();

        // 先用 Inspector 里拖的，方便不开职业也能单独测某个技能
        ApplySkills(startingSkills);
    }

    private void Start()
    {
        // 职业的初始技能在 Start 里应用：Awake 阶段 CharacterStats 的 Class 可能还没就位
        if (_stats != null && _stats.Class != null && _stats.Class.startingSkills.Count > 0)
            ApplySkills(_stats.Class.startingSkills);
    }

    private void OnEnable()
    {
        if (_stats != null) _stats.ClassChanged += OnClassChanged;
    }

    private void OnDisable()
    {
        if (_stats != null) _stats.ClassChanged -= OnClassChanged;
    }

    private void Update()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_cooldowns[i] > 0f) _cooldowns[i] -= Time.deltaTime;
        }

        if (input == null) return;

        // 硬直期间放不出技能 —— 和移动、普通攻击用同一个判断，手感才一致
        if (hitReaction != null && hitReaction.IsStunned) return;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (input.ConsumeSkillPressed(i)) TryCast(i);
        }
    }

    // ---------------- 查询接口（技能栏 UI 用） ----------------

    public SkillData GetSkill(int slot)
        => (slot >= 0 && slot < MaxSlots) ? _slots[slot] : null;

    /// <summary>冷却剩余比例：1 = 刚放完，0 = 好了。技能图标画径向遮罩用它</summary>
    public float GetCooldownRatio(int slot)
    {
        if (slot < 0 || slot >= MaxSlots) return 0f;

        SkillData skill = _slots[slot];
        if (skill == null) return 0f;

        float total = skill.GetCooldown(_stats);
        if (total <= 0f) return 0f;

        return Mathf.Clamp01(_cooldowns[slot] / total);
    }

    /// <summary>还剩几秒冷却，0 = 好了</summary>
    public float GetCooldownRemaining(int slot)
        => (slot >= 0 && slot < MaxSlots) ? Mathf.Max(0f, _cooldowns[slot]) : 0f;

    /// <summary>能不能放：有技能 + 冷却好了 + 法力够</summary>
    public bool IsReady(int slot)
    {
        if (slot < 0 || slot >= MaxSlots) return false;
        if (_slots[slot] == null) return false;
        if (_cooldowns[slot] > 0f) return false;

        return _slots[slot].CanAfford(mana);
    }

    // ---------------- 技能装配 ----------------

    /// <summary>整体替换技能栏。换职业、学新技能、技能树加点时调它</summary>
    public void ApplySkills(List<SkillData> skills)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            _slots[i]     = (skills != null && i < skills.Count) ? skills[i] : null;
            _cooldowns[i] = 0f;
        }
    }

    private void OnClassChanged(ClassData data)
        => ApplySkills(data != null ? data.startingSkills : null);

    // ---------------- 施放 ----------------

    private void TryCast(int slot)
    {
        SkillData skill = _slots[slot];

        if (skill == null)
        {
            Log($"技能槽 {slot + 1} 是空的");
            return;
        }

        if (_cooldowns[slot] > 0f)
        {
            Log($"{skill.displayName} 还在冷却，还剩 {_cooldowns[slot]:0.0} 秒");
            return;
        }

        // 先扣蓝，扣不动就整个作废 —— 不会出现「技能没放出来，却进了冷却」
        if (mana != null && !mana.TrySpend(skill.manaCost))
        {
            Log($"{skill.displayName} 法力不够（需要 {skill.manaCost:0}，当前 {mana.Current:0}）");
            return;
        }

        _cooldowns[slot] = skill.GetCooldown(_stats);
        Cast(skill);
    }

    private void Cast(SkillData skill)
    {
        switch (skill.castType)
        {
            case SkillCastType.Melee:      CastMelee(skill);      break;
            case SkillCastType.Projectile: CastProjectile(skill); break;
            case SkillCastType.Warcry:     CastWarcry(skill);     break;
        }
    }

    /// <summary>
    /// 近战技能：完全复用普通攻击的判定代码，不重写一遍。
    ///
    /// 分两种：
    ///   · duration ≤ 0 —— 打一下就结束（火球式的瞬发近战）
    ///   · duration &gt; 0 —— 挂一个 WhirlwindCast，在 duration 秒里按 tickInterval 反复打。
    ///     旋风斩就是这一种：它不是一个「大判定框」，而是「一段时间里一直在转」，
    ///     所以敌人后走进圈里也会被扫到 —— 这才是旋风斩该有的手感。
    /// </summary>
    private void CastMelee(SkillData skill)
    {
        if (skill.attack == null)
        {
            Debug.LogWarning($"[SkillCaster]「{skill.displayName}」没有填 Attack Data，什么都没打出去", this);
            return;
        }

        if (attacker == null)
        {
            Debug.LogWarning("[SkillCaster] 同物体上没有 PlayerAttacker，近战技能放不出来", this);
            return;
        }

        if (skill.duration <= 0f)
        {
            attacker.Execute(skill.attack);
            return;
        }

        var spin = GetComponent<WhirlwindCast>();
        if (spin == null) spin = gameObject.AddComponent<WhirlwindCast>();

        spin.Setup(
            attacker:     attacker,
            attack:       skill.attack,
            duration:     skill.duration,
            tickInterval: skill.tickInterval,
            arc:          attacker.SlashArc,
            color:        attacker.SlashColor,
            scale:        1f);

        // 动作也跟着转：两帧交替（蹲扫 / 举砍），旋风的弧光本来就在绕圈
        var visual = GetComponent<WarriorVisual>();
        if (visual != null) visual.PlaySpin(skill.duration);
    }

    /// <summary>
    /// 战吼：不打伤害，从自身炸出一圈冲击波把敌人吓跑。
    /// 表现（巨剑 + 扩散圆环）和被吓到的逻辑分别在 WarCryEffect / EnemyController 里。
    /// </summary>
    private void CastWarcry(SkillData skill)
    {
        var effect = gameObject.AddComponent<WarCryEffect>();
        effect.Setup(skill, transform.position);

        var visual = GetComponent<WarriorVisual>();
        if (visual != null) visual.PlayWarcry(skill.ringDuration > 0f ? skill.ringDuration : 0.45f);
    }

    /// <summary>投射物技能：算好伤害 → 生成预制体 → 剩下交给 Projectile 自己飞</summary>
    private void CastProjectile(SkillData skill)
    {
        if (skill.attack == null)
        {
            Debug.LogWarning($"[SkillCaster]「{skill.displayName}」没有填 Attack Data", this);
            return;
        }

        if (skill.projectilePrefab == null)
        {
            Debug.LogWarning($"[SkillCaster]「{skill.displayName}」没有填 Projectile Prefab", this);
            return;
        }

        int facing = attacker != null ? attacker.Facing : 1;

        // 具体怎么发射（方向、扇形、生成预制体、算伤害）全在 ProjectileLauncher 里 ——
        // 普通攻击的远程形态走的是同一份代码
        ProjectileLauncher.Fire(
            data:        skill.attack,
            prefab:      skill.projectilePrefab,
            stats:       _stats,
            owner:       gameObject,
            origin:      ProjectileLauncher.MuzzlePosition(transform, facing),
            facing:      facing,
            count:       skill.projectileCount,
            spreadAngle: skill.spreadAngle);
    }

    private void Log(string message)
    {
        if (logFailures) Debug.Log($"[SkillCaster] {message}", this);
    }
}
