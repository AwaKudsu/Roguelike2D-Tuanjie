using UnityEngine;

/// <summary>
/// 大厅里用的「俯视走路」控制器：四方向移动、没有重力、按 Y 排序。
///
/// ★ 为什么不让 <see cref="PlayerController"/> 直接跑？
///   它是横板控制器：重力、跳跃、土狼时间、冲刺、攻击判定全在里面。
///   大厅是俯视房间，人应该能在 XY 平面上随便走，所以大厅期间把那个组件关掉，
///   换成这个只负责「朝哪走、画在谁前面」的小组件；出发时再换回去。
/// </summary>
[DisallowMultipleComponent]
public class LobbyAvatar : MonoBehaviour
{
    [Tooltip("大厅里的移动速度（比关卡里略慢一点，方便慢慢挑）")]
    [SerializeField] private float moveSpeed = 4.2f;

    [Tooltip("加速度。给一点惯性，走起来不像贴图被拖")]
    [SerializeField] private float acceleration = 46f;

    [Tooltip("Y 排序基准：数值越小越靠前")]
    [SerializeField] private float sortBaseY = 10f;

    private Rigidbody2D _body;
    private PlayerInputReader _input;
    private SpriteRenderer _sprite;
    private int _facing = 1;
    private bool _active;

    /// <summary>正在由大厅接管（确认角色之后才会打开）</summary>
    public bool IsActive => _active;

    private void Awake()
    {
        _body = GetComponent<Rigidbody2D>();
        _input = GetComponent<PlayerInputReader>();
        _sprite = GetComponent<SpriteRenderer>();
    }

    /// <summary>大厅确认角色后打开 / 出发时关掉</summary>
    public void SetActive(bool active)
    {
        _active = active;
        enabled = active;

        if (_body != null)
        {
            _body.velocity = Vector2.zero;
            _body.gravityScale = active ? 0f : _body.gravityScale;
        }

        if (!active && _sprite != null) _sprite.sortingOrder = 0;
    }

    private void FixedUpdate()
    {
        if (!_active || _body == null) return;

        Vector2 move = _input != null ? _input.Move : Vector2.zero;
        Vector2 target = move.sqrMagnitude > 0.01f ? move.normalized * moveSpeed : Vector2.zero;

        _body.velocity = Vector2.MoveTowards(_body.velocity, target, acceleration * Time.fixedDeltaTime);

        if (Mathf.Abs(move.x) > 0.15f)
        {
            _facing = move.x > 0f ? 1 : -1;
            if (_sprite != null) _sprite.flipX = _facing < 0;
        }

        // 俯视房间里「谁在前面」由脚底高度决定：站得越低（屏幕下方）越靠前
        if (_sprite != null)
        {
            _sprite.sortingOrder = Mathf.RoundToInt((sortBaseY - transform.position.y) * 6f);
        }
    }
}
