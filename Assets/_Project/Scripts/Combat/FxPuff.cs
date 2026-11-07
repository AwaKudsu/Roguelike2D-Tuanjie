using UnityEngine;

/// <summary>
/// 一次性特效：一张图 + 一条寿命曲线（缩放 / 透明 / 旋转 / 漂移），到点自己销毁。
///
/// 打击火花、旋风漩涡、战吼冲击波都是「一张图在几十毫秒里变一下」，
/// 各写一个 MonoBehaviour 太啰嗦，所以统一到这里。要新加特效只需要一张图 + 一行 Spawn。
/// </summary>
public class FxPuff : MonoBehaviour
{
    private SpriteRenderer _renderer;
    private Color _color;
    private float _life;
    private float _age;

    private float _startScale;
    private float _endScale;
    private float _spin;
    private Vector2 _drift;

    /// <summary>
    /// 放一个一次性特效。
    /// </summary>
    /// <param name="sprite">要画的图，null 就什么都不放</param>
    /// <param name="worldPosition">世界坐标</param>
    /// <param name="color">颜色（alpha 会被寿命曲线再乘一次）</param>
    /// <param name="life">活多久（秒）</param>
    /// <param name="startScale">起始缩放</param>
    /// <param name="endScale">结束缩放</param>
    /// <param name="spin">每秒转多少度（正数逆时针）</param>
    /// <param name="drift">每秒漂移多少（世界坐标）</param>
    /// <param name="parent">挂给谁：挂上之后跟着它动，适合「贴在角色身上」的漩涡</param>
    /// <param name="rotationZ">初始朝向（度）。刀光弧要按挥砍角度摆，不能永远是横的</param>
    /// <param name="sortingOrder">和刀光（20）、战吼圈（21/22）同一套层级</param>
    public static FxPuff Spawn(Sprite sprite, Vector3 worldPosition, Color color, float life,
                               float startScale, float endScale,
                               float spin = 0f, Vector2 drift = default(Vector2),
                               Transform parent = null, float rotationZ = 0f,
                               int sortingOrder = SwordTrail.SortingOrder)
    {
        if (sprite == null) return null;

        var go = new GameObject("__FxPuff");
        var puff = go.AddComponent<FxPuff>();

        if (parent != null)
        {
            go.transform.SetParent(parent, false);
            go.transform.position = worldPosition;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        }
        else
        {
            go.transform.position = worldPosition;
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);
        }

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;

        puff._renderer = renderer;
        puff._color = color;
        puff._life = Mathf.Max(0.02f, life);
        puff._startScale = startScale;
        puff._endScale = endScale;
        puff._spin = spin;
        puff._drift = drift;
        puff.Apply(0f);

        return puff;
    }

    private void Update()
    {
        _age += Time.deltaTime;

        if (_age >= _life)
        {
            Destroy(gameObject);
            return;
        }

        Apply(_age / _life);

        if (_drift.sqrMagnitude > 0f)
        {
            transform.position += (Vector3)(_drift * Time.deltaTime);
        }
        if (Mathf.Abs(_spin) > 0.01f)
        {
            transform.Rotate(0f, 0f, _spin * Time.deltaTime);
        }
    }

    /// <summary>t = 0 刚出现，1 该消失了。透明度最后 40% 才开始掉，才有「闪一下」的感觉。</summary>
    private void Apply(float t)
    {
        float eased = 1f - (1f - t) * (1f - t);          // 先快后慢

        float scale = Mathf.Lerp(_startScale, _endScale, eased);
        transform.localScale = new Vector3(scale, scale, 1f);

        float fade = t < 0.6f ? 1f : Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);
        fade *= fade;                                    // 尾巴收得干脆，不留一层灰

        if (_renderer != null)
        {
            Color c = _color;
            c.a = _color.a * fade;
            _renderer.color = c;
        }
    }
}
