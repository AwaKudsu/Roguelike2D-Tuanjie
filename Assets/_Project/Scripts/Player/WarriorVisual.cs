using UnityEngine;

/// <summary>
/// 战士的贴图动画：把角色当前状态翻译成一帧贴图。
///
/// 为什么不用 Animator：工程里还没有任何 .controller / .anim 资产，
/// 而状态判断本身全在代码里（速度、是否落地、有没有在挥砍）。
/// 用一个 200 行的组件直读状态，比手搭 9 个状态 + 30 条过渡更不容易错，
/// 也让「不按 Play 也能自检」这件事可行 —— Animator 的过渡在批处理里验不了。
///
/// 优先级（上面的赢）：死亡 &gt; 受击 &gt; 攻击 &gt; 胜利 &gt; 空中 &gt; 跑 &gt; 待机
/// </summary>
[DisallowMultipleComponent]
public class WarriorVisual : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private PlayerController controller;
    [SerializeField] private Health health;
    [SerializeField] private PlayerAttacker attacker;
    [SerializeField] private Rigidbody2D body;

    [Header("单帧姿势")]
    [SerializeField] private Sprite idle;
    [SerializeField] private Sprite jump;
    [SerializeField] private Sprite fall;
    [SerializeField] private Sprite dash;
    [SerializeField] private Sprite hurt;
    [SerializeField] private Sprite death;
    [SerializeField] private Sprite victory;
    [SerializeField] private Sprite attackSlash;
    [SerializeField] private Sprite attackHeavy;
    [SerializeField] private Sprite attackThrust;

    [Header("跑动序列")]
    [SerializeField] private Sprite[] run = new Sprite[0];
    [SerializeField] private float runFps = 10f;

    [Header("节奏（秒）")]
    [Tooltip("普通挥砍保持多久。太长会盖住跑动，太短看不清姿势")]
    [SerializeField] private float attackHold = 0.15f;
    [SerializeField] private float heavyHold = 0.26f;
    [SerializeField] private float hurtHold = 0.22f;
    [SerializeField] private float victoryHold = 1.4f;

    [Header("判定")]
    [Tooltip("水平速度超过这个值就算在跑")]
    [SerializeField] private float runSpeedThreshold = 0.6f;

    private float _attackTimer;
    private float _attackHold;
    private Sprite _attackPose;
    private float _hurtTimer;
    private float _victoryTimer;
    private float _runTimer;
    private int _runFrame;
    private bool _dead;

    private RoomManager _rooms;
    private Room _victoryRoom;

    /// <summary>当前这一帧用的是哪个状态，给自检和调试看</summary>
    public string CurrentState { get; private set; } = "Idle";

    private void Awake()
    {
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (controller == null) controller = GetComponent<PlayerController>();
        if (health == null) health = GetComponent<Health>();
        if (attacker == null) attacker = GetComponent<PlayerAttacker>();
        if (body == null) body = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        if (attacker != null) attacker.AttackStarted += OnAttackStarted;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        if (attacker != null) attacker.AttackStarted -= OnAttackStarted;
    }

    private void Start()
    {
        Apply(idle);
    }

    private void Update()
    {
        TickTimers();
        Apply(ChooseFrame());
    }

    private void TickTimers()
    {
        if (_attackTimer > 0f) _attackTimer -= Time.deltaTime;
        if (_hurtTimer > 0f) _hurtTimer -= Time.deltaTime;
        if (_victoryTimer > 0f) _victoryTimer -= Time.deltaTime;
    }

    private Sprite ChooseFrame()
    {
        if (_dead && death != null)
        {
            CurrentState = "Death";
            return death;
        }

        if (_hurtTimer > 0f && hurt != null)
        {
            CurrentState = "Hurt";
            return hurt;
        }

        // 冲刺优先于攻击：窜出去的瞬间不该还被挥砍姿势钉着
        if (controller != null && controller.IsDashing && dash != null)
        {
            CurrentState = "Dash";
            return dash;
        }

        if (_attackTimer > 0f && _attackPose != null)
        {
            CurrentState = "Attack";
            return _attackPose;
        }

        CheckRoomCleared();

        if (_victoryTimer > 0f && victory != null)
        {
            CurrentState = "Victory";
            return victory;
        }

        bool grounded = controller == null || controller.IsGrounded;
        float vx = body != null ? body.velocity.x : 0f;
        float vy = body != null ? body.velocity.y : 0f;

        if (!grounded)
        {
            if (vy > 0.05f)
            {
                CurrentState = "Jump";
                return jump != null ? jump : idle;
            }

            CurrentState = "Fall";
            return fall != null ? fall : idle;
        }

        if (Mathf.Abs(vx) > runSpeedThreshold && run != null && run.Length > 0)
        {
            CurrentState = "Run";
            _runTimer += Time.deltaTime;
            int frame = Mathf.FloorToInt(_runTimer * runFps) % run.Length;
            if (frame != _runFrame)
            {
                _runFrame = frame;
            }

            return run[frame] != null ? run[frame] : idle;
        }

        _runTimer = 0f;
        _runFrame = 0;
        CurrentState = "Idle";
        return idle;
    }

    /// <summary>房间清空时举剑庆祝一下。不改逻辑，纯表现</summary>
    private void CheckRoomCleared()
    {
        if (_rooms == null) _rooms = FindObjectOfType<RoomManager>();
        if (_rooms == null) return;

        Room room = _rooms.CurrentRoom;
        if (room == null || room == _victoryRoom) return;
        if (!room.IsCleared) return;

        _victoryRoom = room;
        _victoryTimer = victoryHold;
    }

    private void Apply(Sprite frame)
    {
        if (sprite == null || frame == null) return;
        if (sprite.sprite == frame) return;
        sprite.sprite = frame;
    }

    // ---------------- 事件 ----------------

    private void OnDamaged(DamageInfo info)
    {
        if (_dead) return;
        _hurtTimer = hurtHold;
    }

    private void OnDied()
    {
        _dead = true;
    }

    private void OnAttackStarted(AttackData data)
    {
        if (_dead || data == null) return;

        if (data.isRanged)
        {
            _attackPose = attackThrust;
            _attackHold = attackHold;
        }
        else if (IsWideAttack(data))
        {
            // 旋风斩那类大范围挥砍：蹲身横扫的姿势更贴
            _attackPose = attackHeavy;
            _attackHold = heavyHold;
        }
        else
        {
            _attackPose = attackSlash;
            _attackHold = attackHold;
        }

        _attackTimer = _attackHold;
    }

    private static bool IsWideAttack(AttackData data)
    {
        Vector2 size = data.hitboxSize;
        return size.x * size.y >= 6f;
    }

    /// <summary>给自检用：把每套帧都读出来，确认接线没漏</summary>
    public Sprite[] DebugFrames()
    {
        return new[]
        {
            idle, jump, fall, dash, hurt, death, victory, attackSlash, attackHeavy, attackThrust
        };
    }

    /// <summary>给自检用：跑动序列</summary>
    public Sprite[] DebugRunFrames() => run;
}
