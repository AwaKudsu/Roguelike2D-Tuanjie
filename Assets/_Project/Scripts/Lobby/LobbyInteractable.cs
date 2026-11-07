using UnityEngine;

/// <summary>
/// 大厅里「可以走过去按 E」的装饰：技能树告示板、书架、盔甲架……
///
/// 为什么单独做一个组件，而不是在大厅里写死几个坐标：
/// 装饰是**用编辑器菜单摆出来的**（`LobbyRoomBuilder`），坐标随时会挪。
/// 让装饰自己带着「交互半径 / 提示语 / 弹出什么」三件事，
/// 大厅只需要每帧问「最近的那个是谁」，加一件新装饰不用改大厅代码。
///
/// 字段是 public 的，跟 <see cref="LobbyPedestal"/> 一样由生成器直接赋值 ——
/// 这些装饰只在编辑器里生成一次，不需要 `[SerializeField]` 的 inspector 编辑体验。
///
/// 现在只做到「弹出占位面板」为止 —— 技能树之类的具体功能按需求先留空。
/// </summary>
[DisallowMultipleComponent]
public class LobbyInteractable : MonoBehaviour
{
    [Header("提示")]
    [Tooltip("站到跟前时底部提示行显示的文字，会拼成「按 E 查看技能树」")]
    public string prompt = "查看技能树";

    [Tooltip("以这个物体为圆心、多少单位内算「站到跟前」")]
    public float radius = 1.5f;

    [Header("交互内容（占位）")]
    public string title = "技能树";

    [TextArea(2, 5)]
    public string body = "这一块还没做。先把「走过去 → 按 E → 弹出面板」的路走通，内容下一轮再接。";

    [Header("靠近时的高亮（可空）")]
    public SpriteRenderer glow;
    public Color idleColor = new Color(1f, 0.86f, 0.35f, 0.10f);
    public Color nearColor = new Color(1f, 0.90f, 0.55f, 0.60f);

    /// <summary>玩家是不是站在交互范围内（只比平面距离：大厅是俯视的，y 就是深度）</summary>
    public bool IsPlayerNear(Vector3 playerPosition)
    {
        return Vector2.Distance(playerPosition, FootPoint) <= radius;
    }

    /// <summary>
    /// 「站在哪儿」以物体的脚底为准。
    /// 道具的轴心在贴图中心（`NewProp` 会把它往上抬半个高度），
    /// 直接拿 transform.position 当圆心的话，告示板这种一人多高的东西
    /// 圆心会跑到墙上去，人站在它面前反而「够不着」。
    /// </summary>
    private Vector3 FootPoint
    {
        get
        {
            SpriteRenderer renderer = GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) return transform.position;
            return new Vector3(transform.position.x, renderer.bounds.min.y, transform.position.z);
        }
    }

    /// <summary>靠近时把光圈点亮一点，离开时压暗</summary>
    public void SetHighlight(bool near)
    {
        if (glow == null) return;
        glow.color = near ? nearColor : idleColor;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.35f, 0.6f);
        Gizmos.DrawWireSphere(FootPoint, radius);
    }
}
