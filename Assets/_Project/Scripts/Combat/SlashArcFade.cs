using UnityEngine;

/// <summary>
/// 一条挥砍弧光的生命周期：出现 → 张开 → 淡出 → 自毁。
///
/// 为什么不做成预制体：这个工程里凡是「只在运行时存在一小会儿」的东西都走代码生成
/// （词条面板、背包界面、Esc 菜单都是这条路），少一个资产就少一次引用丢失的机会 ——
/// 迁移到团结时已经吃过一次「内置资源 GUID 全废」的亏。
/// </summary>
public class SlashArcFade : MonoBehaviour
{
    private SpriteRenderer _renderer;
    private float _duration;
    private float _elapsed;
    private float _startAlpha;
    private float _startScale;
    private float _endScale;
    private float _spinDegrees;
    private Color _color;

    /// <summary>
    /// 由 <see cref="SlashEffect"/> 调用。不要手动 AddComponent 之后再忘了 Setup。
    /// </summary>
    public void Setup(Color color, float duration, float startScale, float endScale, float spinDegrees)
    {
        _renderer = GetComponent<SpriteRenderer>();
        _color = color;
        _duration = Mathf.Max(0.01f, duration);
        _startAlpha = color.a;
        _startScale = startScale;
        _endScale = endScale;
        _spinDegrees = spinDegrees;
        _elapsed = 0f;

        if (_renderer != null) _renderer.color = color;
        transform.localScale = Vector3.one * startScale;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);

        // 前 25% 张开，之后匀速淡出。用平方比线性更像「刀风」
        float grow = Mathf.Clamp01(t / 0.25f);
        float scale = Mathf.Lerp(_startScale, _endScale, 1f - (1f - grow) * (1f - grow));
        transform.localScale = Vector3.one * scale;

        if (_spinDegrees != 0f)
            transform.Rotate(0f, 0f, _spinDegrees * Time.deltaTime / _duration);

        if (_renderer != null)
        {
            Color c = _color;
            c.a = _startAlpha * (1f - t * t);
            _renderer.color = c;
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
