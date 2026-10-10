using UnityEngine;

/// <summary>
/// 受击的表现层：闪白 + 击退 + 硬直。
///
/// 它只订阅 Health 的事件，自己不判断「该不该受伤」—— 逻辑与表现分离。
/// 将来要加受击音效、粒子、伤害飘字，都加在这里，战斗逻辑一行不用改。
/// </summary>
[RequireComponent(typeof(Health))]
public class HitReaction : MonoBehaviour
{
    [Header("闪白")]
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.12f;

    [Header("击退与硬直")]
    [Tooltip("受击后失去操作权的时间")]
    [SerializeField] private float stunDuration = 0.18f;

    [Tooltip("垂直上弹占击退力的比例，让受击者「被打飞」而不是纯水平滑走")]
    [Range(0f, 1f)]
    [SerializeField] private float verticalRatio = 0.4f;

    private Health _health;
    private SpriteRenderer _sprite;
    private Rigidbody2D _rb;
    private Color _originalColor;

    private float _flashTimer;
    private float _stunTimer;

    /// <summary>是否处于受击硬直。硬直期间应失去操作权</summary>
    public bool IsStunned => _stunTimer > 0f;

    private void Awake()
    {
        _health = GetComponent<Health>();
        _sprite = GetComponentInChildren<SpriteRenderer>();
        _rb     = GetComponent<Rigidbody2D>();

        if (_sprite != null) _originalColor = _sprite.color;
    }

    private void OnEnable()  => _health.Damaged += OnDamaged;
    private void OnDisable() => _health.Damaged -= OnDamaged;

    private void Update()
    {
        if (_flashTimer > 0f)
        {
            _flashTimer -= Time.deltaTime;
            if (_flashTimer <= 0f && _sprite != null)
                _sprite.color = _originalColor;
        }

        if (_stunTimer > 0f)
            _stunTimer -= Time.deltaTime;
    }

    private void OnDamaged(DamageInfo info)
    {
        // 1. 闪白
        if (_sprite != null)
        {
            _sprite.color = flashColor;
            _flashTimer = flashDuration;
        }

        // 2. 硬直
        _stunTimer = stunDuration;

        // 3. 击退：水平远离伤害来源，垂直固定给一点上弹
        if (_rb != null && info.KnockbackForce > 0f)
        {
            Vector2 away = (Vector2)transform.position - info.SourcePosition;
            if (away.sqrMagnitude < 0.0001f) away = Vector2.right;   // 完全重合时给个默认方向
            away.Normalize();

            _rb.velocity = new Vector2(
                away.x * info.KnockbackForce,
                info.KnockbackForce * verticalRatio
            );
        }
    }
}