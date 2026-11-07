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

    [Tooltip("纵向输入要压过横向这么多倍，才切正面 / 背面。大于 1 表示斜着走时仍然保持侧视")]
    [SerializeField] private float verticalBias = 1.15f;

    private Rigidbody2D _body;
    private PlayerInputReader _input;
    private SpriteRenderer _sprite;
    private int _facing = 1;
    private bool _active;

    // ---- 朝向贴图。三张都可以空：空了就回退到侧视，行为跟没这套东西时一模一样 ----
    private Sprite _frontSprite;    // 朝下（面对镜头）
    private Sprite _backSprite;     // 朝上（后脑勺）
    private Sprite _sideSprite;     // 朝左右（可翻转）
    private Sprite _defaultSprite;  // 进大厅时 SpriteRenderer 上那张，侧视兜底用
    private int _vertical;          // 1 = 朝下，-1 = 朝上，0 = 侧视

    // ---- 走路动画。没有帧数组时（图还没画）就一直是静态待机，跟以前一样 ----
    private Sprite[] _walkFrames;   // 侧向走路帧，来自 PlayerVisualSwitcher 的 run 序列
    private Sprite[] _idleFrames;   // 停下时用的待机帧（只用第 0 张）
    private float _walkTime;
    private int _walkFrame = -1;    // -1 = 当前显示的不是走路帧
    private float _idleTime;        // 待机呼吸的另一条计时，跟走路分开，停下时不会接着走
    private int _idleFrame = -1;

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

        if (!active && _sprite != null)
        {
            _sprite.sortingOrder = 0;
            _sprite.flipX = false;
            _vertical = 0;

            // 离开大厅时把正面/背面图换回去，别让横板关卡里顶着大厅的朝向跑
            if (_defaultSprite != null) _sprite.sprite = _defaultSprite;
        }
    }

    /// <summary>
    /// 告诉大厅「这个职业有哪些朝向的贴图」。任何一张为 null 都会回退到侧视。
    /// 图还没画好时留空即可 —— 和没有这套逻辑时的表现完全一致。
    /// 必须在 PlayerVisualSwitcher.SetClass 之后调用（那张待机图会被当作侧视兜底）。
    /// </summary>
    public void SetFacingSprites(ClassData data)
    {
        if (_sprite == null) _sprite = GetComponent<SpriteRenderer>();
        if (_defaultSprite == null && _sprite != null) _defaultSprite = _sprite.sprite;

        _frontSprite = data != null ? data.lobbyFront : null;
        _backSprite = data != null ? data.lobbyBack : null;
        _sideSprite = data != null ? data.lobbySide : null;

        _vertical = 0;
        ApplyFacingSprite();
    }

    /// <summary>
    /// 把职业的「跑的帧」交给大厅：侧向走路时按速度播放，停下就回待机。
    /// 传 null / 空数组 = 不播动画，一直显示静态图（原来的表现）。
    /// </summary>
    public void SetWalkFrames(Sprite[] walkFrames, Sprite[] idleFrames)
    {
        _walkFrames = walkFrames;
        _idleFrames = idleFrames;
        _walkFrame = -1;
        _walkTime = 0f;
        _idleFrame = -1;
        _idleTime = 0f;
        ApplyFacingSprite();
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

            // 只在侧视时翻图：正面 / 背面图镜像过来就不对称了
            if (_sprite != null && _vertical == 0) _sprite.flipX = _facing < 0;
        }

        // 只有「上下明显压过左右」才切正面 / 背面；斜着走保持侧视（四方向贴图斜向用侧身最自然）。
        // 屏幕 y 向上为正，所以往下走是 move.y < 0 —— 那才是要「正对镜头」的方向。
        int vertical = 0;
        if (move.sqrMagnitude > 0.01f
            && Mathf.Abs(move.y) > 0.15f
            && Mathf.Abs(move.y) > Mathf.Abs(move.x) * verticalBias)
        {
            vertical = move.y < 0f ? 1 : -1;
        }

        if (vertical != _vertical)
        {
            _vertical = vertical;
            ApplyFacingSprite();
        }

        UpdateWalkAnimation(_body.velocity.magnitude);

        // 俯视房间里「谁在前面」由脚底高度决定：站得越低（屏幕下方）越靠前
        if (_sprite != null)
        {
            _sprite.sortingOrder = Mathf.RoundToInt((sortBaseY - transform.position.y) * 6f);
        }
    }

    /// <summary>
    /// 走路时按「实际速度」推进帧：走得快腿迈得快；站着就慢慢播待机帧。
    /// 只在侧视（_vertical == 0）时播 —— 正/背面目前只有一张静态图，没有可播的序列。
    /// </summary>
    private void UpdateWalkAnimation(float speed)
    {
        Sprite[] walk = _vertical == 0 ? _walkFrames : null;

        if (walk != null && walk.Length > 1 && speed > 0.25f)
        {
            _idleFrame = -1;

            // 每秒 4 帧起步，跟速度线性挂钩；不用真实 fps，靠手感调这个系数
            _walkTime += Time.fixedDeltaTime * (4f + speed * 1.2f);

            int frame = Mathf.FloorToInt(_walkTime) % walk.Length;
            if (frame == _walkFrame) return;

            _walkFrame = frame;
            if (_sprite != null) _sprite.sprite = walk[frame];
            return;
        }

        // ---- 停下来：回侧向待机（有序列就慢慢播呼吸，没有就一张静态图）----
        if (_walkFrame >= 0)
        {
            _walkFrame = -1;
            _walkTime = 0f;
            ApplyFacingSprite();
        }

        Sprite[] idle = _vertical == 0 ? _idleFrames : null;
        if (idle == null || idle.Length <= 1 || _sprite == null) return;

        _idleTime += Time.fixedDeltaTime * 2.5f;

        int idleFrame = Mathf.FloorToInt(_idleTime) % idle.Length;
        if (idleFrame == _idleFrame) return;

        _idleFrame = idleFrame;
        _sprite.sprite = idle[idleFrame];
    }

    /// <summary>
    /// 按当前朝向挑贴图：朝下要正面、朝上要背面、其余用侧视并翻转。
    /// 缺哪张就退回侧视 —— 宁可看起来像横着走，也不能变成空白。
    /// </summary>
    private void ApplyFacingSprite()
    {
        if (_sprite == null) return;

        Sprite wanted = _vertical > 0 ? _frontSprite : _vertical < 0 ? _backSprite : null;

        if (wanted != null)
        {
            _sprite.sprite = wanted;
            _sprite.flipX = false;
            return;
        }

        Sprite side = _sideSprite;
        if (side == null && _idleFrames != null && _idleFrames.Length > 0) side = _idleFrames[0];
        if (side == null) side = _defaultSprite;
        if (side != null && _sprite.sprite != side) _sprite.sprite = side;
        _sprite.flipX = _facing < 0;

        // 正在走路时（_walkFrame >= 0）别停在这一帧静态图上，否则换朝向那一瞬间会闪一下站姿
        if (_walkFrame >= 0 && _walkFrames != null && _walkFrame < _walkFrames.Length)
        {
            _sprite.sprite = _walkFrames[_walkFrame];
        }
    }
}
