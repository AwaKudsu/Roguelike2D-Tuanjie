using UnityEngine;

/// <summary>
/// 敌人的统一控制器：一套骨架，六种打法。
///
/// ★ 为什么不再「每种怪一个脚本」：
///   六种怪里真正不同的只有「怎么移动」和「怎么打人」两件事，
///   感知、索敌、丢失目标、受击硬直、恐慌、死亡这些是一样的。
///   拆成六个类会把同一套骨架抄六遍，改一个 bug 要改六个地方。
///
/// ★ 所以这里是一个 switch：<see cref="EnemyConfig.behavior"/> 决定走哪条分支，
///   每条分支只做「这一帧往哪走、要不要出手」，其余全在共用代码里。
///
/// ★ config 留空时退化成旧版「看见→走近→贴脸砍」，老房间预制体不用改。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyController : MonoBehaviour
{
    [Header("行为配置（留空 = 走近就砍的老行为）")]
    [SerializeField] private EnemyConfig config;

    [Header("感知（config 为空时用）")]
    [Tooltip("发现玩家的距离")]
    [SerializeField] private float detectRange = 9f;

    [Tooltip("走进这个距离就开始攻击")]
    [SerializeField] private float attackRange = 1.3f;

    [Tooltip("⚠️ 玩家所在的层，必须选 Player")]
    [SerializeField] private LayerMask playerLayer;

    [Tooltip("超出这个距离就放弃追击（比 detectRange 大，避免在边界反复横跳）")]
    [SerializeField] private float loseTargetRange = 14f;

    [Header("移动")]
    [SerializeField] private float acceleration = 30f;

    [Header("攻击（config 为空时用）")]
    [SerializeField] private AttackData attackData;

    [Header("死亡")]
    [SerializeField] private float deathDelay = 0.3f;

    [Header("恐慌（被战吼吓到）")]
    [Tooltip("恐慌时的移速倍率。恐慌就是「不管不顾地跑」，所以要比平时快")]
    [SerializeField] private float fearSpeedMultiplier = 2.2f;

    [Tooltip("一次恐慌里「闷头跑」那一小步要跑多久")]
    [SerializeField] private Vector2 fearRunStep = new Vector2(0.35f, 0.60f);

    [Tooltip("一次恐慌里「慌不择路掉头两步」那一小步要跑多久")]
    [SerializeField] private Vector2 fearBackStep = new Vector2(0.10f, 0.20f);

    // ---------------- 组件 ----------------
    private Rigidbody2D _rb;
    private CharacterStats _stats;
    private Health _health;
    private HitReaction _hitReaction;
    private Transform _target;
    private CharacterVisual _visual;

    // ---------------- 共用状态 ----------------
    private int _facing = 1;
    private float _attackTimer;     // 距离下次能出手还有多久
    private bool _dead;

    // 近战 / 盾牌
    private float _windupTimer;     // >0 表示正在抬手
    private bool _windupArmed;
    private float _chargeTimer;     // 冲撞剩余时间
    private int _chargeDir;

    // 远程
    private bool _charging;         // 正在吟唱
    private float _chargeTimer2;    // 吟唱已积累的时间

    // 跳跃
    private int _jumpPhase;         // 0 趴着, 1 下蹲, 2 空中, 3 刚落地
    private float _jumpT;
    private Vector2 _jumpFrom;
    private Vector2 _jumpTo;
    private float _jumpRestTimer;

    // 飞行
    private bool _diving;
    private float _diveTimer;
    private Vector2 _diveDir;

    // 一次动作是否已经打到人（防止冲撞/俯冲每帧重复结算）
    private bool _actionHitDone;

    // 恐慌状态
    private float _fearTimer;
    private float _fearStepTimer;
    private int _fearStepDir;
    private Vector2 _fearSourcePosition;
    private bool _hasFearSource;

    // ---------------- 动画 ----------------
    private SpriteRenderer _sprite;
    private Sprite[] _frames;
    private float _fps = 8f;
    private bool _loopFrames = true;
    private bool _animLocked;      // 动作帧没播完之前，不许被 idle/move 顶掉
    private float _frameTimer;
    private int _frameIndex;

    private void Awake()
    {
        _rb          = GetComponent<Rigidbody2D>();
        _stats       = GetComponent<CharacterStats>();
        _health      = GetComponent<Health>();
        _hitReaction = GetComponent<HitReaction>();
        _visual      = GetComponent<CharacterVisual>();
        _sprite      = GetComponentInChildren<SpriteRenderer>();

        // 跳跳和飞行不归重力管：它们的位移是自己算的抛物线 / 悬停
        if (config != null && (config.behavior == EnemyBehavior.Jumper || config.behavior == EnemyBehavior.Flyer))
        {
            _rb.gravityScale = 0f;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // 持盾的正面减伤要靠「挨打时看点从哪来」，挂在 Health 的过滤器上
        if (config != null && config.behavior == EnemyBehavior.Shielder && _health != null)
            _health.DamageFilter = FilterShield;

        if (config != null) PlayIdle();
    }

    private void OnEnable()
    {
        _health.Died += OnDied;
        _health.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        _health.Died -= OnDied;
        _health.Damaged -= OnDamaged;
    }

    /// <summary>挨打时切受击帧。锁住动画，别让下一帧的 idle 把它顶掉。</summary>
    private void OnDamaged(DamageInfo info)
    {
        if (config == null || _dead) return;
        PlayAction(config.hurtFrames, config.hurtFps);
    }

    private void OnDestroy()
    {
        if (_health != null && config != null && config.behavior == EnemyBehavior.Shielder)
            _health.DamageFilter = null;
    }

    private void Update()
    {
        AdvanceAnimation(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (_attackTimer > 0f)  _attackTimer -= Time.fixedDeltaTime;
        if (_windupTimer > 0f)  _windupTimer -= Time.fixedDeltaTime;

        if (_dead || !_health.IsAlive) return;

        // 受击硬直期间不移动也不攻击 —— 否则敌人会顶着击退往前走，打击感全无
        if (_hitReaction != null && _hitReaction.IsStunned) return;

        if (_fearTimer > 0f)
        {
            FearUpdate();
            return;
        }

        AcquireTarget();

        if (_target == null)
        {
            Brake();
            if (config != null) PlayIdle();
            return;
        }

        float distance = Vector2.Distance(transform.position, _target.position);

        if (distance > LoseRangeNow)
        {
            _target = null;
            Brake();
            return;
        }

        int dir = _target.position.x > transform.position.x ? 1 : -1;
        _facing = dir;
        if (_visual != null) _visual.SetFacing(dir);
        if (_sprite != null) _sprite.flipX = _facing < 0;

        switch (config != null ? config.behavior : EnemyBehavior.Melee)
        {
            case EnemyBehavior.Melee:    UpdateMelee(distance, dir);    break;
            case EnemyBehavior.Archer:   UpdateArcher(distance, dir);   break;
            case EnemyBehavior.Caster:   UpdateCaster(distance, dir);   break;
            case EnemyBehavior.Jumper:   UpdateJumper(distance, dir);   break;
            case EnemyBehavior.Flyer:    UpdateFlyer(distance, dir);    break;
            case EnemyBehavior.Shielder: UpdateShielder(distance, dir); break;
        }
    }

    // ---------------- 参数取值 ----------------

    private float LoseRangeNow => config != null ? config.loseTargetRange : loseTargetRange;

    private float AttackRangeNow => config != null ? config.attackRange : attackRange;

    private float MoveSpeed => _stats != null ? _stats.Get(StatType.MoveSpeed) : 3f;

    private float AttackPower => _stats != null ? _stats.Get(StatType.Attack) : 5f;

    private AttackData MeleeAttack => config != null && config.meleeAttack != null ? config.meleeAttack : attackData;

    private float MeleeCooldown => config != null ? config.meleeCooldown : (attackData != null ? attackData.cooldown : 1.2f);

    // ---------------- 五种远程/近战打法 ----------------

    /// <summary>哥布林：走过去，抬手，砍一刀。</summary>
    private void UpdateMelee(float distance, int dir)
    {
        if (distance <= AttackRangeNow)
        {
            Brake();
            if (_windupTimer <= 0f && _attackTimer > 0f) return;

            if (_attackTimer <= 0f)
            {
                if (!_windupArmed)
                {
                    // 抬手：站着不动，给玩家一个反应窗口
                    _windupArmed = true;
                    _windupTimer = config != null ? config.meleeWindup : 0.2f;
                    PlayAction(config != null ? config.attackFrames : null, config != null ? config.attackFps : 12f);
                    return;
                }
            }

            if (_windupArmed && _windupTimer <= 0f)
            {
                _windupArmed = false;
                _attackTimer = MeleeCooldown;
                SwingMelee();
            }
            return;
        }

        _windupArmed = false;
        MoveTowards(dir, MoveSpeed);
        PlayMove();
    }

    /// <summary>弓箭手：拉弓 → 放箭；被贴近就后撤。</summary>
    private void UpdateArcher(float distance, int dir)
    {
        float keepOut = config != null ? config.retreatDistance : 2.5f;
        float shootAt = config != null ? config.rangedRange : 7f;

        if (distance < keepOut)
        {
            // 太近了：边退边保持朝向玩家（风筝）
            MoveTowards(-dir, MoveSpeed);
            if (_charging) _charging = false;    // 被贴脸，弓放下
            PlayMove();
            return;
        }

        if (distance > shootAt)
        {
            MoveTowards(dir, MoveSpeed);
            PlayMove();
            return;
        }

        Brake();
        if (_attackTimer > 0f) return;

        // 吟唱：抬手时间内不移动
        if (!_charging)
        {
            _charging = true;
            _chargeTimer2 = 0f;
            PlayAction(config != null ? config.attackFrames : null, config != null ? config.attackFps : 10f);
        }
        else
        {
            _chargeTimer2 += Time.fixedDeltaTime;
            if (_chargeTimer2 >= (config != null ? config.chargeTime : 0.4f))
            {
                _charging = false;
                _attackTimer = config != null ? config.rangedCooldown : 2f;
                FireProjectile(dir);
            }
        }
    }

    /// <summary>火焰法师：站定吟唱，火球出手后有一段后摇。</summary>
    private void UpdateCaster(float distance, int dir)
    {
        float keepOut = config != null ? config.retreatDistance : 2.5f;
        float shootAt = config != null ? config.rangedRange : 7f;

        if (distance < keepOut)
        {
            MoveTowards(-dir, MoveSpeed * 0.8f);
            if (_charging) { _charging = false; }
            PlayMove();
            return;
        }

        if (distance > shootAt)
        {
            MoveTowards(dir, MoveSpeed);
            PlayMove();
            return;
        }

        // 到位之后就不动了：宁可站桩也要把火球放出来
        Brake();
        if (_attackTimer > 0f) return;

        if (!_charging)
        {
            _charging = true;
            _chargeTimer2 = 0f;
            PlayAction(config != null ? config.attackFrames : null, config != null ? config.attackFps : 8f);
        }
        else
        {
            _chargeTimer2 += Time.fixedDeltaTime;
            if (_chargeTimer2 >= (config != null ? config.chargeTime : 0.9f))
            {
                _charging = false;
                _attackTimer = config != null ? config.rangedCooldown : 2.6f;
                FireProjectile(dir);
            }
        }
    }

    /// <summary>史莱姆：趴一会 → 下蹲 → 跳到玩家落点 → 落地砸一下 → 趴一会。</summary>
    /// <summary>
    /// 只刹横向速度，纵向交给重力。
    /// ★ 史莱姆「在空中飞」就是这里：回气/下蹲时如果连纵向速度一起清零，
    ///   重力每帧被清掉，它就永远停在出生高度（出生点是 1.1，看着就像飘在半空）。
    /// </summary>
    private void BrakeHorizontal()
    {
        // 横向刹住，纵向**主动给一个向下的速度**。
        // 只把 x 清零是不够的：外层的追击/待机逻辑每帧也会写 velocity，
        // 把 y 分量一起清掉，重力就永远攒不起来 —— 史莱姆会一直停在出生高度。
        _rb.velocity = new Vector2(0f, Mathf.Min(_rb.velocity.y, -2f));
    }

    private void UpdateJumper(float distance, int dir)
    {
        float duration = config != null ? config.jumpDuration : 0.55f;
        float height   = config != null ? config.jumpHeight : 1.5f;

        switch (_jumpPhase)
        {
            case 0:   // 趴着回气
                BrakeHorizontal();
                PlayIdle();
                _jumpRestTimer -= Time.fixedDeltaTime;
                if (_jumpRestTimer <= 0f) { _jumpPhase = 1; _jumpT = 0f; }
                break;

            case 1:   // 下蹲蓄力（唯一预警）
                BrakeHorizontal();
                _jumpT += Time.fixedDeltaTime;
                if (_jumpT >= 0.28f)
                {
                    _jumpPhase = 2;
                    _jumpT = 0f;
                    _actionHitDone = false;
                    _jumpFrom = transform.position;

                    // 落点在玩家脚下，但不超过自己的跳跃距离
                    float side = _target.position.x >= transform.position.x ? 1f : -1f;
                    float span = Mathf.Min(config != null ? config.jumpDistance : 3.5f,
                                           Mathf.Abs(_target.position.x - transform.position.x) + 0.6f);
                    _jumpTo = new Vector2(transform.position.x + side * span, transform.position.y);
                }
                break;

            case 2:   // 空中：抛物线
                _jumpT += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(_jumpT / Mathf.Max(0.05f, duration));
                Vector2 flat = Vector2.Lerp(_jumpFrom, _jumpTo, t);
                float arc = Mathf.Sin(t * Mathf.PI) * height;
                _rb.MovePosition(new Vector2(flat.x, flat.y + arc));
                if (t >= 1f) { _jumpPhase = 3; _jumpT = 0f; }
                break;

            case 3:   // 落地：砸一下，然后回去趴着
                if (!_actionHitDone)
                {
                    _actionHitDone = true;
                    float radius = config != null ? config.landRadius : 1.1f;
                    float damage = config != null ? config.landDamage : 8f;
                    HitCircle(transform.position, radius, damage, 0f);
                }
                _jumpRestTimer = config != null ? config.landRest : 0.9f;
                _jumpPhase = 0;
                break;
        }
    }

    /// <summary>蝙蝠：悬停在玩家侧上方转，隔一会儿俯冲咬一口。</summary>
    private void UpdateFlyer(float distance, int dir)
    {
        float hoverDistance = config != null ? config.hoverDistance : 2.8f;
        float hoverHeight   = config != null ? config.hoverHeight : 1.4f;
        float diveSpeed     = config != null ? config.diveSpeed : 10f;

        if (_diving)
        {
            _diveTimer -= Time.fixedDeltaTime;
            _rb.velocity = _diveDir * diveSpeed;
            if (!_actionHitDone && HitCircle(transform.position, 0.6f, config != null ? config.diveDamage : 7f, 0f))
                _actionHitDone = true;

            if (_diveTimer <= 0f)
            {
                _diving = false;
                _actionHitDone = false;
                _attackTimer = config != null ? config.diveCooldown : 1.8f;
            }
            return;
        }

        // 悬停：绕着玩家保持一个固定在侧上方的位置
        Vector2 anchor = (Vector2)_target.position
                       + new Vector2(-dir * hoverDistance, hoverHeight);
        Vector2 delta = anchor - (Vector2)transform.position;
        _rb.velocity = Vector2.MoveTowards(_rb.velocity, delta * 3f, acceleration * Time.fixedDeltaTime);
        PlayMove();

        if (_attackTimer <= 0f && distance < hoverDistance + 1.5f)
        {
            _diving = true;
            _diveTimer = config != null ? config.diveDuration : 0.45f;
            _actionHitDone = false;
            Vector2 to = ((Vector2)_target.position - (Vector2)transform.position).normalized;
            _diveDir = to == Vector2.zero ? new Vector2(_facing, 0f) : to;
            PlayAction(config != null ? config.attackFrames : null, config != null ? config.attackFps : 14f);
        }
    }

    /// <summary>持盾战士：慢慢逼近 → 蓄势 → 直线冲撞 → 硬直。</summary>
    private void UpdateShielder(float distance, int dir)
    {
        if (_chargeTimer > 0f)
        {
            _chargeTimer -= Time.fixedDeltaTime;
            _rb.velocity = new Vector2(_chargeDir * (config != null ? config.chargeSpeed : 8f), _rb.velocity.y);

            if (!_actionHitDone && HitCircle(transform.position + new Vector3(0.6f * _chargeDir, 0f), 0.8f,
                                             config != null ? config.chargeDamage : 12f, 0f))
                _actionHitDone = true;

            if (_chargeTimer <= 0f) _attackTimer = config != null ? config.chargeCooldown : 3f;
            return;
        }

        if (_windupArmed)
        {
            Brake();
            if (_windupTimer <= 0f)
            {
                _windupArmed = false;
                _chargeTimer = config != null ? config.chargeDuration : 0.6f;
                _chargeDir = _facing;
                _actionHitDone = false;
            }
            return;
        }

        float trigger = config != null ? config.chargeTriggerRange : 5f;

        if (_attackTimer <= 0f && distance <= trigger && distance > AttackRangeNow)
        {
            // 蓄势：盾压低，玩家这时候可以侧闪
            _windupArmed = true;
            _windupTimer = config != null ? config.chargeWindup : 0.55f;
            Brake();
            PlayAction(config != null ? config.attackFrames : null, config != null ? config.attackFps : 10f);
            return;
        }

        if (distance <= AttackRangeNow)
        {
            Brake();
            if (_windupTimer <= 0f && _attackTimer <= 0f)
            {
                _attackTimer = MeleeCooldown;
                SwingMelee();
            }
            return;
        }

        bool shieldUp = distance < trigger;          // 靠近时举盾，走得慢
        MoveTowards(dir, MoveSpeed * (shieldUp ? 0.6f : 1f));
        PlayMove();
    }

    // ---------------- 出手 ----------------

    /// <summary>近战判定框：用 AttackData 里的尺寸，没有就用默认小方块</summary>
    private void SwingMelee()
    {
        AttackData data = MeleeAttack;
        if (data == null) return;

        Vector2 center = (Vector2)transform.position
                       + new Vector2(data.hitboxOffset.x * _facing, data.hitboxOffset.y);

        HitBox(center, data.hitboxSize, AttackPower * data.damageMultiplier, data.knockbackForce);
    }

    /// <summary>发射投射物（弓箭手的箭、火焰法师的火球都用同一个预制体组件）</summary>
    private void FireProjectile(int dir)
    {
        if (config == null || config.projectilePrefab == null) return;

        Vector2 spawn = (Vector2)transform.position + new Vector2(0.4f * dir, 0.35f);

        Projectile shot = Instantiate(config.projectilePrefab, spawn, Quaternion.identity);
        shot.Launch(new Vector2(dir, 0f),
                    new DamageInfo
                    {
                        Amount         = AttackPower,
                        SourcePosition = transform.position,
                        KnockbackForce = 3f,
                        Attacker       = gameObject,
                        IsCritical     = false,
                        Element        = config.element,
                    },
                    playerLayer);
    }

    /// <summary>矩形判定（近战）。返回是否打到了人。</summary>
    private bool HitBox(Vector2 center, Vector2 size, float amount, float knockback)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, size, 0f, playerLayer);
        return ApplyHits(hits, amount, knockback);
    }

    /// <summary>圆形判定（落地、俯冲、冲撞）。返回是否打到了人。</summary>
    private bool HitCircle(Vector2 center, float radius, float amount, float knockback)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, playerLayer);
        return ApplyHits(hits, amount, knockback);
    }

    private bool ApplyHits(Collider2D[] hits, float amount, float knockback)
    {
        bool any = false;
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (!hit.TryGetComponent(out IDamageable target)) continue;
            if (!target.IsAlive) continue;

            target.TakeDamage(new DamageInfo
            {
                Amount         = amount,
                SourcePosition = transform.position,
                KnockbackForce = knockback,
                Attacker       = gameObject,
                IsCritical     = false,
                Element        = config != null ? config.element : DamageElement.None,
            });

            any = true;
            break;      // 一次动作只打一个目标
        }
        return any;
    }

    /// <summary>持盾战士的正面减伤</summary>
    private float FilterShield(float amount, DamageInfo info)
    {
        if (config == null) return amount;

        Vector2 from = info.SourcePosition - (Vector2)transform.position;
        if (from.sqrMagnitude < 0.0001f) return amount;

        float dot = Vector2.Dot(from.normalized, new Vector2(_facing, 0f));
        float limit = Mathf.Cos(config.shieldArc * 0.5f * Mathf.Deg2Rad);
        return dot >= limit ? amount * (1f - config.shieldReduction) : amount;
    }

    // ---------------- 感知 ----------------

    private void AcquireTarget()
    {
        if (_target != null) return;

        float radius = config != null ? config.detectRange : detectRange;
        Collider2D hit = Physics2D.OverlapCircle(transform.position, radius, playerLayer);
        if (hit == null) return;

        if (hit.TryGetComponent(out IDamageable d) && !d.IsAlive) return;

        _target = hit.transform;
    }

    // ---------------- 恐慌 ----------------

    /// <summary>正在被吓住（战吼期间）。外部（自检、UI）可以看这个值</summary>
    public bool IsFeared => _fearTimer > 0f;

    /// <summary>还活着（战吼要跳过尸体）</summary>
    public bool IsAlive => _health != null && _health.IsAlive;

    /// <summary>剩下的恐慌时间，秒</summary>
    public float FearRemaining => Mathf.Max(0f, _fearTimer);

    /// <summary>
    /// 被战吼吓到：丢掉目标、原地开始跑。
    /// 多个战吼叠加时取「更长的那个」，而不是把时间加起来。
    /// </summary>
    public void ApplyFear(float duration, Vector2 source)
    {
        if (!_health.IsAlive) return;

        _fearTimer = Mathf.Max(_fearTimer, duration);
        _target = null;

        _fearSourcePosition = source;
        _hasFearSource = true;

        _fearStepTimer = 0f;
        _fearStepDir = 0;
    }

    private void FearUpdate()
    {
        _fearTimer -= Time.fixedDeltaTime;

        if (_fearTimer <= 0f)
        {
            _fearStepDir = 0;
            _target = null;
            Brake();
            return;
        }

        _fearStepTimer -= Time.fixedDeltaTime;
        if (_fearStepTimer <= 0f) BeginFearStep();

        if (_fearStepDir == 0) return;

        float speed = MoveSpeed * fearSpeedMultiplier;
        float newX = Mathf.MoveTowards(_rb.velocity.x, _fearStepDir * speed,
                                       acceleration * Time.fixedDeltaTime);
        _rb.velocity = new Vector2(newX, _rb.velocity.y);

        _facing = _fearStepDir;
        if (_visual != null) _visual.SetFacing(_facing);
        if (_sprite != null) _sprite.flipX = _facing < 0;
        PlayMove();
    }

    private void BeginFearStep()
    {
        int away = 1;
        if (_hasFearSource) away = transform.position.x >= _fearSourcePosition.x ? 1 : -1;
        else if (_target != null) away = transform.position.x >= _target.position.x ? 1 : -1;

        bool turnBack = Random.value < 0.38f;

        if (turnBack)
        {
            _fearStepDir = -away;
            _fearStepTimer = Random.Range(fearBackStep.x, fearBackStep.y);
        }
        else
        {
            _fearStepDir = away;
            _fearStepTimer = Random.Range(fearRunStep.x, fearRunStep.y);
        }
    }

    // ---------------- 移动 ----------------

    private void MoveTowards(int dir, float speed)
    {
        float newX = Mathf.MoveTowards(_rb.velocity.x, dir * speed,
                                       acceleration * Time.fixedDeltaTime);
        _rb.velocity = new Vector2(newX, _rb.velocity.y);
    }

    private void Brake()
    {
        float newX = Mathf.MoveTowards(_rb.velocity.x, 0f,
                                       acceleration * Time.fixedDeltaTime);
        _rb.velocity = new Vector2(newX, _rb.velocity.y);
    }

    // ---------------- 动画 ----------------

    private void PlayIdle()
    {
        if (config == null) return;
        PlayAction(config.idleFrames, config.idleFps, true, false);
    }

    private void PlayMove()
    {
        if (config == null) return;
        PlayAction(config.moveFrames, config.moveFps, true, false);
    }

    /// <summary>播一套帧。frames 为空就什么都不做（留空 = 静态贴图）</summary>
    private void PlayAction(Sprite[] frames, float fps) => PlayAction(frames, fps, false, true);

    private void PlayAction(Sprite[] frames, float fps, bool loop, bool lockAnim)
    {
        if (frames == null || frames.Length == 0) return;
        if (_animLocked && lockAnim == false) return;      // 动作还没播完，别打断
        if (_frames == frames && lockAnim == _animLocked) return;  // 同一套，别重头播

        _frames = frames;
        _fps = Mathf.Max(1f, fps);
        _loopFrames = loop;
        _animLocked = lockAnim;
        _frameIndex = 0;
        _frameTimer = 0f;
        ApplyFrame();
    }

    private void AdvanceAnimation(float dt)
    {
        if (_frames == null || _frames.Length == 0) return;

        _frameTimer += dt;
        float step = 1f / _fps;
        while (_frameTimer >= step)
        {
            _frameTimer -= step;
            _frameIndex++;

            if (_frameIndex >= _frames.Length)
            {
                if (_loopFrames) _frameIndex = 0;
                else
                {
                    _frameIndex = _frames.Length - 1;
                    if (_animLocked) _animLocked = false;   // 动作播完，交回给 idle/move
                    break;
                }
            }
            ApplyFrame();
        }
    }

    private void ApplyFrame()
    {
        if (_sprite == null || _frames == null || _frames.Length == 0) return;
        _sprite.sprite = _frames[Mathf.Clamp(_frameIndex, 0, _frames.Length - 1)];
    }

    // ---------------- 死亡 ----------------

    private void OnDied()
    {
        _dead = true;
        _rb.velocity = Vector2.zero;
        _rb.simulated = false;

        // 有死亡帧就先播完再消失，没有就按老规矩 0.3 秒
        float wait = deathDelay;
        if (config != null && config.deathFrames != null && config.deathFrames.Length > 0)
        {
            _frames = config.deathFrames;
            _fps = Mathf.Max(1f, config.deathFps);
            _loopFrames = false;
            _animLocked = true;
            _frameIndex = 0;
            _frameTimer = 0f;
            ApplyFrame();
            wait = Mathf.Max(wait, config.deathFrames.Length / _fps);
        }

        Destroy(gameObject, wait);
    }

    // ---------------- 调试可视化 ----------------

    private void OnDrawGizmosSelected()
    {
        float detect = config != null ? config.detectRange : detectRange;
        float attack = config != null ? config.attackRange : attackRange;
        float lose   = config != null ? config.loseTargetRange : loseTargetRange;

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detect);

        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, lose);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, attack);

        if (config != null)
        {
            if (config.behavior == EnemyBehavior.Jumper)
            {
                Gizmos.color = new Color(0.6f, 1f, 0.4f, 0.6f);
                Gizmos.DrawWireSphere(transform.position, config.landRadius);
            }
            else if (config.behavior == EnemyBehavior.Shielder)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.7f);
                Vector3 from = transform.position;
                Vector3 a = Quaternion.Euler(0f, 0f, _facing > 0 ? config.shieldArc * 0.5f : 180f + config.shieldArc * 0.5f) * Vector3.right;
                Vector3 b = Quaternion.Euler(0f, 0f, _facing > 0 ? -config.shieldArc * 0.5f : 180f - config.shieldArc * 0.5f) * Vector3.right;
                Gizmos.DrawLine(from, from + a * 1.6f);
                Gizmos.DrawLine(from, from + b * 1.6f);
            }
            return;
        }

        AttackData data = MeleeAttack;
        if (data == null) return;

        Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.6f);
        Vector2 center = (Vector2)transform.position
                       + new Vector2(data.hitboxOffset.x * _facing, data.hitboxOffset.y);
        Gizmos.DrawWireCube(center, data.hitboxSize);
    }
}
