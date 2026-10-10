using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 飞行道具：火球、箭、飞刀都用这一个脚本，配不同外观就是不同的东西。
///
/// 关键设计：投射物**不自己算伤害**。
/// 发射的那一瞬间，SkillCaster 就把算好的 DamageInfo 塞进来 —— 因为
/// 「谁打的、攻击力多少、这一下暴击了没有」只有扣下扳机那一刻才知道。
/// 于是投射物只需要关心三件事：飞、撞、消失。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [Header("飞行")]
    [SerializeField] private float speed = 14f;

    [Tooltip("飞多久自动消失。防止打空的火球一直飞到天边")]
    [SerializeField] private float lifeTime = 1.2f;

    [Header("命中")]
    [Tooltip("能打到的层。⚠️ 由发射者从 AttackData 覆盖，一般不用在这里手填")]
    [SerializeField] private LayerMask targetLayers;

    [Tooltip("⚠️ 撞到就消失的层（地面、墙）。留空的话火球会直接穿墙飞出去")]
    [SerializeField] private LayerMask obstacleLayers;

    [Header("穿透")]
    [Tooltip("勾上后命中不消失，继续往前飞")]
    [SerializeField] private bool pierce = false;

    [Tooltip("穿透时最多打几个目标，0 = 不限")]
    [SerializeField] private int maxHits = 0;

    [Header("表现")]
    [Tooltip("生成时朝飞行方向旋转。箭、飞刀要勾；火球一般不用")]
    [SerializeField] private bool rotateToDirection = false;

    [Tooltip("命中后延迟多久销毁，留给爆炸特效")]
    [SerializeField] private float destroyDelay = 0f;

    private Rigidbody2D _rb;
    private DamageInfo _damage;
    private float _lifeTimer;
    private int _hitCount;
    private bool _launched;
    private bool _dead;

    // 穿透时同一只怪身上可能挂着好几个碰撞体，靠它保证一只怪只吃一次伤害
    private readonly HashSet<IDamageable> _alreadyHit = new();

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();

        // 投射物自己飞，不受重力和外力影响 —— 刚体设成 Kinematic 最省心
        _rb.gravityScale = 0f;
    }

    /// <summary>由 SkillCaster 调用，把这一发打出去</summary>
    /// <param name="direction">飞行方向，内部会自动归一化</param>
    /// <param name="damage">发射瞬间就算好的伤害，飞行途中不再改变</param>
    /// <param name="targets">能打到的层，来自技能的 AttackData</param>
    public void Launch(Vector2 direction, DamageInfo damage, LayerMask targets)
    {
        _damage    = damage;
        _lifeTimer = lifeTime;
        _launched  = true;

        // 目标层统一由 AttackData 决定，避免「预制体忘了勾 Layer」这种静默失效
        if (targets.value != 0) targetLayers = targets;

        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        _rb.velocity = direction * speed;
    }

    private void Update()
    {
        if (!_launched || _dead) return;

        _lifeTimer -= Time.deltaTime;
        if (_lifeTimer <= 0f) Expire();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_launched || _dead) return;

        // 别打到自己人。挂在发射者身上的碰撞体也会触发 OnTrigger，所以要挡一下
        if (other.gameObject == _damage.Attacker) return;

        int layerBit = 1 << other.gameObject.layer;

        // 墙优先判断：撞墙永远停下来，哪怕这一层同时也在目标层里
        if ((obstacleLayers.value & layerBit) != 0)
        {
            Expire();
            return;
        }

        if ((targetLayers.value & layerBit) == 0) return;
        if (!other.TryGetComponent(out IDamageable target)) return;
        if (!target.IsAlive) return;
        if (!_alreadyHit.Add(target)) return;      // 这只怪已经被这一发打过了

        target.TakeDamage(_damage);

        _hitCount++;

        bool shouldStop = !pierce || (maxHits > 0 && _hitCount >= maxHits);
        if (shouldStop) Expire();
    }

    private void Expire()
    {
        if (_dead) return;

        _dead = true;
        _rb.velocity = Vector2.zero;
        _rb.simulated = false;      // 关掉物理，避免等销毁的这几帧继续触发碰撞

        Destroy(gameObject, destroyDelay);
    }

    private void OnValidate()
    {
        // LayerMask 留空是「静默失效」——游戏照跑，火球只是穿墙飞出去，
        // Console 一句提示都没有。所以这里主动喊一声。
        if (obstacleLayers.value == 0)
            Debug.LogWarning($"[Projectile]「{name}」的 Obstacle Layers 是空的，它会穿墙飞出去。记得勾上 Ground", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        if (_rb == null) return;

        Vector2 v = _rb.velocity;
        if (v.sqrMagnitude < 0.0001f) return;

        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
        Gizmos.DrawLine(transform.position, (Vector2)transform.position + v.normalized * 1.5f);
    }
}
