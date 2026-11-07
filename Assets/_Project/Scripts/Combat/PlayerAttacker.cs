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
    [Tooltip("刀光的颜色。刀光不是一张贴图 —— 它是剑扫过的轨迹，由 SwordTrail 现画")]
    [SerializeField] private Color slashColor = new Color(1f, 0.94f, 0.75f, 1f);

    [Tooltip("刀光大小倍率（1 = 按默认剑长）")]
    [SerializeField] private float slashScale = 1f;

    [Header("命中火花")]
    [Tooltip("每次真打中敌人时在命中点崩一下的图（留空就没有火花）")]
    [SerializeField] private Sprite hitSparkSprite;

    [Tooltip("火花的基础大小，1 = 1 个世界单位")]
    [SerializeField] private float hitSparkScale = 0.85f;

    [Header("刀光弧")]
    [Tooltip("挥砍起手那一瞬间贴上去的月牙弧。留空就只有网格刀光")]
    [SerializeField] private Sprite slashArcSprite;

    [Tooltip("要不要那道月牙。关掉的理由很实际：它是一张贴图，挥砍方向一换就得跟着转，" +
             "角度算偏一点整道弧就歪到人下面去了。网格刀光本身已经能表达挥砍")]
    [SerializeField] private bool useSlashArc;

    [Header("贴图版刀光")]
    [Tooltip("一张「扇环」贴图（FX_SlashSweep）。绕着自己的圆心转过去就是一次挥砍，" +
             "比网格刀光更干净、也不会有采样稀疏摊成厚板的问题。留空就退回网格")]
    [SerializeField] private Sprite slashSweepSprite;

    [Tooltip("勾上就走贴图版；把它关掉可以对比网格版的效果")]
    [SerializeField] private bool useSpriteSlash = true;

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

    /// <summary>刀光的颜色。技能（旋风斩 / 战吼）也用同一个色，所以对外开一个只读入口</summary>
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

        // 按下就出一刀；按着不放也会在冷却到点后接着出下一刀（长按一路打完三段连击）。
        // 只认「按下」的话，玩家得连点三下才能打完整套。
        bool attackPressed = _input.ConsumeAttackPressed();
        bool attackHeld = _input.AttackHeld;

        if ((attackPressed || attackHeld) && _cooldownTimer <= 0f)
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

    /// <summary>
    /// 换房间时清掉「半途的出手状态」：连击段数归零、冷却清零。
    ///
    /// 不这么做的话，上一间房最后那一下的连击会带进来 ——
    /// 进门第一刀直接从第二段/第三段开始，等于白送一段连招。
    /// 冷却也一起清：房间切换本身有淡入淡出，不需要玩家再等一遍冷却。
    /// </summary>
    public void CancelPendingAttack()
    {
        _comboStage = 0;
        _comboTimer = 0f;
        _cooldownTimer = 0f;
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
    /// 放刀光。四种挥法扫的方向和速度都不一样：
    ///   · 劈砍 —— 从头顶斜着往下劈
    ///   · 上挑 —— 从身前下方撩到头顶
    ///   · 突刺 —— 几乎不扫，只往前拉一道窄痕
    ///   · 旋风斩（判定框面积 ≥ 6）—— 绕身一圈零 90°
    ///
    /// 刀光不是贴图：SwordTrail 每帧记下剑根与剑尖的位置，把连续几帧连成一条带子，
    /// 越旧的顶点越透明。所以它是「剑扫过的地方留下的余晖」，会跟着剑走，
    /// 而不是一张画好的新月贴图在人身上转。
    /// </summary>
    private void PlaySlash(Vector2 center, AttackData data)
    {
        float scale = Mathf.Max(0.1f, slashScale);
        Vector2 origin = transform.position;

        SwordSwing swing;

        if (data.hitboxSize.x * data.hitboxSize.y >= 6f)
        {
            swing = SwordSwing.Spin;
        }
        else
        {
            switch (StageOf(data))
            {
                case 1:  swing = SwordSwing.Lift;   break;
                case 2:  swing = SwordSwing.Thrust; break;
                default: swing = SwordSwing.Slash;  break;
            }
        }

        // 刀光：优先用贴图（一张扇环转过去），没配图/关掉了才退回网格。
        // 网格版在旋风斩这种整圈技能上会首尾相接，所以那条路径本来就跳过 Spin。
        SlashArcFx arc = useSpriteSlash
            ? SlashArcFx.Play(swing, transform, origin, _facing, slashColor, scale, slashSweepSprite)
            : null;

        if (arc == null && swing != SwordSwing.Spin)
            SwordTrail.Play(swing, transform, origin, _facing, slashColor, scale);

        // 起手那一瞬间的月牙。默认关掉：贴图弧的角度只要偏一点就会歪到人下面，
        // 反而不如网格刀光干净（字段 useSlashArc 留着，想要的话在 Inspector 勾上）
        if (useSlashArc && slashArcSprite != null)
            SwordTrail.FlashArc(swing, transform, origin, _facing, ArcColorFor(swing), scale, slashArcSprite);
    }

    /// <summary>
    /// 每一段连击的弧光染色。三段都一样白的话，连击在画面上完全分不出递进。
    /// </summary>
    private static Color ArcColorFor(SwordSwing swing)
    {
        switch (swing)
        {
            case SwordSwing.Lift:   return new Color(0.80f, 0.93f, 1f, 1f);     // 上挑：冷蓝白
            case SwordSwing.Thrust: return new Color(1f, 0.86f, 0.72f, 1f);     // 突刺：暖橙白
            case SwordSwing.Spin:   return new Color(1f, 0.93f, 0.74f, 1f);     // 旋风斩：金色
            default:                return new Color(1f, 0.98f, 0.94f, 1f);     // 劈砍：近白
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
        Vector2? firstHitPoint = null;

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (!hit.TryGetComponent(out IDamageable target)) continue;
            if (!target.IsAlive) continue;
            if (!alreadyHit.Add(target)) continue;

            // 伤害公式统一在 DamageCalculator 里 —— 普通攻击和各种技能共用同一份。
            // 注意这里**没有 break**：一次挥砍可以同时打中好几个敌人（敌人的攻击才只打一个）。
            target.TakeDamage(DamageCalculator.Build(_stats, data, gameObject, transform.position));

            if (firstHitPoint == null)
            {
                firstHitPoint = hit.bounds.ClosestPoint(transform.position);
            }
        }

        // 命中火花：只在这一下**真的打到人**的时候放。
        // 放在判定循环之后是因为要先知道打没打中 —— 空挥不冒火花，
        // 玩家才能从「有没有火花」一眼看出这一下是打到了还是砍在空气里。
        if (firstHitPoint.HasValue && hitSparkSprite != null)
        {
            Vector2 sparkAt = firstHitPoint.Value + UnityEngine.Random.insideUnitCircle * 0.08f;

            FxPuff.Spawn(
                hitSparkSprite,
                sparkAt,
                Color.white,
                life: 0.16f,
                startScale: hitSparkScale * 0.55f,
                endScale: hitSparkScale * 1.35f,
                spin: UnityEngine.Random.Range(-90f, 90f),
                sortingOrder: SwordTrail.SortingOrder + 1);
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
