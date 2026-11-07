using UnityEngine;

/// <summary>
/// 用一张「扇环」贴图做刀光：贴图绕着自己的圆心转过去，就是一次挥砍。
///
/// 为什么不继续用网格（<see cref="SwordTrail"/>）：
///   · 网格是把每帧的剑尖位置连成带子，挥砍快的时候采样稀疏，带子会摊成一块厚板；
///   · 网格的粗细、透明度、弧度全要现算，调起来靠试；
///   · 而这张贴图本身已经画好了「薄两端、亮外缘」的形状，转一下就是效果。
///
/// 贴图约定（和 FX_SlashSweep.png 对应）：
///   · 弧的圆心 ≈ 画布正中心，弧在画布上半部分，跨约 110°；
///   · 弧的半径 ≈ 画布宽度的 0.435，所以「世界半径 / 0.435」= 该把贴图缩放成多大。
/// </summary>
public class SlashArcFx : MonoBehaviour
{
    /// <summary>贴图里弧半径占画布宽度的比例。换图时改这一个数。</summary>
    private const float ArcRadiusInTexture = 0.435f;

    private SpriteRenderer _renderer;
    private Color _baseColor;
    private float _age;
    private float _duration;
    private float _angleFrom;
    private float _angleTo;

    /// <summary>
    /// 在 origin 处放一次挥砍弧。owner 传玩家时贴图会挂成子物体，人走动时弧跟着走。
    /// arc 为空直接什么都不做（返回 null），调用方可以退回网格刀光。
    /// </summary>
    public static SlashArcFx Play(SwordSwing swing, Transform owner, Vector2 origin,
                                  int facing, Color color, float scale, Sprite arc)
    {
        if (arc == null) return null;

        SwordTrail.GetSwing(swing, out _, out float outer, out float startAngle, out float endAngle, out float duration);

        GameObject go = new GameObject("SlashArcFx");

        SlashArcFx fx = go.AddComponent<SlashArcFx>();
        fx._renderer = go.AddComponent<SpriteRenderer>();
        fx._renderer.sprite = arc;
        fx._renderer.sortingOrder = SwordTrail.SortingOrder + 1;   // 压过网格刀光，别被盖住
        fx._baseColor = color;
        fx._duration = Mathf.Max(0.10f, duration * 1.25f);          // 比挥砍略长，收尾有个淡出

        // 贴图放大到「弧半径 = 剑扫过的那圈半径」
        float radius = outer * scale;
        float size = radius / ArcRadiusInTexture;
        go.transform.localScale = new Vector3(size, size, 1f);

        // 面朝左时把角度镜像过去。贴图左右对称，所以不用再翻图
        if (facing < 0)
        {
            startAngle = 180f - startAngle;
            endAngle = 180f - endAngle;
        }

        // 贴图里的弧是「朝上」（+Y = 90°）画的，转到角度制要减 90
        fx._angleFrom = startAngle - 90f;
        fx._angleTo = endAngle - 90f;

        if (owner != null)
        {
            go.transform.SetParent(owner, false);
            go.transform.localPosition = owner.InverseTransformPoint(origin);
        }
        else
        {
            go.transform.position = origin;
        }

        go.transform.localRotation = Quaternion.Euler(0f, 0f, fx._angleFrom);
        fx._renderer.color = fx._baseColor;
        return fx;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _duration);

        // 前 70% 把弧扫过去（起手快、收尾慢），后 30% 原地淡掉
        float sweep = Mathf.Clamp01(t / 0.7f);
        sweep = 1f - (1f - sweep) * (1f - sweep);
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(_angleFrom, _angleTo, sweep));

        Color c = _baseColor;
        if (t > 0.55f)
        {
            float k = 1f - Mathf.Clamp01((t - 0.55f) / 0.45f);
            c.a = _baseColor.a * k * k;
        }
        _renderer.color = c;

        if (_age >= _duration) Destroy(gameObject);
    }
}
