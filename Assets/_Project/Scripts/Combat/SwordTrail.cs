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
/// 所以这里不用任何贴图：每帧记下剑扫到哪个角度，把连续几帧的方向和半径连成一条带子（网格），
/// 越旧的顶点越透明。剑扫得快，带子就宽；扫得慢，带子就窄 —— 形状是从动作里长出来的，不是画好的。
///
/// 横截面是**五条带**（内柔光 / 内芯 / 亮核 / 外刃 / 外柔光裙）：
/// 中间亮到发白、两侧渐隐，最外面还有一圈 1.26 倍半径的淡光，
/// 这样扫出去的是一条「有厚度的高光带」，而不是一块硬边扇面（以前只有三层，边缘像纸片）。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SwordTrail : MonoBehaviour
{
    /// <summary>一帧的采样：从哪个点扫向哪个方向，以及这一帧是哪一刻的。</summary>
    private struct Sample
    {
        public Vector2 Origin;
        public Vector2 Dir;
        public float Time;
    }

    /// <summary>刀光画在角色和敌人之间的那一层。战吼的圈和巨剑比它更高一层。</summary>
    public const int SortingOrder = 20;

    /// <summary>横截面的五条半径位置（0=剑根一侧，1=剑尖一侧）与各自的透明度。</summary>
    // 五层横截面的「半径位置」与「不透明度」。
    // 关键是别让它变成一块厚板：真实刀光的亮线贴在剑锋那一侧（外缘），
    // 往内是很快散掉的余晖。所以第 4 层（1.00 外缘）最亮，往里迅速衰减。
    private static readonly float[] BandT = { 0.00f, 0.55f, 0.86f, 1.00f, 1.12f };
    private static readonly float[] BandAlpha = { 0f, 0.08f, 0.42f, 0.95f, 0f };

    private const int Bands = 5;

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
    ///
    /// owner 是挥剑的那个人：刀光会挂成它的子物体，顶点也按它的**局部坐标**算。
    /// 不这么做的话，刀光被生成在当时的世界上就不再动了 —— 角色一走动，
    /// 刚刚扫出去的那道弧会留在原地，和人叠在一起或者被甩在身后，看着像另一个人的特效。
    /// </summary>
    public static SwordTrail Play(SwordSwing swing, Transform owner, Vector2 origin, int facing, Color color, float scale = 1f)
    {
        var go = new GameObject("__SwordTrail");
        var trail = go.AddComponent<SwordTrail>();

        GetSwing(swing, out float inner, out float outer, out float startAngle, out float endAngle, out float duration);

        trail._origin = origin;
        trail._facing = facing >= 0 ? 1 : -1;
        trail._color = color;
        trail._innerRadius = inner * scale;
        trail._outerRadius = outer * scale;
        trail._startAngle = startAngle;
        trail._endAngle = endAngle;
        trail._duration = duration;

        // 挂到挥剑的人身上：父物体一动，整条带子跟着动，刀光才一直贴着剑。
        // 顶点用的是局部坐标，所以这里把世界坐标的起点换算进父空间的局部坐标。
        if (owner != null)
        {
            go.transform.SetParent(owner, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            trail._origin = owner.InverseTransformPoint(origin);
        }

        // 留在带子上的只是「剑刚扫过去的那一段」：_life 比整段挥剑短，
        // 最老的采样点会被丢掉，所以看到的是一条新月形的亮痕，而不是一整片扇形。
        // 0.55 是又收窄过一次的值 —— 0.85 时三个采样点还全活着，
        // 110° 的挥砍被摊成一块又厚又匀的弧形，看着像「一节粗环」。
        // _deadline 是最后一帧之后余晖彻底散掉的时间。
        trail._life = Mathf.Max(0.05f, duration * 0.55f);
        trail._deadline = duration + 0.10f;

        return trail;
    }

    /// <summary>四种挥法的几何参数。刀光轨迹、那道「一闪而过的弧」、以及贴图版刀光用的是同一份数据。</summary>
    public static void GetSwing(SwordSwing swing, out float inner, out float outer,
                                out float startAngle, out float endAngle, out float duration)
    {
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
    }

    /// <summary>
    /// 挥砍的「那一瞬间」：贴一张画好的月牙弧，0.15 秒内胀大并消失。
    ///
    /// 为什么网格刀光之外还要这张图：
    ///   · 网格刀光是**连续几帧**扫出来的轨迹，起步那一下只有一两个采样点，几乎没有形状；
    ///   · 而玩家感知「砍出去了」靠的正是起步那一瞬间 —— 有形的弧比轨迹更容易被看见。
    /// 两者叠加：弧给「起手的力度」，轨迹给「扫过的余晖」。
    ///
    /// arc 贴图约定：横躺的月牙，凸边上沿朝 +Y、宽度 = 1 世界单位。
    /// 所以旋转角取「挥砍中段角 − 90°」，凸边就正好对着剑扫出去的方向。
    /// </summary>
    public static void FlashArc(SwordSwing swing, Transform owner, Vector2 origin, int facing,
                                Color color, float scale, Sprite arc)
    {
        if (arc == null) return;

        GetSwing(swing, out _, out float outer, out float startAngle, out float endAngle, out _);

        float mid = (startAngle + endAngle) * 0.5f;
        if (facing < 0) mid = 180f - mid;

        float rad = mid * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        float radius = outer * scale;
        Vector2 at = origin + dir * radius * 0.92f;

        // 月牙宽度 = 1 单位，撑到扫过的那段直径（2 倍半径）就正好贴着剑尖
        float width = radius * 2f;

        // 先铺一层更大更淡的当外发光，再盖一层实的。
        // 只有一层时贴图边缘就是「剪出来的一块」，加这一层成本极低、观感差很多
        FxPuff.Spawn(
            arc,
            at,
            new Color(color.r, color.g, color.b, color.a * 0.45f),
            life: 0.22f,
            startScale: width * 0.96f,
            endScale: width * 1.30f,
            rotationZ: mid - 90f,
            parent: owner,
            sortingOrder: SortingOrder - 2);

        FxPuff.Spawn(
            arc,
            at,
            color,
            life: 0.16f,
            startScale: width * 0.84f,
            endScale: width * 1.06f,
            rotationZ: mid - 90f,
            parent: owner,
            sortingOrder: SortingOrder - 1);
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

    /// <summary>记下这一帧剑扫到哪个角度。缓动和挥砍动画同一条曲线（前快后慢）。</summary>
    private void Emit()
    {
        float t = Mathf.Clamp01(_age / _duration);
        float eased = 1f - (1f - t) * (1f - t);
        float angle = Mathf.Lerp(_startAngle, _endAngle, eased);

        // 面朝左时整条弧以竖直轴镜像：θ → 180° − θ
        if (_facing < 0) angle = 180f - angle;

        float rad = angle * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        _samples.Add(new Sample { Origin = _origin, Dir = dir, Time = _age });
    }

    /// <summary>把还活着的采样点连成一条五层的带子。顶点多了就丢掉尾巴。</summary>
    private void Rebuild()
    {
        while (_samples.Count > 0 && _age - _samples[0].Time > _life) _samples.RemoveAt(0);

        _mesh.Clear();

        if (_samples.Count < 2)
        {
            return;
        }

        int count = _samples.Count;
        int vertexCount = count * Bands;
        int quadCount = (count - 1) * (Bands - 1);

        var vertices = new Vector3[vertexCount];
        var colors = new Color[vertexCount];
        var triangles = new int[quadCount * 6];

        // 亮核偏白：纯用 slashColor 的话，浅色地面上几乎看不见
        Color core = Color.Lerp(_color, Color.white, 0.55f);

        for (int i = 0; i < count; i++)
        {
            Sample s = _samples[i];

            float age = _age - s.Time;
            float fade = Mathf.Clamp01(1f - age / _life);
            fade *= fade;   // 尾巴收得干脆一点，不然整条像一片雾

            int b = i * Bands;

            for (int band = 0; band < Bands; band++)
            {
                float radius = Mathf.Lerp(_innerRadius, _outerRadius, BandT[band]);
                vertices[b + band] = s.Origin + s.Dir * radius;

                // 亮核贴在外缘（剑锋扫过的轨迹），不是带子正中间
                Color c = band == 3 ? core : _color;
                c.a = _color.a * BandAlpha[band] * fade;
                colors[b + band] = c;
            }
        }

        for (int i = 0; i < count - 1; i++)
        {
            int v = i * Bands;
            int n = (i + 1) * Bands;
            int t = i * (Bands - 1) * 6;

            for (int band = 0; band < Bands - 1; band++)
            {
                int a0 = v + band;
                int a1 = v + band + 1;
                int b0 = n + band;
                int b1 = n + band + 1;

                triangles[t + 0] = a0; triangles[t + 1] = a1; triangles[t + 2] = b1;
                triangles[t + 3] = a0; triangles[t + 4] = b1; triangles[t + 5] = b0;
                t += 6;
            }
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
