using UnityEngine;

/// <summary>
/// 最基础的敌人：看见你 → 走过去 → 贴近了就打。
///
/// 它没有用协程也没有用行为树，只是一个每帧重新判断的分支 ——
/// 因为对三个状态的东西来说，状态机本身就是过度设计。
/// 等敌人有 5 个以上状态（巡逻 / 警觉 / 追击 / 攻击 / 撤退），再引入正式的状态机。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyController : MonoBehaviour
{
    [Header("感知")]
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

    [Header("攻击")]
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

    private Rigidbody2D _rb;
    private CharacterStats _stats;
    private Health _health;
    private HitReaction _hitReaction;
    private Transform _target;
    private CharacterVisual _visual;
    private float _attackCooldownTimer;
    private int _facing = 1;

    // ---------------- 恐慌状态 ----------------
    // 恐慌不走协程：就三个数字（还剩多久、这一小步还剩多久、这一小步往哪边跑），
    // 每帧重新算一次方向 —— 这样玩家跑过去也吓不到它之外的东西，行为完全可预测。
    private float _fearTimer;
    private float _fearStepTimer;
    private int _fearStepDir;
    private Vector2 _fearSourcePosition;
    private bool _hasFearSource;

    private void Awake()
    {
        _rb          = GetComponent<Rigidbody2D>();
        _stats       = GetComponent<CharacterStats>();
        _health      = GetComponent<Health>();
        _hitReaction = GetComponent<HitReaction>();
        _visual      = GetComponent<CharacterVisual>();
    }

    private void OnEnable()  => _health.Died += OnDied;
    private void OnDisable() => _health.Died -= OnDied;

    private void FixedUpdate()
    {
        if (_attackCooldownTimer > 0f) _attackCooldownTimer -= Time.fixedDeltaTime;

        if (!_health.IsAlive) return;

        // 受击硬直期间不移动也不攻击 —— 否则敌人会顶着击退往前走，打击感全无
        if (_hitReaction != null && _hitReaction.IsStunned) return;

        // 被战吼吓住时，谁也不追、也不打，只顾着跑（优先级在硬直之下）
        if (_fearTimer > 0f)
        {
            FearUpdate();
            return;
        }

        AcquireTarget();

        if (_target == null)
        {
            Brake();
            return;
        }

        float distance = Vector2.Distance(transform.position, _target.position);

        // 追丢了：距离超过 loseTargetRange 就放弃，而不是 detectRange —— 
        // 两个阈值分开可以避免敌人在边界上反复「锁定 / 丢失」
        if (distance > loseTargetRange)
        {
            _target = null;
            Brake();
            return;
        }

        int dir = _target.position.x > transform.position.x ? 1 : -1;
        _facing = dir;

        // 贴图朝向跟着走，免得「倒着走」的敌人看着像在前进
        if (_visual != null) _visual.SetFacing(dir);

        if (distance <= attackRange) Attack();
        else                         Chase(dir);
    }

    // ---------------- 感知 ----------------

    private void AcquireTarget()
    {
        if (_target != null) return;   // 已有目标就不换

        Collider2D hit = Physics2D.OverlapCircle(transform.position, detectRange, playerLayer);
        if (hit == null) return;

        // 不打已经死掉的玩家
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
    ///
    /// 多个战吼叠加时取「更长的那个」，而不是把时间加起来 ——
    /// 否则连着放两次就能把敌人永久钉在恐慌里，那就变成控制技能了。
    /// </summary>
    public void ApplyFear(float duration, Vector2 source)
    {
        if (!_health.IsAlive) return;

        _fearTimer = Mathf.Max(_fearTimer, duration);
        _target = null;                    // 丢失攻击目标

        _fearSourcePosition = source;
        _hasFearSource = true;

        _fearStepTimer = 0f;               // 立刻决定第一步往哪跑
        _fearStepDir = 0;
    }

    private void FearUpdate()
    {
        _fearTimer -= Time.fixedDeltaTime;

        if (_fearTimer <= 0f)
        {
            // 吓完了：停在原地先回气，目标会在下一帧重新找
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

        // 朝哪跑就朝哪看 —— 恐慌最直观的表现就是「一直背对着你」
        _facing = _fearStepDir;
        if (_visual != null) _visual.SetFacing(_facing);
    }

    /// <summary>
    /// 决定接下来这一小步往哪跑。
    ///
    /// 大部分时候是「远离玩家」，偶尔（38%）掉头往回冲两步再掉回去 ——
    /// 这就是「来回快跑」的来源。整体方向仍然是远离，所以玩家会觉得它在逃命，
    /// 而不是站在原地抽搐。
    /// </summary>
    private void BeginFearStep()
    {
        int away = 1;
        if (_hasFearSource)
        {
            away = transform.position.x >= _fearSourcePosition.x ? 1 : -1;
        }
        else if (_target != null)
        {
            away = transform.position.x >= _target.position.x ? 1 : -1;
        }

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

    private float MoveSpeed => _stats != null ? _stats.Get(StatType.MoveSpeed) : 3f;

    private void Chase(int dir)
    {
        float targetSpeed = dir * MoveSpeed;
        float newX = Mathf.MoveTowards(_rb.velocity.x, targetSpeed,
                                       acceleration * Time.fixedDeltaTime);
        _rb.velocity = new Vector2(newX, _rb.velocity.y);
    }

    private void Brake()
    {
        float newX = Mathf.MoveTowards(_rb.velocity.x, 0f,
                                       acceleration * Time.fixedDeltaTime);
        _rb.velocity = new Vector2(newX, _rb.velocity.y);
    }

    // ---------------- 攻击 ----------------

    private void Attack()
    {
        Brake();

        if (attackData == null) return;
        if (_attackCooldownTimer > 0f) return;

        _attackCooldownTimer = attackData.cooldown;

        Vector2 center = (Vector2)transform.position
                       + new Vector2(attackData.hitboxOffset.x * _facing, attackData.hitboxOffset.y);

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, attackData.hitboxSize, 0f,
                                                    attackData.targetLayers);

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (!hit.TryGetComponent(out IDamageable target)) continue;
            if (!target.IsAlive) continue;

            float attack = _stats != null ? _stats.Get(StatType.Attack) : 5f;

            target.TakeDamage(new DamageInfo
            {
                Amount         = attack * attackData.damageMultiplier,
                SourcePosition = transform.position,
                KnockbackForce = attackData.knockbackForce,
                Attacker       = gameObject,
                IsCritical     = false,
            });

            break;   // 一次挥击只打一个目标
        }
    }

    // ---------------- 死亡 ----------------

    private void OnDied()
    {
        _rb.velocity = Vector2.zero;
        _rb.simulated = false;         // 停掉物理，尸体不会继续滑

        Destroy(gameObject, deathDelay);
    }

    // ---------------- 调试可视化 ----------------

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, loseTargetRange);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (attackData == null) return;
        Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.6f);
        Vector2 center = (Vector2)transform.position
                       + new Vector2(attackData.hitboxOffset.x * _facing, attackData.hitboxOffset.y);
        Gizmos.DrawWireCube(center, attackData.hitboxSize);
    }
}