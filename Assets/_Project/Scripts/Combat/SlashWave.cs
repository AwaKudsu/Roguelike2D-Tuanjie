using UnityEngine;

/// <summary>
/// 会往前飞的刀风。
///
/// 为什么不直接用 SlashArcFade：那个是「原地张开再淡掉」，适合旋风斩绕身转；
/// 而普通挥砍的风刃要**直着往前推**，看起来像把气刃甩出去，所以位置要自己算。
///
/// 全部由 <see cref="SlashEffect.Throw"/> 现场建好并塞参数，不依赖预制体。
/// </summary>
public class SlashWave : MonoBehaviour
{
    private SpriteRenderer _renderer;
    private Vector2 _velocity;
    private float _lifetime = 0.24f;
    private float _age;
    private float _startAlpha = 1f;
    private float _startScale = 1f;
    private float _endScale = 1f;

    public void Setup(Vector2 velocity, float lifetime, Color color, float startScale, float endScale)
    {
        _renderer    = GetComponent<SpriteRenderer>();
        _velocity    = velocity;
        _lifetime    = Mathf.Max(0.02f, lifetime);
        _startAlpha  = color.a;
        _startScale  = startScale;
        _endScale    = endScale;

        transform.localScale = Vector3.one * startScale;
    }

    private void Update()
    {
        _age += Time.deltaTime;

        transform.position += (Vector3)(_velocity * Time.deltaTime);

        float t = Mathf.Clamp01(_age / _lifetime);

        // 越飞越大、越淡 —— 刀风是「甩出去」的，不该像贴纸一样保持原样
        float scale = Mathf.Lerp(_startScale, _endScale, t);
        transform.localScale = Vector3.one * scale;

        if (_renderer != null)
        {
            Color c = _renderer.color;
            c.a = _startAlpha * (1f - t * t);
            _renderer.color = c;
        }

        if (_age >= _lifetime) Destroy(gameObject);
    }
}
