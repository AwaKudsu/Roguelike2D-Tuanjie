using UnityEngine;

/// <summary>
/// 玩家移动与跳跃逻辑。
///
/// 注：受击硬直期间会主动放弃操作权，但重力和下落限制仍然生效 ——
/// 这样被击退时角色会自然地被打飞、落地，而不是僵在半空。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerController : MonoBehaviour
{
    [Header("移动")]
    [SerializeField] private float maxSpeed = 8f;

    [Tooltip("按下方向键时的加速度")]
    [SerializeField] private float acceleration = 60f;

    [Tooltip("松开方向键时的减速度")]
    [SerializeField] private float deceleration = 80f;

    [Header("跳跃")]
    [Tooltip("起跳瞬间赋予的垂直速度")]
    [SerializeField] private float jumpForce = 13f;

    [Tooltip("上升阶段的重力倍率（越小跳得越高、滞空越久）")]
    [SerializeField] private float riseGravityScale = 3.5f;

    [Tooltip("下落阶段的重力倍率（越大落地越干脆）")]
    [SerializeField] private float fallGravityScale = 6f;

    [Tooltip("上升途中松开跳跃键，剩余上升速度乘以这个系数 —— 可变跳跃高度")]
    [Range(0f, 1f)]
    [SerializeField] private float jumpCutMultiplier = 0.45f;

    [Tooltip("最大下落速度，防止高速下坠穿透地面")]
    [SerializeField] private float maxFallSpeed = 25f;

    [Header("手感辅助")]
    [Tooltip("土狼时间：离开平台后仍可起跳的宽限时间")]
    [SerializeField] private float coyoteTime = 0.1f;

    [Tooltip("跳跃缓冲：落地前提前按跳，落地瞬间自动起跳")]
    [SerializeField] private float jumpBufferTime = 0.1f;

    [Header("地面检测")]
    [Tooltip("检测圆的半径。太大会导致「还没落地就算落地」，与跳跃缓冲叠加会表现为空中起跳")]
    [SerializeField] private float groundCheckRadius = 0.05f;

    [Tooltip("⚠️ 必须选 Ground 层，留空会导致永远检测不到地面")]
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody2D _rb;
    private Collider2D _col;
    private PlayerInputReader _input;
    private HitReaction _hitReaction;
    private CharacterStats _stats;

    private float _coyoteCounter;      // 土狼时间剩余
    private float _jumpBufferCounter;  // 跳跃缓冲剩余

    /// <summary>本帧是否站在地面上</summary>
    public bool IsGrounded { get; private set; }

    private void Awake()
    {
        _rb          = GetComponent<Rigidbody2D>();
        _col         = GetComponent<Collider2D>();
        _input       = GetComponent<PlayerInputReader>();
        _hitReaction = GetComponent<HitReaction>();
        _stats = GetComponent<CharacterStats>();
    }

    private void FixedUpdate()
    {
        // 顺序有讲究：先知道站没站在地上，才能算土狼时间和能不能起跳
        CheckGround();

        bool canAct = _hitReaction == null || !_hitReaction.IsStunned;

        if (canAct)
        {
            UpdateJumpTimers();
            TryJump();
            ApplyHorizontalMovement();
        }
        else
        {
            // 硬直期间清空输入与计时器，避免硬直结束后「补跳」一下
            _input.ConsumeJumpPressed();
            _coyoteCounter     = 0f;
            _jumpBufferCounter = 0f;
        }

        // 重力和下落限制在硬直期间照常生效，否则被打飞的角色会僵在半空
        ApplyGravityAndJumpCut();
        ClampFallSpeed();
    }

    // ---------------- 地面检测 ----------------

    /// <summary>脚底中心点：取碰撞体包围盒的底边中点</summary>
    private Vector2 GroundCheckPoint =>
        new Vector2(_col.bounds.center.x, _col.bounds.min.y);

    private void CheckGround()
    {
        // 用圆形重叠检测，而不是 OnCollisionEnter —— 后者会把自己撞墙、撞天花板也算成「落地」
        IsGrounded = Physics2D.OverlapCircle(GroundCheckPoint, groundCheckRadius, groundLayer);
    }

    // ---------------- 计时器 ----------------

    private void UpdateJumpTimers()
    {
        // 土狼时间：站在地上就充满，离开后开始倒数
        if (IsGrounded) _coyoteCounter = coyoteTime;
        else            _coyoteCounter -= Time.fixedDeltaTime;

        // 跳跃缓冲：按下瞬间充满，之后开始倒数
        if (_input.ConsumeJumpPressed()) _jumpBufferCounter = jumpBufferTime;
        else                             _jumpBufferCounter -= Time.fixedDeltaTime;
    }

    // ---------------- 起跳 ----------------

    private void TryJump()
    {
        // 两个计时器都还有剩余 = 玩家「想跳」且「跳得了」
        if (_jumpBufferCounter <= 0f || _coyoteCounter <= 0f) return;

        _rb.velocity = new Vector2(_rb.velocity.x, jumpForce);

        // 立刻清零，避免同一次按键触发多次起跳
        _jumpBufferCounter = 0f;
        _coyoteCounter     = 0f;
    }

    // ---------------- 水平移动 ----------------
    /// <summary>实际最大速度：优先读属性系统（职业差异走这里），没组件就用手填值</summary>
    private float MaxSpeed => _stats != null ? _stats.Get(StatType.MoveSpeed) : maxSpeed;

    private void ApplyHorizontalMovement()
    {
        float targetSpeed = _input.Move.x * MaxSpeed; 
        float rate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;

        float newSpeedX = Mathf.MoveTowards(
            _rb.velocity.x,
            targetSpeed,
            rate * Time.fixedDeltaTime
        );

        _rb.velocity = new Vector2(newSpeedX, _rb.velocity.y);
    }

    // ---------------- 重力与可变跳跃高度 ----------------

    private void ApplyGravityAndJumpCut()
    {
        // 上升和下落用不同重力：下落更快，落地更利落
        _rb.gravityScale = _rb.velocity.y < 0f ? fallGravityScale : riseGravityScale;

        // 还在上升时松开跳跃键 → 立刻削掉一部分上升速度，跳得就矮
        if (_input.ConsumeJumpReleased() && _rb.velocity.y > 0f)
        {
            _rb.velocity = new Vector2(
                _rb.velocity.x,
                _rb.velocity.y * jumpCutMultiplier
            );
        }
    }

    private void ClampFallSpeed()
    {
        if (_rb.velocity.y < -maxFallSpeed)
            _rb.velocity = new Vector2(_rb.velocity.x, -maxFallSpeed);
    }

    // ---------------- 调试可视化 ----------------

    // 在 Scene 视图里画出地面检测圆：站在地上是绿色，悬空是红色
    private void OnDrawGizmosSelected()
    {
        var col = _col != null ? _col : GetComponent<Collider2D>();
        if (col == null) return;

        Vector2 p = new Vector2(col.bounds.center.x, col.bounds.min.y);
        Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(p, groundCheckRadius);
    }
}