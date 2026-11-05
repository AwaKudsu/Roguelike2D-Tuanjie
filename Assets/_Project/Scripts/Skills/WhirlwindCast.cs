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
/// 转圈的美术表现不用这里管：PlaySlash 看到判定框面积够大就会自己放「绕身一圈」的弧光。
/// </summary>
public class WhirlwindCast : MonoBehaviour
{
    private PlayerAttacker _attacker;
    private AttackData _attack;
    private float _duration = 1.2f;
    private float _tickInterval = 0.22f;

    private float _age;
    private float _tickTimer;
    private int _ticks;
    private bool _running;

    /// <summary>正在转（自检用）</summary>
    public bool IsRunning => _running;

    /// <summary>一共打了几下（自检用）</summary>
    public int Ticks => _ticks;

    public void Setup(PlayerAttacker attacker, AttackData attack, float duration, float tickInterval,
                      Sprite arc = null, Color color = default, float scale = 1f)
    {
        _attacker = attacker;
        _attack = attack;
        _duration = Mathf.Max(0.1f, duration);
        _tickInterval = Mathf.Max(0.05f, tickInterval);

        _age = 0f;
        _tickTimer = 0f;      // 第一帧就打一下，不能等第一个间隔
        _ticks = 0;
        _running = true;
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
        _attacker.Execute(_attack);
    }
}
