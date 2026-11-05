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

    // 蓄力：正在蓄的槽位（-1 = 没在蓄）+ 已经按住了多久
    private int   _chargeSlot = -1;
    private float _chargeTimer;

    // 当前启用的表现层（战士 / 法师），换职业后会自动换人
    private IPlayerAnim _anim;

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
        if (hitReaction != null && hitReaction.IsStunned)
        {
            if (_chargeSlot >= 0) CancelCharge();
            return;
        }

        UpdateCharge();

        for (int i = 0; i < MaxSlots; i++)
        {
            if (input.ConsumeSkillPressed(i)) BeginOrCast(i);
        }
    }

    // ---------------- 蓄力 ----------------

    /// <summary>正在蓄力的槽位，-1 = 没在蓄力</summary>
    public int ChargingSlot => _chargeSlot;

    public bool IsCharging => _chargeSlot >= 0;

    /// <summary>蓄力进度 0~1（火球大小、伤害倍率都按它插值）</summary>
    public float ChargeRatio
    {
        get
        {
            if (_chargeSlot < 0) return 0f;
            SkillData skill = _slots[_chargeSlot];
            if (skill == null || skill.chargeTime <= 0f) return 1f;
            return Mathf.Clamp01(_chargeTimer / skill.chargeTime);
        }
    }

    private static bool IsChargeable(SkillData skill)
        => skill != null
           && skill.castType == SkillCastType.Projectile
           && skill.chargeable
           && skill.projectilePrefab != null;

    /// <summary>
    /// 按下技能键：可蓄力的就进入蓄力，其余直接放。
    /// 特意在「开始蓄力」这一步就检查冷却和蓝量 —— 蓄了半天才发现放不出来最难受。
    /// </summary>
    private void BeginOrCast(int slot)
    {
        SkillData skill = _slots[slot];

        if (skill == null)
        {
            Log($"技能槽 {slot + 1} 是空的");
            return;
        }

        if (!IsChargeable(skill))
        {
            TryCast(slot);
            return;
        }

        if (_chargeSlot >= 0) return;          // 已经在蓄另一个键，忽略

        if (_cooldowns[slot] > 0f)
        {
            Log($"{skill.displayName} 还在冷却，还剩 {_cooldowns[slot]:0.0} 秒");
            Hint($"{skill.displayName} 冷却中 {_cooldowns[slot]:0.0}s");
            return;
        }

        if (mana != null && !mana.Has(skill.manaCost))
        {
            Log($"{skill.displayName} 法力不够（需要 {skill.manaCost:0}，当前 {mana.Current:0}）");
            Hint($"法力不足（需要 {skill.manaCost:0}）");
            return;
        }

        _chargeSlot = slot;
        _chargeTimer = 0f;

        // 上一轮松手可能没被消费掉（比如按在冷却里、或者蓄力被硬直打断后松手），
        // 那枚「已松开」的旧锁存会留在 reader 里：不在这里清掉，下一次刚按下就会被判成已松手、
        // 立刻以 0% 发射出去 —— 表现就是「有时候火球蓄不了力」。
        input.ConsumeSkillReleased(slot);

        ResolveAnim()?.PlayCharge(true);

        // 蓄力这件事在画面上很安静，Console 里留一行，出了问题好判断是「没进蓄力」还是「进了但没显示」
        Debug.Log($"[SkillCaster] {skill.displayName} 开始蓄力（按住不放，最长 {skill.chargeTime:0.00} 秒）", this);
    }

    private void UpdateCharge()
    {
        if (_chargeSlot < 0) return;

        SkillData skill = _slots[_chargeSlot];
        if (skill == null)
        {
            CancelCharge();
            return;
        }

        bool held = input.IsSkillHeld(_chargeSlot);
        bool released = input.ConsumeSkillReleased(_chargeSlot);

        // 蓄到顶就不再涨；松手（或按住期间被打断）就发出去
        if (held && !released)
        {
            _chargeTimer = Mathf.Min(_chargeTimer + Time.deltaTime, Mathf.Max(0.01f, skill.chargeTime));
            return;
        }

        ReleaseCharge();
    }

    private void ReleaseCharge()
    {
        int slot = _chargeSlot;
        SkillData skill = _slots[slot];

        float ratio = ChargeRatio;
        CancelCharge();

        if (skill == null) return;

        // 发射这一刻才扣蓝：蓄力被打断、松手时蓝不够，都不该白扣
        if (!TryPayCost(slot, skill)) return;

        CastProjectile(skill, ratio);
        Debug.Log($"[SkillCaster] {skill.displayName} 蓄力 {ratio * 100f:0}% 发射", this);
        Log($"{skill.displayName} 蓄力 {ratio * 100f:0}% 发射");
    }

    private void CancelCharge()
    {
        if (_chargeSlot < 0) return;

        _chargeSlot = -1;
        _chargeTimer = 0f;
        ResolveAnim()?.PlayCharge(false);
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

        if (!TryPayCost(slot, skill)) return;

        Cast(skill);
    }

    /// <summary>
    /// 付代价：冷却没好、蓝不够都返回 false，且**什么都没改**。
    /// 单独抽出来是因为蓄力技能要等到「松手发射」那一刻才付费，不能一按下就扣。
    /// </summary>
    private bool TryPayCost(int slot, SkillData skill)
    {
        if (skill == null) return false;

        if (_cooldowns[slot] > 0f)
        {
            Log($"{skill.displayName} 还在冷却，还剩 {_cooldowns[slot]:0.0} 秒");
            return false;
        }

        if (mana != null && !mana.TrySpend(skill.manaCost))
        {
            Log($"{skill.displayName} 法力不够（需要 {skill.manaCost:0}，当前 {mana.Current:0}）");
            return false;
        }

        _cooldowns[slot] = skill.GetCooldown(_stats);
        return true;
    }

    private void Cast(SkillData skill)
    {
        switch (skill.castType)
        {
            case SkillCastType.Melee:      CastMelee(skill);      break;
            case SkillCastType.Projectile: CastProjectile(skill); break;
            case SkillCastType.Warcry:     CastWarcry(skill);     break;
            case SkillCastType.Heal:       CastHeal(skill);       break;
        }
    }

    /// <summary>
    /// 找到当前启用的表现层。战士和法师两个组件同时存在（只有一个 enabled），
    /// 所以不能简单 GetComponent，要挑那个开着的。
    /// </summary>
    private IPlayerAnim ResolveAnim()
    {
        if (_anim != null && (_anim as MonoBehaviour) != null && ((MonoBehaviour)_anim).enabled) return _anim;

        _anim = null;

        var behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null && behaviours[i].enabled && behaviours[i] is IPlayerAnim anim)
            {
                _anim = anim;
                break;
            }
        }

        return _anim;
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
            color:        attacker.SlashColor,
            scale:        1f);

        // 动作也跟着转：两帧交替（蹲扫 / 举砍），旋风的弧光本来就在绕圈
        ResolveAnim()?.PlaySpin(skill.duration);
    }

    /// <summary>
    /// 战吼：不打伤害，从自身炸出一圈冲击波把敌人吓跑。
    /// 表现（巨剑 + 扩散圆环）和被吓到的逻辑分别在 WarCryEffect / EnemyController 里。
    /// </summary>
    private void CastWarcry(SkillData skill)
    {
        var effect = gameObject.AddComponent<WarCryEffect>();
        effect.Setup(skill, transform.position);

        ResolveAnim()?.PlayWarcry(skill.ringDuration > 0f ? skill.ringDuration : 0.45f);
    }

    /// <summary>
    /// 回复技能：举杖 → 脚下撑开法阵 → 每隔一小段时间回一次血，头顶飘绿字。
    ///
    /// 回血交给 Health.Heal，法阵的计时和飘字交给 HealCircle ——
    /// 这样「回血」这件事和「回血长什么样」是分开的，将来给敌人加治疗技能也能直接复用 HealCircle。
    /// </summary>
    private void CastHeal(SkillData skill)
    {
        var health = GetComponent<Health>();
        if (health == null)
        {
            Debug.LogWarning("[SkillCaster] 同物体上没有 Health，回血技能放不出来", this);
            return;
        }

        HealCircle.Play(
            health:      health,
            caster:      transform,
            sprite:      skill.circleSprite,
            healPerTick: skill.healAmount,
            duration:    skill.healDuration,
            tickInterval: skill.healTickInterval,
            diameter:    skill.circleDiameter,
            ringColor:   new Color(1f, 0.86f, 0.45f, 0.95f),
            numberColor: skill.healColor);

        // 举杖：施法姿态只保持前半段，后面让法阵自己转完
        ResolveAnim()?.PlayCast(Mathf.Min(skill.healDuration, 0.8f));

        Log($"{skill.displayName}：{skill.healDuration:0.0} 秒内每 {skill.healTickInterval:0.0} 秒回 {skill.healAmount:0} 点");
    }

    /// <summary>投射物技能：算好伤害 → 生成预制体 → 剩下交给 Projectile 自己飞</summary>
    /// <param name="chargeRatio">
    /// 蓄力进度 0~1。0 = 直接放（普通火球），1 = 蓄满（大球、高速、伤害翻倍）。
    /// 三个倍率都在 SkillData 上，改数值不用动代码。
    /// </param>
    private void CastProjectile(SkillData skill, float chargeRatio = 0f)
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

        float t = Mathf.Clamp01(chargeRatio);
        float sizeScale   = Mathf.Lerp(1f, skill.chargeMaxScale, t);
        float speedScale  = Mathf.Lerp(1f, skill.chargeMaxSpeedBonus, t);
        float damageScale = Mathf.Lerp(1f, skill.chargeMaxDamageBonus, t);

        // 蓄力时多发的弹也算扇形：3 发蓄满 = 三个大球一起飞
        ProjectileLauncher.Fire(
            data:             skill.attack,
            prefab:           skill.projectilePrefab,
            stats:            _stats,
            owner:            gameObject,
            origin:           ProjectileLauncher.MuzzlePosition(transform, facing),
            facing:           facing,
            count:            skill.projectileCount,
            spreadAngle:      skill.spreadAngle,
            damageMultiplier: damageScale,
            sizeScale:        sizeScale,
            speedScale:       speedScale);
    }

    private void Log(string message)
    {
        if (logFailures) Debug.Log($"[SkillCaster] {message}", this);
    }

    /// <summary>
    /// 放不出来时在头顶飘一句话。
    /// 技能被冷却 / 蓝量挡住时，画面上什么都没有，玩家只会觉得「按了没反应」—— 给个看得见的理由。
    /// </summary>
    private void Hint(string text)
    {
        FloatingText.Show(text, new Color(1f, 0.82f, 0.36f, 1f),
                          transform.position + Vector3.up * 1.15f,
                          rise: 0.7f, duration: 0.75f, fontSize: 26);
    }
}
