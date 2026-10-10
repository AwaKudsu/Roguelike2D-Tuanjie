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

    private Rigidbody2D _rb;
    private CharacterStats _stats;
    private Health _health;
    private HitReaction _hitReaction;
    private Transform _target;
    private CharacterVisual _visual;
    private float _attackCooldownTimer;
    private int _facing = 1;

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