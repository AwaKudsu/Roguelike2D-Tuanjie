using UnityEngine;

/// <summary>
/// 挥砍残影：弧光围着角色转过去，像剑真的扫过一道。
///
/// 和上一版的区别 —— 上一版的弧光是脱手往前飞的投射物（像法师的气刃），
/// 手感上更像「发射」而不是「挥砍」。这一版把弧光挂在角色身上绕圈：
/// 剑扫到哪儿，弧光就在哪儿，扫完立刻消失。
///
/// 实现上分两层：
///   父物体放在角色身上，只负责旋转（弧光因此绕角色转，而不是绕自己转）；
///   子物体带 SpriteRenderer，沿朝向偏移 radius（弧光落在剑够得到的地方）。
/// </summary>
public class SlashTrail : MonoBehaviour
{
    private SpriteRenderer _arc;
    private float _duration = 0.18f;
    private float _startAngle;
    private float _sweep;
    private float _startScale = 1f;
    private float _endScale = 1.1f;
    private Color _color = Color.white;
    private float _age;

    /// <summary>
    /// 起手摆好姿势。参数里 angle/sweep 都是「朝右为正」的角度，朝左时内部自动镜像，
    /// 这样调用方不用为左右各写一套数字。
    /// </summary>
    public void Setup(Vector2 origin, int facing, float radius, float startAngle, float sweep,
                      float startScale, float endScale, Color color, float duration)
    {
        transform.position = origin;
        _duration = Mathf.Max(0.02f, duration);
        _startScale = Mathf.Max(0.01f, startScale);
        _endScale = Mathf.Max(0.01f, endScale);
        _color = color;

        // 朝左时把角度翻到另一侧、扫描方向反过来，效果才对称
        _startAngle = facing >= 0 ? startAngle : 180f - startAngle;
        _sweep = facing >= 0 ? sweep : -sweep;

        if (_arc == null)
        {
            var go = new GameObject("arc");
            go.transform.SetParent(transform, false);
            _arc = go.AddComponent<SpriteRenderer>();
            _arc.sortingOrder = SlashEffect.SortingOrder;
        }

        // 弧光放到「剑够得到的那一侧」
        _arc.transform.localPosition = new Vector3(facing >= 0 ? radius : -radius, 0f, 0f);
        _arc.flipX = facing < 0;

        Apply(0f);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _duration);
        Apply(t);

        if (t >= 1f) Destroy(gameObject);
    }

    private void Apply(float t)
    {
        // 前快后慢：刀挥出去是「唰」一下，收尾要顿住，不能匀速
        float eased = 1f - (1f - t) * (1f - t);

        transform.rotation = Quaternion.Euler(0f, 0f, _startAngle + _sweep * eased);

        float scale = Mathf.Lerp(_startScale, _endScale, eased);
        transform.localScale = new Vector3(scale, scale, 1f);

        if (_arc != null)
        {
            Color c = _color;
            c.a = _color.a * (1f - t * t);
            _arc.color = c;
        }
    }

    /// <summary>由 SlashEffect.Create 调用，把精灵塞进子物体</summary>
    public void SetSprite(Sprite sprite)
    {
        if (_arc == null)
        {
            var go = new GameObject("arc");
            go.transform.SetParent(transform, false);
            _arc = go.AddComponent<SpriteRenderer>();
            _arc.sortingOrder = SlashEffect.SortingOrder;
        }

        _arc.sprite = sprite;
    }
}
