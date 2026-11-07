using UnityEngine;

/// <summary>
/// 大厅里站在暗处的「角色立牌」：一个职业一个人，鼠标点它就算选中。
///
/// 只放数据与零件引用，判定（鼠标点没点到）和流程（确认后才给走）都在 <see cref="LobbyRoom"/> 里。
/// </summary>
public class LobbyPedestal : MonoBehaviour
{
    [Tooltip("这个立牌代表哪个职业")]
    public ClassData classData;

    [Tooltip("站在那里的那个人（显示用，不参与战斗）")]
    public SpriteRenderer portrait;

    [Tooltip("鼠标移上去 / 选中时亮起来的地面光圈")]
    public SpriteRenderer glow;

    public Color glowIdle = new Color(1f, 0.86f, 0.35f, 0.10f);
    public Color glowHover = new Color(1f, 0.88f, 0.42f, 0.55f);
    public Color glowChosen = new Color(0.55f, 0.95f, 0.65f, 0.65f);

    [Tooltip("被点中时放大到多少（给个「被拿起来」的手感）")]
    public float hoverScale = 1.06f;

    [Tooltip("待机用的帧（会做很慢的呼吸），可以只放一张")]
    public Sprite[] idleFrames;

    private float _idleTimer;
    private float _currentScale = 1f;
    private Vector3 _baseScale = Vector3.one;
    private bool _alive = true;

    public string DisplayName =>
        classData != null && !string.IsNullOrEmpty(classData.className) ? classData.className : "未知职业";

    public bool IsPicked => classData != null;

    private void Awake()
    {
        _baseScale = transform.localScale;
        if (portrait == null) portrait = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>
    /// 雕像 / 活人。
    /// 确认选人时把这个立牌设成 false：人已经「走下来」变成玩家了，
    /// 台上再站一个就会看到两个人叠在一起。
    /// </summary>
    public void SetAlive(bool alive)
    {
        _alive = alive;
        if (portrait != null) portrait.enabled = alive;
        if (glow != null && !alive) glow.color = new Color(glowChosen.r, glowChosen.g, glowChosen.b, 0.06f);
    }

    public bool IsAlive => _alive;

    /// <summary>每帧由大厅调用：慢慢换帧，站着也有点气</summary>
    public void Breathe(float deltaTime)
    {
        if (portrait == null || !_alive) return;

        if (idleFrames != null && idleFrames.Length > 1)
        {
            _idleTimer += deltaTime;
            int index = Mathf.FloorToInt(_idleTimer / 0.62f) % idleFrames.Length;
            Sprite frame = idleFrames[index];
            if (frame != null && portrait.sprite != frame) portrait.sprite = frame;
        }
    }

    /// <summary>三种状态：暗着待命 / 鼠标移上去 / 已选中</summary>
    public void SetState(bool hovered, bool chosen)
    {
        if (glow != null)
        {
            Color target = chosen ? glowChosen : hovered ? glowHover : glowIdle;
            glow.color = Color.Lerp(glow.color, target, 0.25f);
        }

        float targetScale = hovered || chosen ? hoverScale : 1f;
        _currentScale = Mathf.Lerp(_currentScale, targetScale, 0.2f);
        transform.localScale = _baseScale * _currentScale;
    }

    /// <summary>
    /// 鼠标点选命中判定。
    /// 先看立绘的实际包围盒（点身体任何位置都算中），再退一步看脚底周围一个小圆 ——
    /// 只用「离脚底多近」的话，点在立牌上半身（头、帽子）会点不中，手感很别扭。
    /// </summary>
    public bool HitTest(Vector2 worldPoint, float radius)
    {
        if (portrait != null && portrait.sprite != null)
        {
            Bounds bounds = portrait.bounds;
            if (worldPoint.x >= bounds.min.x && worldPoint.x <= bounds.max.x &&
                worldPoint.y >= bounds.min.y && worldPoint.y <= bounds.max.y)
            {
                return true;
            }
        }

        return Vector2.Distance(worldPoint, (Vector2)transform.position) <= radius;
    }
}
