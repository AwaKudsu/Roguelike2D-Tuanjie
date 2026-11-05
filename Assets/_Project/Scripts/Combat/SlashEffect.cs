using UnityEngine;

/// <summary>
/// 近战挥砍的弧光特效工厂。
///
/// 战士那套素材里有一张单独的「新月刀风」（Warrior_SlashArc），
/// 本来是个静态姿势图上的装饰，这里把它当成独立的 VFX 用：
///   · <see cref="Swing"/> —— 普通攻击 / 突刺，一道斜向的刀风
///   · <see cref="Spin"/>  —— 旋风斩，弧光绕着玩家转一圈
///
/// 全部运行时生成，不依赖预制体。sprite 传空就什么都不做（美术还没导入时不会报错）。
/// </summary>
public static class SlashEffect
{
    private const int SortingOrder = 20;

    /// <summary>斜着扫一道弧光。facing 决定朝左还是朝右（左挥是把整条弧镜像过来）</summary>
    public static void Swing(Sprite arc, Vector2 center, int facing, float scale, Color color, float duration = 0.16f)
    {
        GameObject go = Create(arc, center, color, scale);
        if (go == null) return;

        float angle = facing >= 0 ? -30f : 210f;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

        var fade = go.AddComponent<SlashArcFade>();
        fade.Setup(color, duration, scale * 0.75f, scale * 1.15f, 0f);
    }

    /// <summary>弧光绕玩家转一圈（旋风斩）。radius 是弧光离玩家的距离</summary>
    public static void Spin(Sprite arc, Vector2 center, float radius, float scale, Color color,
                            float duration = 0.42f)
    {
        GameObject go = Create(arc, center + Vector2.up * radius * 0.35f, color, scale);
        if (go == null) return;

        var fade = go.AddComponent<SlashArcFade>();
        fade.Setup(color, duration, scale, scale * 1.35f, 720f);
    }

    private static GameObject Create(Sprite arc, Vector2 center, Color color, float scale)
    {
        if (arc == null) return null;

        var go = new GameObject("__SlashArc");
        go.transform.position = center;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = arc;
        renderer.color = color;
        renderer.sortingOrder = SortingOrder;

        return go;
    }
}
