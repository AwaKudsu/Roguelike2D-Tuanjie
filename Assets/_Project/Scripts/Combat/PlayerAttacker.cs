using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家普通攻击的执行者。
///
/// 它只做三件事：读输入 → 按 AttackData 决定「怎么打」→ 把 DamageInfo 交给所有 IDamageable。
///   · AttackData 的 Is Ranged 没勾 → 在身前打一个判定框（战士的剑）
///   · AttackData 的 Is Ranged 勾上 → 发射投射物（法师的法杖弹）
///
/// 「伤害多少」「打多大范围」来自 AttackData，「攻击力多高」来自 CharacterStats。
/// 所以同一个脚本既能当剑也能当法杖，区别只在资产里 —— 将来加武器也走这条路。
/// </summary>
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerAttacker : MonoBehaviour
{
    [Header("当前使用的攻击（将来由武器 / 装备系统切换）")]
    [SerializeField] private AttackData attackData;

    [Header("调试")]
    [Tooltip("每次攻击 / 技能打出去时，在 Scene 视图闪一下判定框。\n" +
             "亮黄色 = 普通攻击；亮青色 = 技能。闪完自动消失，不常驻。")]
    [SerializeField] private bool debugShowHitbox = true;

    [Tooltip("判定框闪多久（秒）。太短来不及把视线从 Game 视图挪过来，太长会挡住画面")]
    [SerializeField] private float debugShowDuration = 0.6f;

    [Header("挥砍表现")]
    [Tooltip("近战挥砍时飞出来的刀风贴图（战士素材里的 Warrior_SlashArc）。留空就没有刀风，不影响判定")]
    [SerializeField] private Sprite slashArc;

    [SerializeField] private Color slashColor = new Color(1f, 0.94f, 0.75f, 1f);

    [Tooltip("刀风大小倍率")]
    [SerializeField] private float slashScale = 1f;

    /// <summary>
    /// 每次攻击真正打出去时触发（远程也在内）。
    /// 外观层靠它切姿势 —— 判定和表现分开，删掉这个事件不影响战斗。
    /// </summary>
    public event Action<AttackData> AttackStarted;

    private PlayerInputReader _input;
    private CharacterStats _stats;
    private HitReaction _hitReaction;
    private CharacterVisual _visual;
    private float _cooldownTimer;
    private int _facing = 1;

    // 一次性的判定框闪示。每次攻击/技能打出去时由 ShowFlash() 记下位置、大小和到期时间，
    // 到点就自然不画了 —— 所以它显示的是「刚才那一下实际打在哪」，按下才出现、放完就消失。
    private Vector2 _flashCenter;
    private Vector2 _flashSize;
    private float _flashUntil;
    private bool _flashIsSkill;

    /// <summary>当前朝向：1 = 右，-1 = 左。技能系统和投射物都靠它决定往哪边打</summary>
    public int Facing => _facing;

    /// <summary>当前普通攻击用的数据。换武器时改的就是它</summary>
    public AttackData CurrentAttack => attackData;

    /// <summary>某个 AttackData 在「当前站位 + 当前朝向」下会打到哪里</summary>
    private Vector2 CenterFor(AttackData data)
    {
        return (Vector2)transform.position
             + new Vector2(data.hitboxOffset.x * _facing, data.hitboxOffset.y);
    }

    private void Awake()
    {
        _input       = GetComponent<PlayerInputReader>();
        _stats       = GetComponent<CharacterStats>();
        _hitReaction = GetComponent<HitReaction>();
        _visual      = GetComponent<CharacterVisual>();
    }

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        float x = _input.Move.x;
        if (Mathf.Abs(x) > 0.1f)
        {
            _facing = x > 0f ? 1 : -1;

            // 贴图朝向跟着走。没装 CharacterVisual 也不影响判定框 —— 它只管外观
            if (_visual != null) _visual.SetFacing(_facing);
        }

        if (_hitReaction != null && _hitReaction.IsStunned) return;

        if (_input.ConsumeAttackPressed() && _cooldownTimer <= 0f && attackData != null)
        {
            _cooldownTimer = GetCooldown(attackData);
            BasicAttack(attackData);
        }
    }

    /// <summary>换武器 / 换职业时改普通攻击形态，由装备系统调用</summary>
    public void SetAttack(AttackData data) => attackData = data;

    /// <summary>实际冷却 = 基础冷却 ÷ 攻速，再乘上冷却缩减</summary>
    public float GetCooldown(AttackData data)
    {
        if (_stats == null) return data.cooldown;

        float speed     = Mathf.Max(0.1f, _stats.Get(StatType.AttackSpeed));
        float reduction = Mathf.Clamp(_stats.Get(StatType.CooldownRate), 0f, 0.8f);

        return data.cooldown / speed * (1f - reduction);
    }

    /// <summary>按数据决定这次普通攻击走哪条路</summary>
    private void BasicAttack(AttackData data)
    {
        if (data.isRanged) Fire(data);
        else               Execute(data);
    }

    /// <summary>发射投射物（远程普通攻击）</summary>
    public void Fire(AttackData data)
    {
        Vector2 muzzle = ProjectileLauncher.MuzzlePosition(transform, _facing);

        ProjectileLauncher.Fire(
            data:   data,
            prefab: data.projectilePrefab,
            stats:  _stats,
            owner:  gameObject,
            origin: muzzle,
            facing: _facing);

        // 投射物本身没有判定框，就在枪口闪一个小方块当「打出去了」的反馈
        ShowFlash(muzzle, new Vector2(0.4f, 0.4f), data != attackData);

        AttackStarted?.Invoke(data);
    }

    /// <summary>
    /// 放一道刀风。大范围挥砍（旋风斩）让弧光绕身转一圈，普通挥砍是斜着扫一下。
    /// slashArc 没配就什么都不做。
    /// </summary>
    private void PlaySlash(Vector2 center, AttackData data)
    {
        if (slashArc == null) return;

        float scale = Mathf.Max(0.1f, slashScale);

        if (data.hitboxSize.x * data.hitboxSize.y >= 6f)
        {
            SlashEffect.Spin(slashArc, transform.position,
                             radius: Mathf.Max(data.hitboxSize.x, data.hitboxSize.y) * 0.28f,
                             scale: scale * 0.9f,
                             color: slashColor);
            return;
        }

        // 普通挥砍：弧光放在判定框中心偏后一点，看起来是从刀身上扫出去的
        Vector2 at = center - new Vector2(_facing * 0.25f, 0f);
        SlashEffect.Swing(slashArc, at, _facing, scale, slashColor);
    }

    /// <summary>
    /// 执行一次近战判定。技能系统调用的也是这个方法 ——
    /// 战士的旋风斩就是「换一个判定框更大的 AttackData，再调一次这里」。
    /// </summary>
    public void Execute(AttackData data)
    {
        Vector2 center = CenterFor(data);

        // 先通知外观层切姿势、再放刀风，最后才判定 ——
        // 这样即使表现层出问题（比如贴图没导入），伤害也照样打出去
        AttackStarted?.Invoke(data);
        PlaySlash(center, data);

        // 按下才算、闪完就没了：这里记的是这一下**实际**打在哪
        ShowFlash(center, data.hitboxSize, data != attackData);

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, data.hitboxSize, 0f, data.targetLayers);

        var alreadyHit = new HashSet<IDamageable>();

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (!hit.TryGetComponent(out IDamageable target)) continue;
            if (!target.IsAlive) continue;
            if (!alreadyHit.Add(target)) continue;

            // 伤害公式统一在 DamageCalculator 里 —— 普通攻击和各种技能共用同一份。
            // 注意这里**没有 break**：一次挥砍可以同时打中好几个敌人（敌人的攻击才只打一个）。
            target.TakeDamage(DamageCalculator.Build(_stats, data, gameObject, transform.position));
        }
    }

    /// <summary>在指定位置闪一下判定框，debugShowDuration 秒后自动消失</summary>
    private void ShowFlash(Vector2 center, Vector2 size, bool isSkill)
    {
        _flashCenter  = center;
        _flashSize    = size;
        _flashUntil   = Time.time + debugShowDuration;
        _flashIsSkill = isSkill;
    }

    // ---------------- 调试可视化 ----------------

    private void OnDrawGizmos()
    {
        if (!debugShowHitbox) return;
        if (!Application.isPlaying) return;
        if (Time.time >= _flashUntil) return;   // 已经过期 —— 打过的那一下早就闪完了，什么都不画

        // 亮黄色 = 普通攻击；亮青色 = 技能。这样一眼就能分清刚才那下是哪个
        Color c = _flashIsSkill
            ? new Color(0.3f, 1f, 0.9f)
            : new Color(1f, 0.9f, 0.2f);

        Gizmos.color = new Color(c.r, c.g, c.b, 0.25f);
        Gizmos.DrawCube(_flashCenter, _flashSize);

        Gizmos.color = new Color(c.r, c.g, c.b, 1f);
        Gizmos.DrawWireCube(_flashCenter, _flashSize);
    }

    private void OnDrawGizmosSelected()
    {
        // 选中 Player 时额外画一条远程射击方向线（近战没有方向线可画，
        // 它的判定框由 OnDrawGizmos 里的闪框负责）
        if (!Application.isPlaying) return;
        if (attackData == null || !attackData.isRanged) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        Vector2 from = ProjectileLauncher.MuzzlePosition(transform, _facing);
        Gizmos.DrawLine(from, from + new Vector2(_facing * 2f, 0f));
    }
}
