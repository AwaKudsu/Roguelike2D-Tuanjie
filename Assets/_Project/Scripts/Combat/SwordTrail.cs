using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次挥剑属于哪一种。决定刀光从哪个角度扫到哪个角度、扫多快、多长。
/// </summary>
public enum SwordSwing
{
    Slash,   // 劈砍：从上往下扫
    Lift,    // 上挑：从下往上撩
    Thrust,  // 突刺：几乎不扫，只在身前拉一道横线
    Spin     // 旋风斩：绕身整整一圈还多
}

/// <summary>
/// 挥剑刀光 —— 一条贴着剑身扫过去的带状亮痕。
///
/// 为什么不用一张「刀光贴图」平移 / 旋转：
///   · 素材里那张新月弧是**一张固定形状的图**，转它只是让整张图绕着人转，
///     看起来像贴纸在飞，和剑本身的位置没有任何关系；
///   · 真正的刀光不是「一个东西在动」，而是**剑扫过的地方留下的余晖** ——
///     剑尖走过的轨迹连起来，才形成那条弧。
/// 所以这里不用任何贴图：每帧记下「剑根」和「剑尖」两个点，把连续几帧的点连成一条带子（网格），
/// 越旧的顶点越透明。剑扫得快，带子就宽；扫得慢，带子就窄 —— 形状是从动作里长出来的，不是画好的。
///
/// 三个顶点横截面（内缘 / 中线 / 外缘）让带子中间亮、两边淡，边缘不会像硬纸片。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SwordTrail : MonoBehaviour
{
    /// <summary>一帧的采样：剑根、剑身中线、剑尖，以及这一帧是哪一刻的。</summary>
    private struct Sample
    {
        public Vector2 Inner;
        public Vector2 Mid;
        public Vector2 Outer;
        public float Time;
    }

    /// <summary>刀光画在角色和敌人之间的那一层。战吼的圈和巨剑比它更高一层。</summary>
    public const int SortingOrder = 20;

    private readonly List<Sample> _samples = new List<Sample>();

    private Mesh _mesh;
    private Material _material;
    private MeshRenderer _renderer;

    private Vector2 _origin;
    private int _facing = 1;
    private Color _color = Color.white;

    private float _innerRadius;
    private float _outerRadius;
    private float _startAngle;
    private float _endAngle;
    private float _duration;
    private float _life;
    private float _deadline;
    private float _age;

    /// <summary>
    /// 放一道刀光。origin 是人物脚下（刀光绕这个点扫），facing 决定往哪边扫。
    /// </summary>
    public static SwordTrail Play(SwordSwing swing, Vector2 origin, int facing, Color color, float scale = 1f)
    {
        var go = new GameObject("__SwordTrail");
        var trail = go.AddComponent<SwordTrail>();

        float inner, outer, startAngle, endAngle, duration;

        switch (swing)
        {
            case SwordSwing.Lift:      // 上挑：从身前下方撩到头顶
                inner = 0.50f; outer = 0.86f;
                startAngle = -40f; endAngle = 88f; duration = 0.21f;
                break;

            case SwordSwing.Thrust:    // 突刺：几乎不扫，只往前捅出一条窄带
                inner = 0.60f; outer = 1.06f;
                startAngle = 6f; endAngle = -8f; duration = 0.12f;
                break;

            case SwordSwing.Spin:      // 旋风斩：绕身一整圈，带子比普攻窄一点（一整圈太厚会糊住角色）
                inner = 0.70f; outer = 1.04f;
                startAngle = 90f; endAngle = 450f; duration = 0.42f;
                break;

            default:                   // 劈砍：从头顶斜着劈到身前下方
                inner = 0.52f; outer = 0.92f;
                startAngle = 72f; endAngle = -38f; duration = 0.19f;
                break;
        }

        trail._origin = origin;
        trail._facing = facing >= 0 ? 1 : -1;
        trail._color = color;
        trail._innerRadius = inner * scale;
        trail._outerRadius = outer * scale;
        trail._startAngle = startAngle;
        trail._endAngle = endAngle;
        trail._duration = duration;

        // 留在带子上的只是「剑刚扫过去的那一段」：_life 比整段挥剑略短，
        // 最老的采样点会被丢掉，所以看到的是一条新月形的亮痕，而不是一整片扇形。
        // 0.85 是画出来对比过的值 —— 再短就只剩一小截尾巴，再长就摊成一整块扇面。
        // _deadline 是最后一帧之后余晖彻底散掉的时间。
        trail._life = Mathf.Max(0.06f, duration * 0.85f);
        trail._deadline = duration + 0.10f;

        return trail;
    }

    private void Awake()
    {
        _mesh = new Mesh { name = "SwordTrailMesh" };
        _mesh.MarkDynamic();

        var filter = GetComponent<MeshFilter>();
        filter.mesh = _mesh;

        _renderer = GetComponent<MeshRenderer>();
        _renderer.sortingOrder = SortingOrder;
        _renderer.receiveShadows = false;

        // Sprites/Default：走透明队列、Cull Off、吃顶点色 —— 2D 里最省事的选择
        Shader shader = Shader.Find("Sprites/Default");
        _material = new Material(shader) { name = "SwordTrailMat" };
        _renderer.sharedMaterial = _material;
    }

    private void Update()
    {
        _age += Time.deltaTime;

        if (_age <= _duration) Emit();

        Rebuild();
        Fade();
    }

    /// <summary>记下这一帧剑根与剑尖落在哪。缓动和挥砍动画同一条曲线（前快后慢）。</summary>
    private void Emit()
    {
        float t = Mathf.Clamp01(_age / _duration);
        float eased = 1f - (1f - t) * (1f - t);
        float angle = Mathf.Lerp(_startAngle, _endAngle, eased);

        // 面朝左时整条弧以竖直轴镜像：θ → 180° − θ
        if (_facing < 0) angle = 180f - angle;

        float rad = angle * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        _samples.Add(new Sample
        {
            Inner = _origin + dir * _innerRadius,
            Mid = _origin + dir * Mathf.Lerp(_innerRadius, _outerRadius, 0.55f),
            Outer = _origin + dir * _outerRadius,
            Time = _age
        });
    }

    /// <summary>把还活着的采样点连成一条带子。顶点多了就丢掉尾巴。</summary>
    private void Rebuild()
    {
        while (_samples.Count > 0 && _age - _samples[0].Time > _life) _samples.RemoveAt(0);

        _mesh.Clear();

        if (_samples.Count < 2)
        {
            return;
        }

        int count = _samples.Count;
        var vertices = new Vector3[count * 3];
        var colors = new Color[count * 3];
        var triangles = new int[(count - 1) * 12];

        for (int i = 0; i < count; i++)
        {
            Sample s = _samples[i];
            float age = _age - s.Time;
            float fade = Mathf.Clamp01(1f - age / _life);
            fade *= fade;   // 尾巴收得干脆一点，不然整条像一片雾

            int b = i * 3;
            vertices[b + 0] = s.Inner;
            vertices[b + 1] = s.Mid;
            vertices[b + 2] = s.Outer;

            Color c = _color;
            c.a = _color.a * fade;
            // 横截面上「两头透明、中线最亮」：不这么做，扫出来的是一整块扇形（看着像披萨），
            // 而不是刀刃划过留下的那条亮痕。外缘留一点点，剑尖那一下才不至于凭空断掉。
            colors[b + 0] = new Color(c.r, c.g, c.b, 0f);
            colors[b + 1] = c;
            colors[b + 2] = new Color(c.r, c.g, c.b, c.a * 0.15f);
        }

        for (int i = 0; i < count - 1; i++)
        {
            int v = i * 3;
            int n = (i + 1) * 3;
            int t = i * 12;

            // 内半边（内缘 → 中线）
            triangles[t + 0] = v + 0; triangles[t + 1] = v + 1; triangles[t + 2] = n + 1;
            triangles[t + 3] = v + 0; triangles[t + 4] = n + 1; triangles[t + 5] = n + 0;

            // 外半边（中线 → 外缘）
            triangles[t + 6] = v + 1; triangles[t + 7] = v + 2; triangles[t + 8] = n + 2;
            triangles[t + 9] = v + 1; triangles[t + 10] = n + 2; triangles[t + 11] = n + 1;
        }

        _mesh.vertices = vertices;
        _mesh.colors = colors;
        _mesh.triangles = triangles;
        _mesh.RecalculateBounds();
    }

    private void Fade()
    {
        if (_age > _deadline) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
    }
}
