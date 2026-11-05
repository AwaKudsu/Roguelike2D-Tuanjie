using UnityEngine;

/// <summary>
/// 战士的贴图动画：把角色当前状态翻译成一帧贴图。
///
/// 为什么不用 Animator：工程里还没有任何 .controller / .anim 资产，
/// 而状态判断本身全在代码里（速度、是否落地、有没有在挥砍）。
/// 用一个组件直读状态，比手搭 9 个状态 + 30 条过渡更不容易错，
/// 也让「不按 Play 也能自检」这件事可行 —— Animator 的过渡在批处理里验不了。
///
/// 2026-11-05 改版：单帧升级成**短序列**。
/// 以前一刀只有一张姿势，看着是「啪」地换图；现在起手 / 命中 / 收招各一帧，
/// 待机有呼吸、跑动 6 帧一循环、旋风斩两帧交替。素材还是那 16 张，
/// 新帧是用变换派生的（见 docs/16-更多动作帧.md）。
///
/// 优先级（上面的赢）：死亡 &gt; 受击 &gt; 旋风斩 &gt; 攻击 &gt; 冲刺 &gt; 胜利 &gt; 空中 &gt; 跑 &gt; 待机
/// </summary>
[DisallowMultipleComponent]
public class WarriorVisual : MonoBehaviour, IPlayerAnim
{
    [Header("引用")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private PlayerController controller;
    [SerializeField] private Health health;
    [SerializeField] private PlayerAttacker attacker;
    [SerializeField] private Rigidbody2D body;

    [Header("待机 / 空中 / 状态单帧")]
    [SerializeField] private Sprite idle;
    [SerializeField] private Sprite jump;
    [SerializeField] private Sprite fall;
    [SerializeField] private Sprite dash;
    [SerializeField] private Sprite hurt;
    [SerializeField] private Sprite death;

    [Header("待机序列（呼吸：站稳 → 微抬 → 抬更多 → 微抬）")]
    [SerializeField] private Sprite[] idleFrames = new Sprite[0];
    [SerializeField] private float idleFrameInterval = 0.62f;

    [Header("攻击序列：起手 / 命中 / 收招")]
    [SerializeField] private Sprite attackSlash;
    [SerializeField] private Sprite attackLift;
    [SerializeField] private Sprite attackHeavy;
    [SerializeField] private Sprite attackThrust;
    [SerializeField] private Sprite[] slashFrames = new Sprite[0];
    [SerializeField] private Sprite[] liftFrames = new Sprite[0];
    [SerializeField] private Sprite[] thrustFrames = new Sprite[0];

    [Header("技能姿势")]
    [Tooltip("战吼：举剑怒吼的那一帧")]
    [SerializeField] private Sprite warcry;
    [Tooltip("旋风斩轮换的两帧（蹲扫 / 举砍），交替播就像在原地转")]
    [SerializeField] private Sprite[] spinFrames = new Sprite[0];
    [SerializeField] private float spinFrameInterval = 0.11f;

    [Header("跑动序列")]
    [SerializeField] private Sprite[] run = new Sprite[0];

    [Tooltip("慢走时的帧率。定速播放会显得「原地小跑」，所以慢的时候要慢下来")]
    [SerializeField] private float runFpsMin = 10f;

    [Tooltip("全速时的帧率")]
    [SerializeField] private float runFpsMax = 20f;

    [Header("节奏（秒）")]
    [Tooltip("普通挥砍整段保持多久。太长会盖住跑动，太短看不清姿势")]
    [SerializeField] private float attackHold = 0.22f;
    [SerializeField] private float liftHold = 0.24f;
    [SerializeField] private float thrustHold = 0.20f;
    [SerializeField] private float heavyHold = 0.26f;
    [SerializeField] private float hurtHold = 0.22f;

    [Header("判定")]
    [Tooltip("水平速度超过这个值就算在跑")]
    [SerializeField] private float runSpeedThreshold = 0.6f;

    /// <summary>
    /// 三段序列的帧时长比例：起手短、命中长、收招中等。
    /// 平均分的话「命中」那一帧只闪 1/3 的时间，看不清楚打到没有。
    /// </summary>
    private static readonly float[] PoseWeights = { 0.30f, 0.42f, 0.28f };

    private Sprite[] _clip;          // 正在播的序列
    private int _clipIndex;
    private float _clipFrameTime;    // 当前这一帧还剩多久
    private float _clipTotal;        // 整段时长，用来按比例算每帧

    private float _hurtTimer;
    private float _spinTimer;
    private float _spinClock;
    private float _runTimer;
    private float _idleTimer;
    private bool _dead;

    /// <summary>当前这一帧用的是哪个状态，给自检和调试看</summary>
    public string CurrentState { get; private set; } = "Idle";

    /// <summary>旋风斩是不是正在播</summary>
    public bool IsSpinning => _spinTimer > 0f;

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
        Apply(FirstOf(idleFrames, idle));
    }

    private void Update()
    {
        TickTimers();
        Apply(ChooseFrame());
    }

    private void TickTimers()
    {
        if (_hurtTimer > 0f) _hurtTimer -= Time.deltaTime;

        if (_spinTimer > 0f)
        {
            _spinTimer -= Time.deltaTime;
            _spinClock += Time.deltaTime;
            if (_spinTimer <= 0f) _spinClock = 0f;
        }

        AdvanceClip(Time.deltaTime);
    }

    /// <summary>
    /// 推进攻击序列。按 PoseWeights 分配每帧时长，播完就放手让状态机接管。
    /// </summary>
    private void AdvanceClip(float dt)
    {
        if (_clip == null) return;

        _clipFrameTime -= dt;
        while (_clipFrameTime <= 0f && _clip != null)
        {
            _clipIndex++;
            if (_clip.Length == 0 || _clipIndex >= _clip.Length)
            {
                _clip = null;
                _clipIndex = 0;
                return;
            }

            _clipFrameTime += _clipTotal * WeightOf(_clipIndex, _clip.Length);
        }
    }

    private static float WeightOf(int index, int length)
    {
        if (length >= PoseWeights.Length) return PoseWeights[Mathf.Min(index, PoseWeights.Length - 1)];
        return 1f / Mathf.Max(1, length);
    }

    private void PlayClip(Sprite[] frames, float total, Sprite fallback)
    {
        Sprite[] usable = Compact(frames, fallback);
        _clip = usable;
        _clipIndex = 0;
        _clipTotal = Mathf.Max(0.05f, total);
        _clipFrameTime = _clipTotal * WeightOf(0, usable.Length);
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

        // 旋风斩盖住攻击：转的时候每 0.2 秒都在 Execute，不能让攻击姿势把旋转盖掉
        if (_spinTimer > 0f && spinFrames != null && spinFrames.Length > 0)
        {
            CurrentState = "Spin";
            int index = (int)(_spinClock / Mathf.Max(0.04f, spinFrameInterval)) % spinFrames.Length;
            Sprite frame = spinFrames[index];
            return frame != null ? frame : idle;
        }

        if (_clip != null && _clip.Length > 0 && _clip[_clipIndex] != null)
        {
            CurrentState = "Attack";
            return _clip[_clipIndex];
        }

        // 冲刺优先于跑动：窜出去的瞬间不该还在踩步子
        if (controller != null && controller.IsDashing && dash != null)
        {
            CurrentState = "Dash";
            return dash;
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
            return RunFrame(vx);
        }

        _runTimer = 0f;
        return IdleFrame();
    }

    /// <summary>待机：三帧呼吸循环，0 → 1 → 2 → 1</summary>
    private Sprite IdleFrame()
    {
        if (idleFrames == null || idleFrames.Length == 0)
        {
            CurrentState = "Idle";
            return idle;
        }

        CurrentState = "Idle";
        _idleTimer += Time.deltaTime;

        int beats = idleFrames.Length <= 2 ? idleFrames.Length : (idleFrames.Length - 1) * 2;
        int step = (int)Mathf.Repeat(_idleTimer / Mathf.Max(0.1f, idleFrameInterval), beats);
        int index = step < idleFrames.Length ? step : beats - step;
        index = Mathf.Clamp(index, 0, idleFrames.Length - 1);

        Sprite frame = idleFrames[index];
        return frame != null ? frame : idle;
    }

    /// <summary>
    /// 跑动帧。
    ///
    /// 现在的序列是 6 帧：四张原姿势 + 两张「落地压缩」（往下压 2px、纵向 0.98），
    /// 压缩帧插在两次落地之间，跑起来才有重量，不会像贴图在飘。
    ///
    /// 帧率仍然跟着速度走：慢走 10 帧/秒、全速 20 帧/秒。
    /// </summary>
    private Sprite RunFrame(float vx)
    {
        float maxSpeed = controller != null ? Mathf.Max(0.1f, controller.MaxSpeed) : 6f;
        float ratio = Mathf.Clamp01(Mathf.Abs(vx) / maxSpeed);
        float fps = Mathf.Lerp(runFpsMin, runFpsMax, ratio);

        _runTimer += fps * Time.deltaTime;      // 直接累计「已经走过几个节拍」

        int index = (int)Mathf.Repeat(_runTimer, run.Length);
        _idleTimer = 0f;

        return run[index] != null ? run[index] : idle;
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
        if (_spinTimer > 0f) return;        // 转着呢，别被每 0.2 秒一次的 Execute 打断

        if (data.isRanged)
        {
            // 远程：把剑递出去，剑尖朝前
            PlayClip(thrustFrames, thrustHold, attackThrust != null ? attackThrust : attackSlash);
        }
        else if (IsWideAttack(data))
        {
            // 旋风斩那类大范围挥砍：蹲身横扫的姿势更贴
            PlayClip(null, heavyHold, attackHeavy);
        }
        else
        {
            // 三段连击：劈砍 → 上挑 → 突刺，一段一个序列
            int stage = attacker != null ? attacker.ComboStage : 0;

            switch (stage)
            {
                case 1:
                    PlayClip(liftFrames, liftHold, attackLift != null ? attackLift : attackSlash);
                    break;

                case 2:
                    PlayClip(thrustFrames, thrustHold, attackThrust != null ? attackThrust : attackSlash);
                    break;

                default:
                    PlayClip(slashFrames, attackHold, attackSlash);
                    break;
            }
        }
    }

    private static bool IsWideAttack(AttackData data)
    {
        Vector2 size = data.hitboxSize;
        return size.x * size.y >= 6f;
    }

    // ---------------- 给技能调用的表现入口 ----------------

    /// <summary>旋风斩：转多久就轮换多久的两帧动作</summary>
    public void PlaySpin(float duration)
    {
        if (_dead || duration <= 0f) return;
        _spinTimer = duration;
        _spinClock = 0f;
        _clip = null;
    }

    /// <summary>战吼：举剑怒吼一下（技能本身是瞬发的，这里只是把姿势按住一会儿）</summary>
    public void PlayWarcry(float hold = 0.45f)
    {
        if (_dead) return;
        PlayClip(new[] { warcry }, hold, attackHeavy);
    }

    /// <summary>战士没有蓄力招式：空实现，让 SkillCaster 不必区分职业</summary>
    public void PlayCharge(bool on)
    {
    }

    /// <summary>施法姿态：战士没有法杖，借用一次举剑动作顶替</summary>
    public void PlayCast(float hold)
    {
        PlayWarcry(hold);
    }

    // ---------------- 工具 ----------------

    private static Sprite FirstOf(Sprite[] frames, Sprite fallback)
    {
        if (frames != null)
        {
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] != null) return frames[i];
            }
        }

        return fallback;
    }

    /// <summary>把空槽位剔掉，槽位全空时退化成单帧</summary>
    private static Sprite[] Compact(Sprite[] frames, Sprite fallback)
    {
        if (frames == null || frames.Length == 0)
        {
            return fallback != null ? new[] { fallback } : new Sprite[0];
        }

        int count = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null) count++;
        }

        if (count == 0) return fallback != null ? new[] { fallback } : new Sprite[0];

        var result = new Sprite[count];
        int at = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null) result[at++] = frames[i];
        }

        return result;
    }

    // ---------------- 自检入口 ----------------

    /// <summary>给自检用：把每套单帧都读出来，确认接线没漏</summary>
    public Sprite[] DebugFrames()
    {
        return new[]
        {
            idle, jump, fall, dash, hurt, death,
            attackSlash, attackLift, attackHeavy, attackThrust, warcry
        };
    }

    /// <summary>给自检用：跑动序列</summary>
    public Sprite[] DebugRunFrames() => run;

    /// <summary>给自检用：待机序列</summary>
    public Sprite[] DebugIdleFrames() => idleFrames;

    /// <summary>给自检用：三段攻击序列</summary>
    public Sprite[] DebugSlashFrames() => slashFrames;

    public Sprite[] DebugLiftFrames() => liftFrames;

    public Sprite[] DebugThrustFrames() => thrustFrames;

    public Sprite[] DebugSpinFrames() => spinFrames;
}
