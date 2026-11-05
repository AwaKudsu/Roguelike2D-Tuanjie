using UnityEngine;

/// <summary>
/// 回复技能的表现层：以角色为圆心撑开一个金色法阵，在持续时间里一段一段地回血。
///
/// 三个设计点：
///   · **回血是「流」不是「一口」** —— 每隔 healTickInterval 跳一次，飘一条绿字。
///     一次回满会让玩家觉得「按一下就满了」，分几次才有「正在恢复」的过程感。
///   · **法阵用椭圆压扁**（y 缩到 55%）来假装地面透视 —— 完全是平的圆环悬浮在胸前会像贴在屏幕上的贴纸。
///   · **法阵只负责表现和计时**，真正加血调 Health.Heal，所以它不需要知道玩家有没有装备、有没有词条。
/// </summary>
public class HealCircle : MonoBehaviour
{
    [Tooltip("法阵贴图（金色空心圆）。留空就只回血、不放特效")]
    public Sprite circleSprite;

    public Color circleColor = new Color(1f, 0.86f, 0.45f, 0.95f);

    /// <summary>法阵最大直径（世界单位）</summary>
    public float diameter = 3.2f;

    /// <summary>每次回多少血</summary>
    public float healPerTick = 6f;

    /// <summary>持续多久</summary>
    public float duration = 2.4f;

    /// <summary>每隔多久回一次</summary>
    public float tickInterval = 0.3f;

    public Color numberColor = new Color(0.45f, 1f, 0.55f, 1f);

    /// <summary>一共回了多少血（自检和 HUD 都可能想看）</summary>
    public float TotalHealed { get; private set; }

    /// <summary>跳了几次</summary>
    public int Ticks { get; private set; }

    private Health _health;
    private Transform _caster;
    private SpriteRenderer _ring;
    private float _age;
    private float _tickTimer;
    private bool _finished;

    /// <summary>
    /// 放一个法阵并开始回血。caster 是跟着走的角色（一般就是玩家自己）。
    /// </summary>
    public static HealCircle Play(Health health, Transform caster, Sprite sprite,
                                  float healPerTick, float duration, float tickInterval, float diameter,
                                  Color ringColor, Color numberColor)
    {
        if (health == null || caster == null) return null;

        var go = new GameObject("__HealCircle");
        var circle = go.AddComponent<HealCircle>();
        circle.Setup(health, caster, sprite, healPerTick, duration, tickInterval, diameter, ringColor, numberColor);
        return circle;
    }

    private void Setup(Health health, Transform caster, Sprite sprite,
                       float perTick, float total, float interval, float size,
                       Color ringColor, Color numberColor)
    {
        _health = health;
        _caster = caster;
        circleSprite = sprite;
        circleColor = ringColor;
        healPerTick = perTick;
        duration = Mathf.Max(0.1f, total);
        tickInterval = Mathf.Max(0.05f, interval);
        diameter = Mathf.Max(0.1f, size);
        this.numberColor = numberColor;

        BuildRing();

        // 第一下马上回，不然按下技能要先等一个间隔才有反馈
        _tickTimer = 0f;
    }

    private void BuildRing()
    {
        if (circleSprite == null) return;

        var go = new GameObject("Ring");
        go.transform.SetParent(transform, false);

        _ring = go.AddComponent<SpriteRenderer>();
        _ring.sprite = circleSprite;
        _ring.color = circleColor;
        _ring.sortingOrder = 18;                    // 压在角色(20)下面的地面上
        go.transform.localScale = new Vector3(0.2f, 0.11f, 1f);   // y 压扁 -> 看起来是躺在地上的圈
    }

    private void Update()
    {
        if (_finished) return;

        _age += Time.deltaTime;

        float t = Mathf.Clamp01(_age / duration);
        float eased = 1f - (1f - t) * (1f - t);

        if (_ring != null && _caster != null)
        {
            // 跟着角色走（走位时法阵不会留在原地）
            transform.position = _caster.position + new Vector3(0f, -0.45f, 0f);

            float size = Mathf.Lerp(0.45f, diameter, eased);
            float squash = 0.11f / 0.2f;                       // 保持同一个压扁比例
            _ring.transform.localScale = new Vector3(size, size * squash, 1f);

            Color c = circleColor;
            c.a = circleColor.a * (1f - t * t * t);
            _ring.color = c;
        }

        _tickTimer -= Time.deltaTime;
        if (_tickTimer <= 0f && _age < duration)
        {
            _tickTimer += tickInterval;
            Tick();
        }

        if (_age >= duration + 0.15f)
        {
            _finished = true;
            Destroy(gameObject);
        }
    }

    private void Tick()
    {
        if (_health == null || !_health.IsAlive) return;

        float healed = _health.Heal(healPerTick);
        Ticks++;

        if (healed <= 0.01f) return;    // 满血：不跳字，免得刷屏

        TotalHealed += healed;

        Vector3 head = _health.transform.position + Vector3.up * 0.95f;
        FloatingText.Show($"+{Mathf.RoundToInt(healed)}", numberColor, head,
                          rise: 1.1f, duration: 0.85f, fontSize: 32);
    }

    private void OnDestroy()
    {
        // 只销毁 Ring 子物体，避免把整个场景带歪（本组件所在物体没有别的子物体，留着也安全）
        if (_ring != null) Destroy(_ring.gameObject);
    }
}
