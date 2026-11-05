using UnityEngine;

/// <summary>
/// 近战挥砍的弧光特效工厂。
///
/// 战士那套素材里有一张单独的「新月刀风」（Warrior_SlashArc）。
/// 第一版把它当成往前飞的投射物（脱手 → 飞行 → 消失），
/// 但那个读起来像法师的气刃，不像剑挥出来的轨迹 —— 所以改成绕角色扫过：
///   · <see cref="Swing"/>  —— 普攻 / 三段连击，弧光贴着武器绕角色扫一道
///   · <see cref="Thrust"/> —— 突刺，不扫大圈，只在身前快速闪一道
///   · <see cref="Spin"/>   —— 旋风斩，弧光绕着玩家转一整圈
///
/// 全部运行时生成，不依赖预制体。sprite 传空就什么都不做（美术还没导入时不会报错）。
/// </summary>
public static class SlashEffect
{
    public const int SortingOrder = 20;

    /// <summary>
    /// 挥砍：弧光从 startAngle 扫过 sweep 度，绕角色转。
    /// 角度以「朝右」为基准，朝左时内部自动镜像，调用方不用为左右各写一套数字。
    /// startAngle 负数 = 从头顶那侧起手往下劈；正数 = 从下往上撩。
    /// </summary>
    public static void Swing(Sprite arc, Vector2 origin, int facing, float scale, Color color,
                             float startAngle = -75f, float sweep = 135f, float radius = 0.55f,
                             float duration = 0.18f)
    {
        if (arc == null) return;

        GameObject go = new GameObject("__SlashTrail");
        go.transform.position = origin;

        var trail = go.AddComponent<SlashTrail>();
        trail.SetSprite(arc);
        trail.Setup(
            origin:     origin,
            facing:     facing,
            radius:     radius,
            startAngle: startAngle,
            sweep:      sweep,
            startScale: scale * 0.8f,
            endScale:   scale * 1.1f,
            color:      color,
            duration:   duration);
    }

    /// <summary>
    /// 突刺：不扫大圈，弧光在身前一小段角度里一闪即逝，读作「扎出去」。
    /// distance 是弧光离身体的远近（突刺判定框长，所以放得比挥砍远）。
    /// </summary>
    public static void Thrust(Sprite arc, Vector2 origin, int facing, float scale, Color color,
                              float distance = 1.0f, float duration = 0.13f)
    {
        Swing(arc, origin, facing, scale, color,
              startAngle: -12f, sweep: 24f, radius: distance, duration: duration);
    }

    /// <summary>弧光绕玩家转一圈（旋风斩）。radius 是弧光离玩家的距离</summary>
    public static void Spin(Sprite arc, Vector2 center, float radius, float scale, Color color,
                            float duration = 0.42f)
    {
        if (arc == null) return;

        GameObject go = new GameObject("__SlashArc");
        go.transform.position = center + Vector2.up * radius * 0.35f;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = arc;
        renderer.color = color;
        renderer.sortingOrder = SortingOrder;

        var fade = go.AddComponent<SlashArcFade>();
        fade.Setup(color, duration, scale, scale * 1.35f, 720f);
    }
}
