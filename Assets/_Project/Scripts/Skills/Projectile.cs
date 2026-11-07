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

    [Tooltip("朝左飞时水平翻转贴图。素材里的火球是「球在右、拖尾在左」—— 是朝右设计的，不翻转就是倒着飞")]
    [SerializeField] private bool mirrorToDirection = true;

    [Tooltip("要翻转的贴图。留空会自动取子物体里的 SpriteRenderer")]
    [SerializeField] private SpriteRenderer body;

    [Tooltip("命中后延迟多久销毁，留给爆炸特效")]
    [SerializeField] private float destroyDelay = 0f;

    [Header("特效（法师的弹道、火球共用）")]
    [Tooltip("命中时炸开的那张图。留空就用本体图缩一下")]
    [SerializeField] private Sprite impactSprite;

    [Tooltip("命中特效的染色。火球偏橙、法杖弹偏青")]
    [SerializeField] private Color impactColor = new Color(1f, 0.86f, 0.55f, 1f);

    [Tooltip("飞行时留下拖尾。每 trailSpacing 个世界单位在身后留一张会缩小的残影")]
    [SerializeField] private bool leaveTrail = true;

    [Tooltip("每隔多少世界单位留一张残影。按距离而不是按时间 —— " +
             "按时间的话，弹速快的地方拖成虚线、慢的地方挤成一团疙瘩")]
    [SerializeField] private float trailSpacing = 0.4f;

    [Tooltip("本体自转速度（度/秒）。法师的奥术球要自转，火球和箭不用")]
    [SerializeField] private float spinSpeed = 0f;

    [Tooltip("出生后这段时间内不吃「撞墙」判定。大球的碰撞体可能一出生就压在 Ground 上，" +
             "不放过这一小段就会被判定成撞墙、当场消失（表现是「蓄力越久越没伤害」）")]
    [SerializeField] private float obstacleGrace = 0.08f;

    [Tooltip("碰撞体最多放大到几倍。贴图可以随蓄力无限变大，但碰撞体一味跟着长，" +
             "半径一旦超过离地高度就会一直蹭着地板走 —— 撞墙就失效了。")]
    [SerializeField] private float maxColliderScale = 1.6f;

    private Rigidbody2D _rb;
    private CircleCollider2D _circle;
    private float _baseRadius;
    private DamageInfo _damage;
    private float _lifeTimer;
    private int _hitCount;
    private bool _launched;
    private bool _dead;
    private float _spawnTime;
    private Vector3 _lastTrailPos;
    private SpriteRenderer _body;

    // 穿透时同一只怪身上可能挂着好几个碰撞体，靠它保证一只怪只吃一次伤害
    private readonly HashSet<IDamageable> _alreadyHit = new();

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();

        // 投射物自己飞，不受重力和外力影响 —— 刚体设成 Kinematic 最省心
        _rb.gravityScale = 0f;

        _circle = GetComponent<CircleCollider2D>();
        if (_circle != null) _baseRadius = _circle.radius;

        _body = body != null ? body : GetComponentInChildren<SpriteRenderer>();

        // 弹道是靠刚体速度在 FixedUpdate 里推进的：不插值的话，
        // 屏幕刷新率比物理高时看到的就是「一帧一动」的跳格 —— 越快越明显。
        // 这里强制打开插值，不依赖预制体上有没有勾。
        if (_rb != null) _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        EnsureSpinPivot();
    }

    /// <summary>
    /// 自转要转「哪个物体」很重要：刚体开了插值之后，物体的 transform 每帧都会被
    /// 物理插值的结果覆盖 —— 在根节点上手动转，等于每帧和引擎抢一次，画面就是一顿一顿的。
    /// 所以贴图直接挂在根上的预制体，这里给它垫一个空子物体当转轴，转的是子物体。
    /// </summary>
    private void EnsureSpinPivot()
    {
        if (_body == null || spinSpeed == 0f) return;
        if (_body.transform != transform) return;

        var pivot = new GameObject("SpinPivot");
        pivot.transform.SetParent(transform, false);

        var copy = pivot.AddComponent<SpriteRenderer>();
        copy.sprite = _body.sprite;
        copy.sharedMaterial = _body.sharedMaterial;
        copy.sortingLayerID = _body.sortingLayerID;
        copy.sortingOrder = _body.sortingOrder;
        copy.flipX = _body.flipX;
        copy.enabled = true;

        _body.enabled = false;
        _body = copy;
    }

    /// <summary>本体贴图当前的世界大小（估算用：精灵宽度 / PPU × 缩放）</summary>
    private float BodyWorldSize()
    {
        if (_body == null || _body.sprite == null) return 0.6f;
        float ppu = Mathf.Max(1f, _body.sprite.pixelsPerUnit);
        return _body.sprite.rect.width / ppu * Mathf.Abs(_body.transform.lossyScale.x);
    }

    /// <summary>由 SkillCaster 调用，把这一发打出去</summary>
    /// <param name="direction">飞行方向，内部会自动归一化</param>
    /// <param name="damage">发射瞬间就算好的伤害，飞行途中不再改变</param>
    /// <param name="targets">能打到的层，来自技能的 AttackData</param>
    /// <param name="sizeScale">整体缩放倍率，蓄力火球靠它变大（碰撞体也跟着变）</param>
    /// <param name="speedScale">速度倍率，蓄得越久飞得越快</param>
    public void Launch(Vector2 direction, DamageInfo damage, LayerMask targets, float sizeScale = 1f, float speedScale = 1f)
    {
        // 兜底：正常流程里 Awake 已经取过了，但实例化顺序不受控时可能还没跑
        if (_rb == null) _rb = GetComponent<Rigidbody2D>();

        _damage    = damage;
        _lifeTimer = lifeTime;
        _launched  = true;
        _spawnTime = Time.time;

        // 目标层统一由 AttackData 决定，避免「预制体忘了勾 Layer」这种静默失效
        if (targets.value != 0) targetLayers = targets;

        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        if (sizeScale > 0f && !Mathf.Approximately(sizeScale, 1f))
        {
            // 缩放整个物体：CircleCollider2D 跟着变大，所以大球天生更容易撞到人
            transform.localScale *= sizeScale;

            // 贴图可以随蓄力一直变大，但碰撞体不能。半径一旦超过球心离地高度，
            // 这颗球就会一直蹭着地板飞 —— 一撞墙就 Expire，蓄得越大越打不出伤害。
            // 所以：贴图吃满 sizeScale，碰撞体只吃到 maxColliderScale。
            if (_circle == null) _circle = GetComponent<CircleCollider2D>();
            if (_circle != null)
            {
                if (_baseRadius <= 0f) _baseRadius = _circle.radius;
                float colliderScale = Mathf.Min(sizeScale, maxColliderScale);
                _circle.radius = _baseRadius * colliderScale / sizeScale;
            }

            // 只放大贴图不放大碰撞体会「看着打中了却没伤害」，反过来更糟；
            // 所以这里两者一起缩放，判定和画面永远一致。
            _lifeTimer = lifeTime / Mathf.Max(0.5f, speedScale);   // 飞得快，存活时间同比缩短，射程不变
        }

        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // 贴图朝向跟着飞行方向走：火球素材是「球在右、拖尾在左」的朝右画法，
        // 往左打时不翻转就会看着像倒着飞 —— 表现和法师面朝对不上。
        if (mirrorToDirection)
        {
            if (_body == null) _body = GetComponentInChildren<SpriteRenderer>();
            if (_body != null) _body.flipX = direction.x < 0f;
        }

        _rb.velocity = direction * speed * Mathf.Max(0.1f, speedScale);
        _lastTrailPos = transform.position;

        SpawnMuzzleFlash(direction, sizeScale);
    }

    /// <summary>
    /// 出膛的那一下：在枪口（法杖头）崩一小团光，朝飞行方向拉长。
    /// 没有它的话，弹道是「凭空出现在身前半米」—— 前摇和弹道之间少了一帧连接。
    /// </summary>
    private void SpawnMuzzleFlash(Vector2 direction, float sizeScale)
    {
        Sprite sprite = _body != null ? _body.sprite : null;
        if (sprite == null) return;

        float size = BodyWorldSize();
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Vector3 at = transform.position - (Vector3)(direction * size * 0.35f);

        FxPuff.Spawn(
            sprite,
            at,
            new Color(1f, 1f, 1f, 0.85f),
            life: 0.09f,
            startScale: size * 1.15f * sizeScale,
            endScale: size * 0.45f,
            rotationZ: angle,
            sortingOrder: SwordTrail.SortingOrder + 2);
    }

    private void Update()
    {
        if (!_launched || _dead) return;

        _lifeTimer -= Time.deltaTime;
        if (_lifeTimer <= 0f)
        {
            Expire(false);
            return;
        }

        // 奥术球这类「能量团」靠自转显出「东西是活的」；
        // 转的是本体渲染器而不是整个物体，免得圆形碰撞体和朝向一起转
        if (spinSpeed != 0f && _body != null)
        {
            _body.transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime, Space.Self);
        }

        if (leaveTrail) EmitTrail();
    }

    /// <summary>
    /// 拖尾：每隔一段「距离」在当前位置留一张「会缩小的本体图」，几帧之后自己消失。
    /// 比给预制体挂 TrailRenderer 省事 —— 火球、法杖弹、箭全都有，不用各配一份材质。
    ///
    /// 间距是按距离算的，不是按时间：按时间留尾，飞得快的地方一段段断成虚线，
    /// 飞得慢的地方则在同一处堆成一团疙瘩 —— 同一颗球在快慢变化时看起来就会「一顿一顿」。
    /// </summary>
    private void EmitTrail()
    {
        Vector3 now = transform.position;
        float spacing = Mathf.Max(0.02f, trailSpacing);
        if ((now - _lastTrailPos).sqrMagnitude < spacing * spacing) return;

        _lastTrailPos = now;

        Sprite sprite = _body != null ? _body.sprite : null;
        if (sprite == null) return;

        float size = BodyWorldSize();

        // 不挂父物体：本体可能因为蓄力被缩放过，挂上去残影会跟着一起变
        FxPuff.Spawn(
            sprite,
            now,
            new Color(1f, 1f, 1f, 0.30f),
            life: 0.16f,
            startScale: size * 0.90f,
            endScale: size * 0.34f,
            spin: spinSpeed,
            sortingOrder: SwordTrail.SortingOrder + 1);
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
            // 刚出生的一小会儿不吃墙：大球的碰撞体可能正压在脚底那块地板上
            if (Time.time - _spawnTime < obstacleGrace) return;

            Expire(true);
            return;
        }

        if ((targetLayers.value & layerBit) == 0) return;
        if (!other.TryGetComponent(out IDamageable target)) return;
        if (!target.IsAlive) return;
        if (!_alreadyHit.Add(target)) return;      // 这只怪已经被这一发打过了

        target.TakeDamage(_damage);

        _hitCount++;

        bool shouldStop = !pierce || (maxHits > 0 && _hitCount >= maxHits);
        if (shouldStop) Expire(true);
    }

    private void Expire(bool withImpact)
    {
        if (_dead) return;

        _dead = true;
        _rb.velocity = Vector2.zero;
        _rb.simulated = false;      // 关掉物理，避免等销毁的这几帧继续触发碰撞

        if (withImpact) SpawnImpact();

        Destroy(gameObject, destroyDelay);
    }

    /// <summary>命中时炸开的一团：撞墙是小的，打到人才是大的。</summary>
    private void SpawnImpact()
    {
        Sprite sprite = impactSprite != null ? impactSprite : (_body != null ? _body.sprite : null);
        if (sprite == null) return;

        float size = BodyWorldSize();

        FxPuff.Spawn(
            sprite,
            transform.position,
            impactColor,
            life: 0.22f,
            startScale: size * 0.7f,
            endScale: size * 1.9f,
            spin: UnityEngine.Random.Range(-60f, 60f),
            sortingOrder: SwordTrail.SortingOrder + 2);
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
