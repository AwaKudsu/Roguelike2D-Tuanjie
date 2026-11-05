using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家普通攻击的执行者。
///
/// 它只做三件事：读输入 → 按 AttackData 决定「怎么打」→ 把 DamageInfo 交给所有 IDamageable。
///   · AttackData 的 Is Ranged 没勾 → 在身前打一个判定框（战士的剑）
///   · AttackData 的 Is Ranged 勾上 → 发射投射物（法师的法杖弹）
///
/// 「伤害多少」「打多大范围」来自 AttackData，「攻击力多高」来自 CharacterStats。
/// 所以同一个脚本既能当剑也能当法杖，区别只在资产里 —— 将来加武器也走这条路。
/// </summary>
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerAttacker : MonoBehaviour
{
    [Header("当前使用的攻击（将来由武器 / 装备系统切换）")]
    [SerializeField] private AttackData attackData;

    [Header("调试")]
    [Tooltip("每次攻击 / 技能打出去时，在 Scene 视图闪一下判定框。\n" +
             "亮黄色 = 普通攻击；亮青色 = 技能。闪完自动消失，不常驻。")]
    [SerializeField] private bool debugShowHitbox = true;

    [Tooltip("判定框闪多久（秒）。太短来不及把视线从 Game 视图挪过来，太长会挡住画面")]
    [SerializeField] private float debugShowDuration = 0.6f;

    [Header("挥砍表现")]
    [Tooltip("近战挥砍时飞出来的刀风贴图（战士素材里的 Warrior_SlashArc）。留空就没有刀风，不影响判定")]
    [SerializeField] private Sprite slashArc;

    [SerializeField] private Color slashColor = new Color(1f, 0.94f, 0.75f, 1f);

    [Tooltip("刀风大小倍率")]
    [SerializeField] private float slashScale = 1f;

    [Header("三段连击")]
    [Tooltip("按顺序放：一段劈砍 / 二段上挑 / 三段突刺。留空就退回单段普通攻击（法师这种远程职业就是这样）")]
    [SerializeField] private AttackData[] comboAttacks = new AttackData[0];

    [Tooltip("上一段打完多久内再按才算连得上下一段；超时就回到第一段")]
    [SerializeField] private float comboWindow = 0.55f;

    /// <summary>
    /// 每次攻击真正打出去时触发（远程也在内）。
    /// 外观层靠它切姿势 —— 判定和表现分开，删掉这个事件不影响战斗。
    /// </summary>
    public event Action<AttackData> AttackStarted;

    private PlayerInputReader _input;
    private CharacterStats _stats;
    private HitReaction _hitReaction;
    private CharacterVisual _visual;
    private float _cooldownTimer;
    private int _facing = 1;

    // 连击：_comboStage 记「下一次该打第几段」，_comboTimer 是这一次连招还能接多久
    private int _comboStage;
    private float _comboTimer;

    // 一次性的判定框闪示。每次攻击/技能打出去时由 ShowFlash() 记下位置、大小和到期时间，
    // 到点就自然不画了 —— 所以它显示的是「刚才那一下实际打在哪」，按下才出现、放完就消失。
    private Vector2 _flashCenter;
    private Vector2 _flashSize;
    private float _flashUntil;
    private bool _flashIsSkill;

    /// <summary>当前朝向：1 = 右，-1 = 左。技能系统和投射物都靠它决定往哪边打</summary>
    public int Facing => _facing;

    /// <summary>当前普通攻击用的数据。换武器时改的就是它</summary>
    public AttackData CurrentAttack => attackData;

    /// <summary>刚才那一下是三段连击里的第几段（0 开始）。外观层据此选姿势</summary>
    public int ComboStage { get; private set; }

    /// <summary>配了几段连击。0 表示这个职业只有单段攻击</summary>
    public int ComboLength => comboAttacks != null ? comboAttacks.Length : 0;

    /// <summary>挥砍用的弧光精灵。技能特效（旋风斩 / 战吼）也借它，所以对外开一个只读入口</summary>
    public Sprite SlashArc => slashArc;

    /// <summary>挥砍弧光的颜色</summary>
    public Color SlashColor => slashColor;


    /// <summary>某个 AttackData 在「当前站位 + 当前朝向」下会打到哪里</summary>
    private Vector2 CenterFor(AttackData data)
    {
        return (Vector2)transform.position
             + new Vector2(data.hitboxOffset.x * _facing, data.hitboxOffset.y);
    }

    private void Awake()
    {
        _input       = GetComponent<PlayerInputReader>();
        _stats       = GetComponent<CharacterStats>();
        _hitReaction = GetComponent<HitReaction>();
        _visual      = GetComponent<CharacterVisual>();
    }

    private void Start()
    {
        // 普通攻击跟着职业走。放 Start 不放 Awake：CharacterStats 是在它自己的 Awake 里
        // 应用职业数据的，两个 Awake 谁先跑没有保证。
        ApplyClassAttack(_stats != null ? _stats.Class : null);

        if (_stats != null) _stats.ClassChanged += ApplyClassAttack;
    }

    private void OnDisable()
    {
        if (_stats != null) _stats.ClassChanged -= ApplyClassAttack;
    }

    /// <summary>
    /// 职业的普通攻击模板。没有武器覆盖时就用它。
    /// 之前这里什么都没接 —— ClassData.basicAttack 一直是个没人读的字段，
    /// 所以战士用的还是场景里手填的法师弹道。
    /// </summary>
    private void ApplyClassAttack(ClassData data)
    {
        if (data == null || data.basicAttack == null) return;
        attackData = data.basicAttack;
    }

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        // 连击窗口过期，下一刀从第一段重新开始
        if (_comboTimer > 0f)
        {
            _comboTimer -= Time.deltaTime;
            if (_comboTimer <= 0f) _comboStage = 0;
        }

        float x = _input.Move.x;
        if (Mathf.Abs(x) > 0.1f)
        {
            _facing = x > 0f ? 1 : -1;

            // 贴图朝向跟着走。没装 CharacterVisual 也不影响判定框 —— 它只管外观
            if (_visual != null) _visual.SetFacing(_facing);
        }

        if (_hitReaction != null && _hitReaction.IsStunned) return;

        if (_input.ConsumeAttackPressed() && _cooldownTimer <= 0f)
        {
            AttackData data = NextAttack();
            if (data == null) return;

            _cooldownTimer = GetCooldown(data);
            BasicAttack(data);
        }
    }

    /// <summary>
    /// 挑这一刀用哪份 AttackData：窗口内就接着下一段，否则从第一段开始。
    /// 远程职业（连击数组为空，或当前攻击是远程）直接返回普通攻击，不做连击。
    /// </summary>
    private AttackData NextAttack()
    {
        bool useCombo = comboAttacks != null && comboAttacks.Length > 0
                        && comboAttacks[0] != null
                        && (attackData == null || !attackData.isRanged);

        if (!useCombo)
        {
            ComboStage = 0;
            return attackData;
        }

        if (_comboTimer <= 0f) _comboStage = 0;

        int index = Mathf.Clamp(_comboStage, 0, comboAttacks.Length - 1);
        AttackData data = comboAttacks[index];

        ComboStage  = index;
        _comboStage = (index + 1) % comboAttacks.Length;
        _comboTimer = comboWindow;

        return data != null ? data : attackData;
    }

    /// <summary>某个 AttackData 是三段连击里的第几段；不是连击段就返回 0</summary>
    private int StageOf(AttackData data)
    {
        if (comboAttacks == null || data == null) return 0;

        for (int i = 0; i < comboAttacks.Length; i++)
            if (comboAttacks[i] == data) return i;

        return 0;
    }

    /// <summary>换武器 / 换职业时改普通攻击形态，由装备系统调用</summary>
    public void SetAttack(AttackData data) => attackData = data;

    /// <summary>实际冷却 = 基础冷却 ÷ 攻速，再乘上冷却缩减</summary>
    public float GetCooldown(AttackData data)
    {
        if (_stats == null) return data.cooldown;

        float speed     = Mathf.Max(0.1f, _stats.Get(StatType.AttackSpeed));
        float reduction = Mathf.Clamp(_stats.Get(StatType.CooldownRate), 0f, 0.8f);

        return data.cooldown / speed * (1f - reduction);
    }

    /// <summary>按数据决定这次普通攻击走哪条路</summary>
    private void BasicAttack(AttackData data)
    {
        if (data.isRanged) Fire(data);
        else               Execute(data);
    }

    /// <summary>发射投射物（远程普通攻击）</summary>
    public void Fire(AttackData data)
    {
        Vector2 muzzle = ProjectileLauncher.MuzzlePosition(transform, _facing);

        ProjectileLauncher.Fire(
            data:   data,
            prefab: data.projectilePrefab,
            stats:  _stats,
            owner:  gameObject,
            origin: muzzle,
            facing: _facing);

        // 投射物本身没有判定框，就在枪口闪一个小方块当「打出去了」的反馈
        ShowFlash(muzzle, new Vector2(0.4f, 0.4f), data != attackData);

        AttackStarted?.Invoke(data);
    }

    /// <summary>
    /// 放挥砍残影。三种情况各不相同：
    ///   · 大范围挥砍（旋风斩）—— 弧光绕身转一圈
    ///   · 三段连击 —— 扫的方向不一样（劈砍从上往下、上挑从下往上、突刺几乎不扫只往前闪）
    ///   · 其它近战 —— 平着扫一道
    /// slashArc 没配就什么都不做。
    ///
    /// 注意这里是「贴着剑扫」，不是「脱手飞出去」—— 弧光以角色为圆心旋转，
    /// 扫完立刻消失。飞出去的那种读起来像法师的气刃，不像战士挥剑。
    /// </summary>
    private void PlaySlash(Vector2 center, AttackData data)
    {
        if (slashArc == null) return;

        float scale = Mathf.Max(0.1f, slashScale);
        Vector2 origin = transform.position;

        // 弧光扫到多远，不写死 —— 直接由这份 AttackData 的判定框推出来。
        // 否则一调攻击距离，判定框变长了、弧光还停在老位置，看着像「空气把人打飞」。
        float reach = Mathf.Abs(data.hitboxOffset.x) + data.hitboxSize.x * 0.5f;

        if (data.hitboxSize.x * data.hitboxSize.y >= 6f)
        {
            SlashEffect.Spin(slashArc, origin,
                             radius: Mathf.Max(data.hitboxSize.x, data.hitboxSize.y) * 0.28f,
                             scale: scale * 0.9f,
                             color: slashColor);
            return;
        }

        switch (StageOf(data))
        {
            case 1:   // 上挑：从下往上撩，扫得比劈砍开一点
                SlashEffect.Swing(slashArc, origin, _facing, scale, slashColor,
                                  startAngle: 105f, sweep: -140f,
                                  radius: reach * 0.42f, duration: 0.22f);
                break;

            case 2:   // 突刺：不扫圈，只在身前远一点的位置闪一道
                SlashEffect.Thrust(slashArc, origin, _facing, scale * 1.05f, slashColor,
                                   distance: reach * 0.62f, duration: 0.13f);
                break;

            default:  // 劈砍：从头顶那侧往下劈
                SlashEffect.Swing(slashArc, origin, _facing, scale, slashColor,
                                  startAngle: -75f, sweep: 135f,
                                  radius: reach * 0.42f, duration: 0.18f);
                break;
        }
    }

    /// <summary>
    /// 执行一次近战判定。技能系统调用的也是这个方法 ——
    /// 战士的旋风斩就是「换一个判定框更大的 AttackData，再调一次这里」。
    /// </summary>
    public void Execute(AttackData data)
    {
        Vector2 center = CenterFor(data);

        // 先通知外观层切姿势、再放刀风，最后才判定 ——
        // 这样即使表现层出问题（比如贴图没导入），伤害也照样打出去
        AttackStarted?.Invoke(data);
        PlaySlash(center, data);

        // 按下才算、闪完就没了：这里记的是这一下**实际**打在哪
        ShowFlash(center, data.hitboxSize, data != attackData);

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, data.hitboxSize, 0f, data.targetLayers);

        var alreadyHit = new HashSet<IDamageable>();

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (!hit.TryGetComponent(out IDamageable target)) continue;
            if (!target.IsAlive) continue;
            if (!alreadyHit.Add(target)) continue;

            // 伤害公式统一在 DamageCalculator 里 —— 普通攻击和各种技能共用同一份。
            // 注意这里**没有 break**：一次挥砍可以同时打中好几个敌人（敌人的攻击才只打一个）。
            target.TakeDamage(DamageCalculator.Build(_stats, data, gameObject, transform.position));
        }
    }

    /// <summary>在指定位置闪一下判定框，debugShowDuration 秒后自动消失</summary>
    private void ShowFlash(Vector2 center, Vector2 size, bool isSkill)
    {
        _flashCenter  = center;
        _flashSize    = size;
        _flashUntil   = Time.time + debugShowDuration;
        _flashIsSkill = isSkill;
    }

    // ---------------- 调试可视化 ----------------

    private void OnDrawGizmos()
    {
        if (!debugShowHitbox) return;
        if (!Application.isPlaying) return;
        if (Time.time >= _flashUntil) return;   // 已经过期 —— 打过的那一下早就闪完了，什么都不画

        // 亮黄色 = 普通攻击；亮青色 = 技能。这样一眼就能分清刚才那下是哪个
        Color c = _flashIsSkill
            ? new Color(0.3f, 1f, 0.9f)
            : new Color(1f, 0.9f, 0.2f);

        Gizmos.color = new Color(c.r, c.g, c.b, 0.25f);
        Gizmos.DrawCube(_flashCenter, _flashSize);

        Gizmos.color = new Color(c.r, c.g, c.b, 1f);
        Gizmos.DrawWireCube(_flashCenter, _flashSize);
    }

    private void OnDrawGizmosSelected()
    {
        // 选中 Player 时额外画一条远程射击方向线（近战没有方向线可画，
        // 它的判定框由 OnDrawGizmos 里的闪框负责）
        if (!Application.isPlaying) return;
        if (attackData == null || !attackData.isRanged) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        Vector2 from = ProjectileLauncher.MuzzlePosition(transform, _facing);
        Gizmos.DrawLine(from, from + new Vector2(_facing * 2f, 0f));
    }
}
