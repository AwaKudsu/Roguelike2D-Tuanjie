using UnityEngine;

/// <summary>
/// 射手的动作表现（`WarriorVisual` / `MageVisual` 的射手版）。
///
/// 和另外两套的差别：
///   · 射手的核心动作是**拉弓**，所以普通攻击播一段 3 帧的射击序列（起手 → 放箭 → 后坐），
///     而不是挥砍；技能（多重射击、穿透箭、箭雨）在视觉上也都是「拉弓」；
///   · 猎人专注是**举弓蓄力**的姿势，单独占一个 focus 计时；
///   · 射手有冲刺帧（战士也有，法师没有），所以状态表里保留 Dash 这一档。
///
/// 状态优先级：死亡 &gt; 受击 &gt; 射击 &gt; 冲刺 &gt; 专注 &gt; 胜利 &gt; 空中 &gt; 行走 &gt; 待机。
///
/// 不用 Animator：姿势全在代码里，所以不按 Play 也能自检。
/// </summary>
[DisallowMultipleComponent]
public class ArcherVisual : MonoBehaviour, IPlayerAnim
{
    [Header("引用（留空会自动在同一个物体上找）")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private PlayerController controller;
    [SerializeField] private Health health;
    [SerializeField] private PlayerAttacker attacker;
    [SerializeField] private SkillCaster caster;
    [SerializeField] private Rigidbody2D body;

    [Header("单帧")]
    [SerializeField] private Sprite idle;
    [SerializeField] private Sprite jump;
    [SerializeField] private Sprite fall;
    [SerializeField] private Sprite dash;
    [SerializeField] private Sprite hurt;
    [SerializeField] private Sprite death;
    [SerializeField] private Sprite victory;

    [Tooltip("猎人专注：举弓蓄力。没有专门素材时退回 victory")]
    [SerializeField] private Sprite focus;

    [Header("待机（呼吸：站稳 → 微抬 → 抬更多 → 微抬）")]
    [SerializeField] private Sprite[] idleFrames = new Sprite[0];
    [SerializeField] private float idleFrameInterval = 0.72f;

    [Header("行走（4 帧，脚落地 / 抬脚 / 抬更高 / 压缩）")]
    [SerializeField] private Sprite[] walkFrames = new Sprite[0];

    [Tooltip("慢走时的帧率")]
    [SerializeField] private float walkFpsMin = 9f;

    [Tooltip("全速时的帧率")]
    [SerializeField] private float walkFpsMax = 17f;

    [Header("射击序列（起手 / 放箭 / 后坐）")]
    [SerializeField] private Sprite[] shootFrames = new Sprite[0];

    [Tooltip("一次射击姿势持续多久。箭本身飞得比这快，所以只是给玩家一个「刚射出去」的读感")]
    [SerializeField] private float shootHold = 0.20f;

    [Header("节奏（秒）")]
    [SerializeField] private float hurtHold = 0.22f;
    [SerializeField] private float victoryHold = 1.4f;
    [SerializeField] private float focusHold = 0.55f;

    [Header("判定")]
    [Tooltip("水平速度超过这个值就算在走")]
    [SerializeField] private float runSpeedThreshold = 0.6f;

    private float _hurtTimer;
    private float _victoryTimer;
    private float _focusTimer;
    private float _shootTimer;
    private float _shootTotal;
    private float _runTimer;
    private float _idleTimer;

    private RoomManager _rooms;
    private Room _victoryRoom;

    /// <summary>当前这一帧是什么状态，给自检与调试看</summary>
    public string CurrentState { get; private set; } = "Idle";

    /// <summary>换职业时切回的那张图（PlayerVisualSwitcher 用）</summary>
    public Sprite DefaultSprite => FirstOf(idleFrames, idle);

    /// <summary>射击姿势是不是正在播</summary>
    public bool IsShooting => _shootTimer > 0f;

    /// <summary>举弓蓄力是不是正在播</summary>
    public bool IsFocused => _focusTimer > 0f;

    /// <summary>角色是不是已经倒下（死亡优先级最高，所以这里统一从 Health 读）</summary>
    private bool IsDead => health != null && !health.IsAlive;

    // ---------------- 生命周期 ----------------

    private void Awake()
    {
        if (sprite     == null) sprite     = GetComponentInChildren<SpriteRenderer>();
        if (controller == null) controller = GetComponent<PlayerController>();
        if (health     == null) health     = GetComponent<Health>();
        if (attacker   == null) attacker   = GetComponent<PlayerAttacker>();
        if (caster     == null) caster     = GetComponent<SkillCaster>();
        if (body       == null) body       = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += OnDamaged;
        }

        if (attacker != null) attacker.AttackStarted += OnAttackStarted;
        if (caster   != null) caster.SkillCast     += OnSkillCast;

        // 被切走一段时间再切回来时，别把旧的计时器接着播
        _shootTimer = 0f;
        _hurtTimer  = 0f;
        _focusTimer = 0f;
    }

    private void OnDisable()
    {
        if (health != null) health.Damaged -= OnDamaged;

        if (attacker != null) attacker.AttackStarted -= OnAttackStarted;
        if (caster   != null) caster.SkillCast     -= OnSkillCast;
    }

    private void Start()
    {
        Apply(DefaultSprite);
    }

    private void Update()
    {
        TickTimers();

        CheckRoomCleared();
        Apply(ChooseFrame());
    }

    private void TickTimers()
    {
        float dt = Time.deltaTime;

        if (_hurtTimer    > 0f) _hurtTimer    -= dt;
        if (_victoryTimer > 0f) _victoryTimer -= dt;
        if (_focusTimer   > 0f) _focusTimer   -= dt;
        if (_shootTimer   > 0f) _shootTimer   -= dt;
    }

    // ---------------- 状态机 ----------------

    private Sprite ChooseFrame()
    {
        if (IsDead)
        {
            CurrentState = "Death";
            return death != null ? death : DefaultSprite;
        }

        if (_hurtTimer > 0f)
        {
            CurrentState = "Hurt";
            return hurt != null ? hurt : DefaultSprite;
        }

        Sprite shoot = ShootFrame();
        if (shoot != null)
        {
            CurrentState = "Shoot";
            return shoot;
        }

        if (controller != null && controller.IsDashing && dash != null)
        {
            CurrentState = "Dash";
            return dash;
        }

        if (_focusTimer > 0f)
        {
            CurrentState = "Focus";
            return focus != null ? focus : (victory != null ? victory : DefaultSprite);
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
                ? (jump != null ? jump : DefaultSprite)
                : (fall != null ? fall : (jump != null ? jump : DefaultSprite));
        }

        if (Mathf.Abs(vx) > runSpeedThreshold && walkFrames != null && walkFrames.Length > 0)
        {
            CurrentState = "Walk";
            return WalkFrame(vx);
        }

        _runTimer = 0f;
        return IdleFrame();
    }

    /// <summary>
    /// 射击帧：把 shootHold 均分给每一帧。
    /// 单帧素材也能用（均分后退化成一直播那一张），所以少了帧不会变成空白人。
    /// </summary>
    private Sprite ShootFrame()
    {
        if (_shootTimer <= 0f) return null;

        Sprite[] frames = Compact(shootFrames, null);
        if (frames.Length == 0) return null;

        float total = Mathf.Max(0.05f, _shootTotal);
        float t = Mathf.Clamp01(1f - _shootTimer / total);
        int index = Mathf.Min(frames.Length - 1, (int)(t * frames.Length));
        return frames[index];
    }

    /// <summary>待机：ping-pong 循环（0 → 1 → 2 → 1），不会从最后一帧硬切回第一帧</summary>
    private Sprite IdleFrame()
    {
        CurrentState = "Idle";
        _idleTimer += Time.deltaTime;

        if (idleFrames == null || idleFrames.Length == 0) return idle;

        int beats = idleFrames.Length <= 2 ? idleFrames.Length : (idleFrames.Length - 1) * 2;
        int step = (int)Mathf.Repeat(_idleTimer / Mathf.Max(0.1f, idleFrameInterval), beats);
        int index = Mathf.Clamp(step < idleFrames.Length ? step : beats - step, 0, idleFrames.Length - 1);

        Sprite frame = idleFrames[index];
        return frame != null ? frame : idle;
    }

    /// <summary>行走：帧率跟着速度走，慢走不会「原地小跑」</summary>
    private Sprite WalkFrame(float vx)
    {
        float maxSpeed = controller != null ? Mathf.Max(0.1f, controller.MaxSpeed) : 8f;
        float ratio = Mathf.Clamp01(Mathf.Abs(vx) / maxSpeed);
        float fps = Mathf.Lerp(walkFpsMin, walkFpsMax, ratio);

        _runTimer += fps * Time.deltaTime;
        _idleTimer = 0f;

        int index = (int)Mathf.Repeat(_runTimer, walkFrames.Length);
        return walkFrames[index] != null ? walkFrames[index] : DefaultSprite;
    }

    /// <summary>走进一间已经清空的房间时举弓庆祝一下。纯表现</summary>
    private void CheckRoomCleared()
    {
        if (victory == null) return;

        if (_rooms == null) _rooms = FindObjectOfType<RoomManager>();
        if (_rooms == null) return;

        Room room = _rooms.CurrentRoom;
        if (room == null || room == _victoryRoom || !room.IsCleared) return;

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
        if (IsDead) return;
        _hurtTimer = hurtHold;
    }

    private void OnAttackStarted(AttackData data)
    {
        if (IsDead) return;

        // 被换成了近战武器就交给战士那套去演，射手别抢
        if (data != null && !data.isRanged) return;

        PlayShoot(shootHold);
    }

    /// <summary>
    /// 技能表现。技能是 PlayerAttacker 之外的另一条路，所以单独订阅 SkillCaster.SkillCast。
    /// 射手的三个输出技能都是「拉弓」，专注是「举弓蓄力」。
    /// </summary>
    private void OnSkillCast(SkillData skill)
    {
        if (IsDead || skill == null) return;

        switch (skill.castType)
        {
            case SkillCastType.Projectile:
            case SkillCastType.ArrowRain:
                PlayShoot(shootHold);
                break;

            case SkillCastType.Buff:
                PlayFocus(focusHold);
                break;
        }
    }

    // ---------------- 对外表现入口 ----------------

    /// <summary>播一次射击姿势（普通攻击 / 技能都能调）</summary>
    public void PlayShoot(float hold = -1f)
    {
        if (IsDead) return;

        _shootTotal = hold > 0f ? hold : shootHold;
        _shootTimer = _shootTotal;
    }

    /// <summary>猎人专注：举弓蓄力</summary>
    public void PlayFocus(float hold = -1f)
    {
        if (IsDead) return;
        _focusTimer = hold > 0f ? hold : focusHold;
    }

    // ---------------- IPlayerAnim ----------------

    /// <summary>
    /// 射手没有「转圈」的帧。真被塞了一个持续型近战技能（比如旋风斩）时，
    /// 用连续射击顶替 —— 至少姿势在动，不会整个人僵住。
    /// </summary>
    public void PlaySpin(float duration) => PlayShoot(Mathf.Max(duration, shootHold));

    /// <summary>战吼那类「举起来吼一声」的姿势，射手用举弓替代</summary>
    public void PlayWarcry(float hold) => PlayFocus(hold);

    /// <summary>射手没有蓄力技能（蓄力是法师火球的），空实现</summary>
    public void PlayCharge(bool on) { }

    /// <summary>施法姿势：射手当成一次射击/举弓</summary>
    public void PlayCast(float hold) => PlayFocus(hold);

    /// <summary>
    /// 掐断正在播的动作，回到待机。换房间时调 —— 原因见 <see cref="IPlayerAnim.CancelAction"/>。
    /// 举弓和射击的计时器一起清：站在门边放完箭雨进门，新房间里不该还举着弓。
    /// </summary>
    public void CancelAction()
    {
        _shootTimer   = 0f;
        _focusTimer   = 0f;
        _hurtTimer    = 0f;
        _victoryTimer = 0f;
        _runTimer     = 0f;
        _idleTimer    = 0f;

        Apply(DefaultSprite);
    }

    // ---------------- 工具 ----------------

    private static Sprite FirstOf(Sprite[] frames, Sprite fallback)
    {
        if (frames != null)
        {
            for (int i = 0; i < frames.Length; i++)
                if (frames[i] != null) return frames[i];
        }

        return fallback;
    }

    /// <summary>剔掉空槽位；全空时退化成单帧</summary>
    private static Sprite[] Compact(Sprite[] frames, Sprite fallback)
    {
        if (frames == null || frames.Length == 0)
            return fallback != null ? new[] { fallback } : new Sprite[0];

        int count = 0;
        for (int i = 0; i < frames.Length; i++)
            if (frames[i] != null) count++;

        if (count == 0) return fallback != null ? new[] { fallback } : new Sprite[0];

        var result = new Sprite[count];
        int at = 0;
        for (int i = 0; i < frames.Length; i++)
            if (frames[i] != null) result[at++] = frames[i];

        return result;
    }

    // ---------------- 自检接口 ----------------

    public Sprite[] DebugSingleFrames()
        => new[] { idle, jump, fall, dash, hurt, death, victory, focus };

    public Sprite[] DebugIdleFrames() => idleFrames;
    public Sprite[] DebugRunFrames() => walkFrames;
    public Sprite[] DebugWalkFrames() => walkFrames;
    public Sprite[] DebugShootFrames() => shootFrames;
    public Sprite[] DebugAttackFrames() => shootFrames;
}
