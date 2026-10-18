using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 临时诊断脚本：进 Play 之后把「UI 到底在不在、画到了屏幕哪里」打进 Console / Editor.log。
///
/// 为什么需要它：HUD 不显示的时候，Unity 不报任何错，Unity 只在
///   - Canvas 的 rect / scale 被算成 0
///   - 元素被摆到屏幕外
///   - CanvasRenderer 被判成「透明、不用画」
///   - Canvas 组件自己被禁用
/// 这几种情况下默默不画。靠肉眼看 Inspector 分不出来，只能把运行时的真实数值打出来。
///
/// 用法：什么都不用挂，进 Play 就会自动跑（RuntimeInitializeOnLoadMethod）。
/// 输出前缀统一是 [HudProbe]，方便在 Console 里过滤。
/// </summary>
public class HudProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("__HudProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<HudProbe>();
    }

    private void Start()
    {
        Dump("进入 Play 第一帧");
        Invoke(nameof(Dump2), 1.0f);
        Invoke(nameof(Dump2), 2.5f);
    }

    private void Dump2() => Dump("运行 " + Time.time.ToString("F1") + " 秒");

    private void Dump(string when)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[HudProbe] ===== " + when + " =====");
        sb.AppendLine("[HudProbe] 屏幕 = " + Screen.width + " x " + Screen.height +
                      "  dpi=" + Screen.dpi + "  全屏=" + Screen.fullScreen);

        Camera cam = Camera.main;
        if (cam != null)
        {
            sb.AppendLine("[HudProbe] 主相机 '" + cam.name + "' 正交=" + cam.orthographic +
                          " size=" + cam.orthographicSize + " 像素矩形=" + cam.pixelRect +
                          " 视口=" + cam.rect + " 深度=" + cam.depth);
        }
        else
        {
            sb.AppendLine("[HudProbe] 主相机 = 无（World 视图仍可能由别的相机渲染）");
        }

        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        sb.AppendLine("[HudProbe] Canvas 数量 = " + canvases.Length);
        foreach (Canvas c in canvases)
        {
            if (c.gameObject.scene.name == null) continue;   // 跳过预制体/资源里的

            var rt = (RectTransform)c.transform;
            sb.AppendLine("[HudProbe] Canvas '" + PathOf(c.transform) + "'" +
                          " 渲染模式=" + c.renderMode +
                          " 组件启用=" + c.enabled +
                          " 对象激活=" + c.gameObject.activeInHierarchy +
                          " 缩放因子=" + c.scaleFactor.ToString("F3") +
                          " 根Canvas=" + c.isRootCanvas +
                          " 排序序号=" + c.sortingOrder);
            sb.AppendLine("[HudProbe]     RectTransform: 世界坐标=" + V3(rt.position) +
                          " 局部缩放=" + V3(rt.localScale) +
                          " rect=" + rt.rect +
                          " 锚点=" + rt.anchorMin + "~" + rt.anchorMax +
                          " pivot=" + rt.pivot);
            sb.AppendLine("[HudProbe]     屏幕矩形 = " + ScreenRect(rt));

            Image[] images = c.GetComponentsInChildren<Image>(true);
            sb.AppendLine("[HudProbe]     子 Image 数 = " + images.Length);
            foreach (Image img in images)
            {
                var irt = (RectTransform)img.transform;
                CanvasRenderer cr = img.GetComponent<CanvasRenderer>();
                sb.AppendLine("[HudProbe]       '" + PathOf(img.transform) + "'" +
                              " 组件启用=" + img.enabled +
                              " 对象激活=" + img.gameObject.activeInHierarchy +
                              " 颜色=" + C4(img.color) +
                              " sprite=" + (img.sprite == null ? "★null" : img.sprite.name) +
                              " 类型=" + img.type +
                              " fillAmount=" + img.fillAmount.ToString("F2") +
                              " cull=" + (cr == null ? "无CanvasRenderer" : cr.cull.ToString()) +
                              " 屏幕矩形=" + ScreenRect(irt));
            }
        }
        DumpShader(sb, canvases);
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 查「布局全对但就是不画」最常见的第二类原因：UI 的默认材质 / 着色器坏了。
    /// 团结（Tuanjie）把 Unity 的内置资源整包重打了，UI 默认着色器如果没跟着过来，
    /// 表现就是「一切正常、Console 没红字、就是一个像素都不画」。
    /// </summary>
    private static void DumpShader(StringBuilder sb, Canvas[] canvases)
    {
        sb.AppendLine("[HudProbe] ----- 材质 / 着色器 -----");

        Material def = Canvas.GetDefaultCanvasMaterial();
        sb.AppendLine("[HudProbe] Canvas.GetDefaultCanvasMaterial() = " + (def == null ? "★null" : def.name) +
                      " shader=" + (def == null || def.shader == null ? "★null" : def.shader.name) +
                      " 受支持=" + (def != null && def.shader != null && def.shader.isSupported));

        Shader uiDefault = Shader.Find("UI/Default");
        sb.AppendLine("[HudProbe] Shader.Find(\"UI/Default\") = " +
                      (uiDefault == null ? "★null" : uiDefault.name + " 受支持=" + uiDefault.isSupported));

        Shader spritesDefault = Shader.Find("Sprites/Default");
        sb.AppendLine("[HudProbe] Shader.Find(\"Sprites/Default\") = " +
                      (spritesDefault == null ? "★null" : spritesDefault.name + " 受支持=" + spritesDefault.isSupported));

        foreach (Canvas c in canvases)
        {
            if (c.gameObject.scene.name == null) continue;
            Image img = c.GetComponentInChildren<Image>(false);
            if (img == null) continue;

            Material m = img.material;
            sb.AppendLine("[HudProbe] 首个 Image '" + PathOf(img.transform) + "' 的 material = " +
                          (m == null ? "★null" : m.name) +
                          " shader=" + (m == null || m.shader == null ? "★null" : m.shader.name) +
                          " 受支持=" + (m != null && m.shader != null && m.shader.isSupported));

            CanvasRenderer cr = img.GetComponent<CanvasRenderer>();
            if (cr != null)
            {
                sb.AppendLine("[HudProbe] 该 Image 的 CanvasRenderer: alpha=" + cr.GetAlpha().ToString("F2") +
                              " 继承alpha=" + cr.GetInheritedAlpha().ToString("F2") +
                              " 颜色=" + C4(cr.GetColor()) +
                              " 材质数=" + cr.materialCount);
            }
            break;
        }
    }

    private static string ScreenRect(RectTransform rt)
    {
        Vector2 min = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.min));
        Vector2 max = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.max));
        float w = Mathf.Abs(max.x - min.x);
        float h = Mathf.Abs(max.y - min.y);
        return "左下(" + min.x.ToString("F1") + "," + min.y.ToString("F1") + ")" +
               " 右上(" + max.x.ToString("F1") + "," + max.y.ToString("F1") + ")" +
               " 尺寸(" + w.ToString("F1") + "x" + h.ToString("F1") + ")" +
               (w < 1f || h < 1f ? " ★尺寸接近0" : "");
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        Transform cur = t.parent;
        while (cur != null) { path = cur.name + "/" + path; cur = cur.parent; }
        return path;
    }

    private static string V3(Vector3 v) => "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";

    private static string C4(Color c) => "RGBA(" + Mathf.RoundToInt(c.r * 255f) + "," + Mathf.RoundToInt(c.g * 255f) + "," +
                                         Mathf.RoundToInt(c.b * 255f) + "," + Mathf.RoundToInt(c.a * 255f) + ")";
}
