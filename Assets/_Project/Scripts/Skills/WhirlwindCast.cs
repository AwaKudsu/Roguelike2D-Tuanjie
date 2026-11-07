using UnityEngine;

/// <summary>
/// 持续型近战技能（旋风斩）。
///
/// 一次性的近战判定是「按下 → 打一个框 → 结束」，那对旋风斩来说不对：
/// 旋风斩应该是「在一段时间里一直在转」，所以敌人后走进圈里也会被扫到。
///
/// 这个组件由 SkillCaster 在放技能时挂到玩家身上，自己数时间、自己收尾：
///   · 每 tickInterval 秒调一次 PlayerAttacker.Execute —— 伤害、击退、目标层过滤全部复用普攻那份代码
///   · duration 到了就把自己 Destroy 掉（只删组件，不删玩家）
///
/// 转圈的刀光由 PlayerAttacker.PlaySlash 负责（判定框面积够大就放「绕身一圈」的弧）。
/// 这个组件额外管三件事，让「在转」这件事一眼能看出来：
///   · aura 贴图绕身旋转（一张空心风刃环，转起来就是漩涡）
///   · 起步与收尾各崩一下白色冲击（有开始有结束，不是无声无息地出现又消失）
///   · 整段时间里持续往外甩火花
/// </summary>
public class WhirlwindCast : MonoBehaviour
{
    private PlayerAttacker _attacker;
    private AttackData _attack;
    private float _duration = 1.2f;
    private float _tickInterval = 0.22f;

    private Color _color = new Color(1f, 0.94f, 0.75f, 1f);
    private float _scale = 1f;
    private Sprite _aura;

    private float _age;
    private float _tickTimer;
    private int _ticks;
    private bool _running;
    private FxPuff _auraFx;

    /// <summary>正在转（自检用）</summary>
    public bool IsRunning => _running;

    /// <summary>一共打了几下（自检用）</summary>
    public int Ticks => _ticks;

    public void Setup(PlayerAttacker attacker, AttackData attack, float duration, float tickInterval,
                      Color color = default, float scale = 1f, Sprite aura = null)
    {
        _attacker = attacker;
        _attack = attack;
        _duration = Mathf.Max(0.1f, duration);
        _tickInterval = Mathf.Max(0.05f, tickInterval);
        _color = color.a <= 0f ? new Color(1f, 0.94f, 0.75f, 1f) : color;
        _scale = scale <= 0f ? 1f : scale;
        _aura = aura;

        _age = 0f;
        _tickTimer = 0f;      // 第一帧就打一下，不能等第一个间隔
        _ticks = 0;
        _running = true;

        StartAura();
    }

    /// <summary>
    /// 漩涡贴图：挂成玩家的子物体，转 _duration 秒后由 FxPuff 自己销毁。
    /// 用 FxPuff 而不是自己写一份，是因为「转 + 淡出 + 定时删」这套逻辑已经在里面了。
    /// </summary>
    private void StartAura()
    {
        if (_aura == null) return;

        // 漩涡比人宽一圈（判定框大概就是那么大），压在角色和刀光之间
        float diameter = 2.6f * _scale;

        _auraFx = FxPuff.Spawn(
            _aura,
            transform.position,
            new Color(1f, 1f, 1f, 0.85f),
            life: _duration,
            startScale: diameter * 0.85f,   // 起来时就基本是满的，靠旋转和淡出说话，不是靠「扩散」
            endScale: diameter,
            spin: 420f,
            parent: transform,
            sortingOrder: SwordTrail.SortingOrder - 1);
    }

    /// <summary>
    /// 立刻收手（换房间时用）。
    /// 只 Destroy(this) 是不够的：漩涡贴图是挂成玩家子物体的，
    /// 不一起删掉的话，它会跟着人走进下一个房间继续转。
    /// </summary>
    public void StopNow()
    {
        _running = false;

        if (_auraFx != null) Destroy(_auraFx.gameObject);

        Destroy(this);
    }

    private void Update()
    {
        if (!_running) return;

        if (_attacker == null || _attack == null)
        {
            _running = false;
            Destroy(this);
            return;
        }

        _age += Time.deltaTime;

        if (_tickTimer <= 0f)
        {
            _tickTimer += _tickInterval;
            Tick();
        }

        _tickTimer -= Time.deltaTime;

        if (_age >= _duration)
        {
            _running = false;
            Destroy(this);     // 只摘掉这个组件，玩家本身不动
        }
    }

    private void Tick()
    {
        _ticks++;

        // 刀光不在这里放：PlayerAttacker.PlaySlash 看到判定框面积够大，本来就会自己放一圈 Spin 刀光。
        // 这里再放一遍会亮两倍、边缘糊成一片。
        _attacker.Execute(_attack);
    }
}
