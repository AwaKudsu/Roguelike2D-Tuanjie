using UnityEngine;

/// <summary>
/// 近战挥砍的弧光特效工厂。
///
/// 战士那套素材里有一张单独的「新月刀风」（Warrior_SlashArc），
/// 本来是个静态姿势图上的装饰，这里把它当成独立的 VFX 用：
///   · <see cref="Throw"/> —— 普通攻击 / 三段连击，一条直着往前飞的刀风
///   · <see cref="Spin"/>  —— 旋风斩，弧光绕着玩家转一圈
///
/// 全部运行时生成，不依赖预制体。sprite 传空就什么都不做（美术还没导入时不会报错）。
/// </summary>
public static class SlashEffect
{
    private const int SortingOrder = 20;

    /// <summary>
    /// 甩出一道往前飞的刀风。
    /// facing 决定往左还是往右（往左时整条弧水平镜像），tilt 是飞行角度（正数往上挑）。
    /// </summary>
    public static void Throw(Sprite arc, Vector2 origin, int facing, float scale, Color color,
                             float tilt = 0f, float speed = 9.5f, float duration = 0.24f,
                             float growTo = 1.25f)
    {
        GameObject go = Create(arc, origin, color, scale);
        if (go == null) return;

        // 镜像 + 反向倾角 = 左右两侧看起来对称
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.flipX = facing < 0;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, facing >= 0 ? tilt : -tilt);

        go.AddComponent<SlashWave>().Setup(
            velocity:    new Vector2(facing * speed, 0f),
            lifetime:    duration,
            color:       color,
            startScale:  scale * 0.8f,
            endScale:    scale * growTo);
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
