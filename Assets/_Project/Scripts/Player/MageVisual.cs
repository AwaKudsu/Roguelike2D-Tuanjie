using UnityEngine;

/// <summary>
/// 法师的动作表现（`WarriorVisual` 的法师版）。
///
/// 和战士的差别：
///   · 法师的攻击是**蓄力**的 —— `PlayCharge(true)` 期间停在「杖头发光」那一帧，
///     玩家看到的是「法术在攒」，而不是一挥手就没了；
///   · 施法（回血）用 3 帧序列：举杖 → 头顶光环 → 收尾，配合 `HealCircle` 的法阵。
///
/// 状态优先级：死亡 &gt; 受击 &gt; 施法 &gt; 攻击 &gt; 蓄力 &gt; 冲刺位（法师没有冲刺帧，跳过）&gt; 空中 &gt; 跑 &gt; 待机。
/// </summary>
[DisallowMultipleComponent]
public class MageVisual : MonoBehaviour, IPlayerAnim
{
    [Header("引用（留空会自动在同一个物体上找）")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private PlayerController controller;
    [SerializeField] private Health health;
    [SerializeField] private PlayerAttacker attacker;
    [SerializeField] private Rigidbody2D body;

    [Header("待机（3 帧呼吸）")]
    [SerializeField] private Sprite[] idleFrames = new Sprite[0];
    [SerializeField] private float idleFrameInterval = 0.62f;

    [Header("跑动（6 帧）")]
    [SerializeField] private Sprite[] run = new Sprite[0];
    [SerializeField] private float runFpsMin = 10f;
    [SerializeField] private float runFpsMax = 20f;
    [SerializeField] private float runSpeedThreshold = 0.6f;

    [Header("单帧")]
    [SerializeField] private Sprite jump;
    [SerializeField] private Sprite fall;
    [SerializeField] private Sprite hurt;
    [SerializeField] private Sprite death;
    [SerializeField] private Sprite victory;

    [Header("攻击（蓄力序列，4 帧）")]
    [SerializeField] private Sprite[] attackFrames = new Sprite[0];
    [SerializeField] private float attackHold = 0.34f;

    [Header("施法（3 帧：举杖 / 头顶光环 / 收尾）")]
    [SerializeField] private Sprite[] castFrames = new Sprite[0];
    [SerializeField] private float castHold = 0.55f;

    [Header("节奏")]
    [SerializeField] private float hurtHold = 0.22f;
    [SerializeField] private float victoryHold = 1.4f;

    /// <summary>当前状态名，自检和调试看它</summary>
    public string CurrentState { get; private set; } = "Idle";

    public bool IsCharging { get; private set; }
    public bool IsCasting => _castTimer > 0f;

    private float _castTimer;
    private float _attackTimer;
    private float _hurtTimer;
    private float _victoryTimer;
    private float _idleTimer;
    private float _runTimer;
    private float _chargeTimer;
    private int _idleIndex;
    private Room _victoryRoom;

    // ---------------- 生命周期 ----------------

    private void Awake()
    {
        if (sprite     == null) sprite     = GetComponentInChildren<SpriteRenderer>();
        if (controller == null) controller = GetComponent<PlayerController>();
        if (health     == null) health     = GetComponent<Health>();
        if (attacker   == null) attacker   = GetComponent<PlayerAttacker>();
        if (body       == null) body       = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died    += OnDied;
        }

        if (attacker != null) attacker.AttackStarted += OnAttackStarted;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died    -= OnDied;
        }

        if (attacker != null) attacker.AttackStarted -= OnAttackStarted;

        IsCharging = false;
    }

    private void Start()
    {
        Apply(idleFrames.Length > 0 ? idleFrames[0] : null);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (_castTimer   > 0f) _castTimer   -= dt;
        if (_attackTimer > 0f) _attackTimer -= dt;
        if (_hurtTimer   > 0f) _hurtTimer   -= dt;
        if (_victoryTimer > 0f) _victoryTimer -= dt;

        if (IsCharging) _chargeTimer += dt;

        CheckRoomCleared();
        Apply(ChooseFrame(dt));
    }

    // ---------------- 事件 ----------------

    private void OnDamaged(DamageInfo info)
    {
        _hurtTimer = hurtHold;
        IsCharging = false;      // 挨打会打断蓄力（技能层也会掉蓄力，两边一致）
    }

    private void OnDied()
    {
        _hurtTimer = 0f;
        _castTimer = 0f;
        _attackTimer = 0f;
        IsCharging = false;
    }

    private void OnAttackStarted(AttackData data)
    {
        // 法师的普通攻击是法术弹：播一次「杖头发光」的短序列
        _attackTimer = attackHold;
    }

    private void CheckRoomCleared()
    {
        if (victory == null) return;

        var rooms = FindObjectOfType<RoomManager>();
        if (rooms == null) return;

        Room room = rooms.CurrentRoom;
        if (room == null || room == _victoryRoom || !room.IsCleared) return;

        _victoryRoom = room;
        _victoryTimer = victoryHold;
    }

    // ---------------- 状态机 ----------------

    private Sprite ChooseFrame(float dt)
    {
        if (health != null && !health.IsAlive)
        {
            CurrentState = "Death";
            return death != null ? death : FirstOf(idleFrames);
        }

        if (_hurtTimer > 0f)
        {
            CurrentState = "Hurt";
            return hurt != null ? hurt : FirstOf(idleFrames);
        }

        if (_castTimer > 0f)
        {
            CurrentState = "Cast";
            return SequenceFrame(castFrames, 1f - _castTimer / Mathf.Max(0.05f, castHold));
        }

        if (_attackTimer > 0f)
        {
            CurrentState = IsCharging ? "Charge" : "Attack";
            // 蓄力时停在「杖头发光」那一帧：玩家能看出法术在攒，而不是一挥手就没
            int index = IsCharging ? Mathf.Min(1, attackFrames.Length - 1)
                                   : Mathf.Clamp(
                                       Mathf.FloorToInt((1f - _attackTimer / Mathf.Max(0.05f, attackHold)) * attackFrames.Length),
                                       0, Mathf.Max(0, attackFrames.Length - 1));
            return attackFrames.Length > 0 ? attackFrames[index] : FirstOf(idleFrames);
        }

        if (IsCharging)
        {
            CurrentState = "Charge";
            return attackFrames.Length > 1 ? attackFrames[1] : FirstOf(attackFrames);
        }

        if (_victoryTimer > 0f && victory != null)
        {
            CurrentState = "Victory";
            return victory;
        }

        float vx = body != null ? body.velocity.x : 0f;
        float vy = body != null ? body.velocity.y : 0f;
        bool grounded = controller == null || controller.IsGrounded;

        if (!grounded || Mathf.Abs(vy) > 0.05f)
        {
            CurrentState = vy > 0.05f ? "Jump" : "Fall";
            return vy > 0.05f
                ? (jump != null ? jump : FirstOf(idleFrames))
                : (fall != null ? fall : (jump != null ? jump : FirstOf(idleFrames)));
        }

        if (run.Length > 0 && Mathf.Abs(vx) > runSpeedThreshold)
        {
            CurrentState = "Run";

            float top = controller != null ? Mathf.Max(0.1f, controller.MaxSpeed) : 6f;
            float fps = Mathf.Lerp(runFpsMin, runFpsMax, Mathf.Clamp01(Mathf.Abs(vx) / top));
            _runTimer += fps * dt;

            int step = (int)Mathf.Repeat(_runTimer, run.Length);
            return run[step];
        }

        // 待机：0 → 1 → 2 → 1 的呼吸
        if (idleFrames.Length > 1)
        {
            _idleIndex = Mathf.FloorToInt(_idleTimer / Mathf.Max(0.05f, idleFrameInterval));
            int beats = (idleFrames.Length - 1) * 2;
            _idleTimer += dt;
            if (_idleTimer >= idleFrameInterval * beats)
            {
                _idleTimer = 0f;
                _idleIndex = 0;
            }

            int i = _idleIndex % beats;
            int frame = i < idleFrames.Length ? i : beats - i;
            CurrentState = "Idle";
            return idleFrames[Mathf.Clamp(frame, 0, idleFrames.Length - 1)];
        }

        CurrentState = "Idle";
        return FirstOf(idleFrames);
    }

    private static Sprite SequenceFrame(Sprite[] frames, float t)
    {
        if (frames == null || frames.Length == 0) return null;

        int index = Mathf.Clamp(Mathf.FloorToInt(t * frames.Length), 0, frames.Length - 1);
        return frames[index];
    }

    private static Sprite FirstOf(Sprite[] frames)
        => frames != null && frames.Length > 0 ? frames[0] : null;

    private void Apply(Sprite frame)
    {
        if (sprite == null || frame == null) return;
        if (sprite.sprite == frame) return;

        sprite.sprite = frame;
    }

    // ---------------- IPlayerAnim ----------------

    public void PlaySpin(float duration)
    {
        // 法师没有旋转帧：用施法序列代替（转圈这件事留给法阵和弧光）
        PlayCast(duration);
    }

    public void PlayWarcry(float hold)
    {
        PlayCast(hold);
    }

    public void PlayCharge(bool on)
    {
        IsCharging = on;
        if (on) _chargeTimer = 0f;
    }

    public void PlayCast(float hold)
    {
        _castTimer = Mathf.Max(0.05f, hold);
        _attackTimer = 0f;
    }

    /// <summary>蓄力进度 0~1，给「火球正在变大」这类预览用</summary>
    public float ChargeRatio => Mathf.Clamp01(_chargeTimer / 1.1f);

    // ---------------- 自检接口 ----------------

    public Sprite[] DebugIdleFrames() => idleFrames;
    public Sprite[] DebugRunFrames() => run;
    public Sprite[] DebugAttackFrames() => attackFrames;
    public Sprite[] DebugCastFrames() => castFrames;
    public Sprite DebugDeath() => death;
    public Sprite DebugHurt() => hurt;
    public Sprite DebugJump() => jump;
}
