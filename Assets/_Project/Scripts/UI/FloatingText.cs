using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 世界坐标上的飘字（「+12」这种）。用在回血、伤害数字、拾取提示。
///
/// 为什么不做成场景里的 TextMesh：
///   · TextMesh 用的是旧版字体材质，中文字形要预烘进图集，运行时加载不到就全是方块；
///   · 而 UI Text + 系统动态字体在运行时一定能拿到中文字形（和菜单、词条面板同一套办法）。
/// 代价是它挂在屏幕空间的 Canvas 上，所以每帧要把世界坐标换算成屏幕坐标。
/// </summary>
public static class FloatingText
{
    private const int SortingOrder = 260;      // 压过 HUD(0)、背包(320 之后)、词条(300)、菜单(400) 都让开
    private const string CanvasName = "__FloatingTextCanvas";

    private static Canvas _canvas;
    private static Font _font;

    /// <summary>在世界坐标处弹一条字，会自己往上飘、淡出、销毁</summary>
    public static FloatingTextItem Show(string text, Color color, Vector3 worldPosition,
                                        float rise = 1.2f, float duration = 0.9f, int fontSize = 34)
    {
        if (string.IsNullOrEmpty(text)) return null;

        EnsureCanvas();

        var go = new GameObject("FloatingText", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);

        var label = go.AddComponent<Text>();
        label.text = text;
        label.font = _font;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;

        var outline = go.AddComponent<Outline>();   // 亮色字压在亮色地板上会看不清，描一圈深色边
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var item = go.AddComponent<FloatingTextItem>();
        item.Setup(worldPosition, rise, duration);
        return item;
    }

    private static void EnsureCanvas()
    {
        if (_canvas != null) return;

        var existing = GameObject.Find(CanvasName);
        if (existing != null && existing.TryGetComponent(out _canvas))
        {
            return;
        }

        var go = new GameObject(CanvasName, typeof(RectTransform));
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _font = LoadFont();
    }

    private static Font LoadFont()
    {
        // 和 GameMenuUI / RelicChoiceUI 用的是同一串候选：先系统中文，再兜底 Arial
        Font font = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "PingFang SC", "Arial" }, 32);

        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Debug.LogWarning("[FloatingText] 系统字体没找到，退回内置字体（中文可能显示成方块）");
        }

        return font;
    }
}

/// <summary>一条飘字的生命周期：跟着世界坐标飘上去，然后淡出销毁</summary>
public class FloatingTextItem : MonoBehaviour
{
    private RectTransform _rect;
    private Text _label;
    private Vector3 _anchor;
    private float _rise;
    private float _duration;
    private float _age;
    private Color _color;

    public void Setup(Vector3 worldPosition, float rise, float duration)
    {
        _anchor = worldPosition;
        _rise = rise;
        _duration = Mathf.Max(0.05f, duration);

        _rect = GetComponent<RectTransform>();
        _label = GetComponent<Text>();
        if (_label != null) _color = _label.color;

        UpdatePosition(0f);
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;   // 用不受暂停影响的时间：暂停菜单不该把飘字冻在半空
        float t = Mathf.Clamp01(_age / _duration);

        UpdatePosition(t);

        if (_label != null)
        {
            Color c = _color;
            c.a = _color.a * (1f - t * t);
            _label.color = c;
        }

        if (_age >= _duration) Destroy(gameObject);
    }

    private void UpdatePosition(float t)
    {
        if (_rect == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // 先往上飘，后半程开始回落的曲线：像被弹出一下
        float lift = _rise * (t - 0.35f * t * t);
        Vector3 world = _anchor + Vector3.up * lift;
        _rect.position = cam.WorldToScreenPoint(world);
    }
}
